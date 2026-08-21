using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Geom;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace PdfRemediation.Api.Services;

// Represents a single extracted element (text fragment or image) with geometry and style.
public class PdfElement
{
    public float Y { get; set; }
    public float X { get; set; }
    public float EndX { get; set; }
    public string Text { get; set; } = "";
    public byte[]? ImageBytes { get; set; }
    public float ImageWidth { get; set; }
    public float ImageHeight { get; set; }
    public bool IsImage => ImageBytes != null;
    public float FontSize { get; set; }
    public bool IsBold { get; set; }
}

// Listener that captures both text and image rendering events.
public class StructuralEventListener : IEventListener
{
    public List<PdfElement> Elements { get; } = new List<PdfElement>();

    public void EventOccurred(IEventData data, EventType type)
    {
        if (type == EventType.RENDER_TEXT)
        {
            var textInfo = (TextRenderInfo)data;
            var text = textInfo.GetText();
            if (string.IsNullOrWhiteSpace(text)) return;

            // Font size approximation – use ascent/descent when possible.
            var ascent = textInfo.GetAscentLine().GetStartPoint();
            var descent = textInfo.GetDescentLine().GetStartPoint();
            float approxSize = ascent.Get(1) - descent.Get(1);
            if (approxSize <= 0) approxSize = textInfo.GetFontSize();

            // Determine boldness from font name (heuristic).
            var font = textInfo.GetFont();
            var fontProgram = font?.GetFontProgram();
            var fontName = fontProgram?.GetFontNames()?.GetFontName()?.ToLowerInvariant() ?? "";
            bool isBold = fontName.Contains("bold") || fontName.Contains("black") || fontName.Contains("heavy");

            var startPoint = textInfo.GetBaseline().GetStartPoint();
            var endPoint = textInfo.GetBaseline().GetEndPoint();

            Elements.Add(new PdfElement
            {
                Text = text,
                Y = startPoint.Get(1),
                X = startPoint.Get(0),
                EndX = endPoint.Get(0),
                FontSize = approxSize,
                IsBold = isBold
            });
        }
        else if (type == EventType.RENDER_IMAGE)
        {
            try
            {
                var imageInfo = (ImageRenderInfo)data;
                var image = imageInfo.GetImage();
                if (image == null) return;

                var ctm = imageInfo.GetImageCtm();
                float width = Math.Abs(ctm.Get(Matrix.I11));   // rendered width in user units (points)
                float height = Math.Abs(ctm.Get(Matrix.I22)); // rendered height in points
                float x = ctm.Get(Matrix.I31);
                float y = ctm.Get(Matrix.I32) + height; // top edge for sorting

                Elements.Add(new PdfElement
                {
                    ImageBytes = image.GetImageBytes(),
                    Y = y,
                    X = x,
                    ImageWidth = width,
                    ImageHeight = height
                });
            }
            catch { }
        }
    }

    public ICollection<EventType> GetSupportedEvents()
    {
        return new HashSet<EventType> { EventType.RENDER_TEXT, EventType.RENDER_IMAGE };
    }
}

// Simple fragment used during line assembly.
public class MergedFragment
{
    public float X { get; set; }
    public float EndX { get; set; }
    public string Text { get; set; } = "";
    public float FontSize { get; set; }
    public bool IsBold { get; set; }
}

// Represents a line of text after merging fragments that belong to the same column.
public class AssembledLine
{
    public float Y { get; set; }
    public List<MergedFragment> Columns { get; set; } = new List<MergedFragment>();
    public bool IsTable => Columns.Count >= 2; // consider 2+ columns a potential table row
    public float MaxFontSize => Columns.Max(c => c.FontSize);
    public bool AnyBold => Columns.Any(c => c.IsBold);
    public string JoinedText => string.Join(" ", Columns.Select(c => c.Text));
}

public class HeuristicPdfEngine
{
    public HeuristicPdfEngine() { }

    public class RemediationOptions
    {
        public bool NormalizeMetadata { get; set; } = true;
        public bool TagLanguage { get; set; } = true;
        public bool AutoTagStructure { get; set; } = false;
    }

    public byte[] ApplyRemediation(byte[] pdfBytes, RemediationOptions options)
    {
        using var outputStream = new MemoryStream();
        var pdfWriter = new PdfWriter(outputStream);
        var pdfDoc = new PdfDocument(pdfWriter);

        if (options.NormalizeMetadata)
        {
            var info = pdfDoc.GetDocumentInfo();
            info.SetTitle("Remediated Document");
            info.SetCreator("PDF Remediation Suite API");
            info.SetAuthor("Automated System");
        }

        if (options.TagLanguage)
        {
            var catalog = pdfDoc.GetCatalog();
            catalog.SetLang(new PdfString("en-US"));
            var viewerPreferences = new PdfViewerPreferences();
            viewerPreferences.SetDisplayDocTitle(true);
            catalog.SetViewerPreferences(viewerPreferences);
        }

        pdfDoc.SetTagged();
        var layoutDoc = new iText.Layout.Document(pdfDoc);

        using var sourceReader = new PdfReader(new MemoryStream(pdfBytes));
        using var sourceDoc = new PdfDocument(sourceReader);

        for (int pageNum = 1; pageNum <= sourceDoc.GetNumberOfPages(); pageNum++)
        {
            var page = sourceDoc.GetPage(pageNum);
            var listener = new StructuralEventListener();
            var processor = new PdfCanvasProcessor(listener);
            processor.ProcessPageContent(page);

            var textFragments = listener.Elements.Where(e => !e.IsImage).ToList();

            // -----------------------------------------------------------------
            // 1️⃣ Determine baseline margin and base font size for the page.
            // -----------------------------------------------------------------
            float baseMargin = 0f;
            float baseFontSize = 10f;
            if (textFragments.Any())
            {
                baseMargin = textFragments
                    .GroupBy(f => Math.Round(f.X / 5) * 5)
                    .OrderByDescending(g => g.Count())
                    .First().Key is double v ? (float)v : 0f;

                baseFontSize = textFragments
                    .GroupBy(f => Math.Round(f.FontSize))
                    .OrderByDescending(g => g.Count())
                    .First().Key is double fs ? (float)fs : 10f;
            }

            // ---------------------------------------------------------------
            // 2️⃣ Group fragments by Y (line) and merge fragments into columns.
            // ---------------------------------------------------------------
            var lineGroups = textFragments
                .GroupBy(e => Math.Round(e.Y / 3) * 3) // tolerance 3pt vertically
                .Select(g => new { Y = (float)g.Key, Fragments = g.OrderBy(e => e.X).ToList() })
                .ToList();

            var assembledLines = new List<AssembledLine>();
            foreach (var lg in lineGroups)
            {
                var line = new AssembledLine { Y = lg.Y };
                if (!lg.Fragments.Any()) continue;

                // Merge fragments that are close horizontally (<=15pt gap) into the same column.
                var current = new MergedFragment
                {
                    X = lg.Fragments[0].X,
                    EndX = lg.Fragments[0].EndX,
                    Text = lg.Fragments[0].Text,
                    FontSize = lg.Fragments[0].FontSize,
                    IsBold = lg.Fragments[0].IsBold
                };

                for (int i = 1; i < lg.Fragments.Count; i++)
                {
                    var next = lg.Fragments[i];
                    float gap = next.X - current.EndX;
                    if (gap < 15f) // same column, just whitespace or kerning
                    {
                        current.Text += (gap > 2f ? " " : "") + next.Text;
                        current.EndX = next.EndX;
                        if (next.FontSize > current.FontSize) current.FontSize = next.FontSize;
                        if (next.IsBold) current.IsBold = true;
                    }
                    else // new column
                    {
                        line.Columns.Add(current);
                        current = new MergedFragment
                        {
                            X = next.X,
                            EndX = next.EndX,
                            Text = next.Text,
                            FontSize = next.FontSize,
                            IsBold = next.IsBold
                        };
                    }
                }
                line.Columns.Add(current);
                assembledLines.Add(line);
            }

            // ---------------------------------------------------------------
            // 3️⃣ Sort lines top‑to‑bottom and images top‑to‑bottom.
            // ---------------------------------------------------------------
            var sortedLines = assembledLines.OrderByDescending(l => l.Y).ToList();
            var sortedImages = listener.Elements.Where(e => e.IsImage).OrderByDescending(e => e.Y).ToList();

            int lineIdx = 0, imgIdx = 0;
            var currentParagraph = new List<MergedFragment>();
            float currentX = baseMargin;

            // Table construction helpers.
            var tableRowsBuffer = new List<AssembledLine>();
            iText.Layout.Element.Table? currentTable = null;
            int currentTableCols = 0;

            void FlushParagraph()
            {
                if (!currentParagraph.Any()) return;
                var p = new iText.Layout.Element.Paragraph();
                float maxFont = currentParagraph.Max(f => f.FontSize);
                bool anyBold = currentParagraph.Any(f => f.IsBold);
                string full = string.Join(" ", currentParagraph.Select(f => f.Text)).Trim();
                bool isShort = full.Length < 70;

                // Header detection based on relative font size.
                if (maxFont >= baseFontSize + 3f)
                {
                    p.GetAccessibilityProperties().SetRole("H1");
                    p.SetFontSize(14).SetBold();
                }
                else if (anyBold && isShort && maxFont >= baseFontSize)
                {
                    p.GetAccessibilityProperties().SetRole("H2");
                    p.SetFontSize(12).SetBold();
                }
                else
                {
                    p.GetAccessibilityProperties().SetRole("P");
                    p.SetFontSize(11);
                }

                foreach (var frag in currentParagraph)
                {
                    var txt = new iText.Layout.Element.Text(frag.Text + " ");
                    if (frag.IsBold) txt.SetBold();
                    txt.SetFontSize(frag.FontSize);
                    p.Add(txt);
                }

                // Apply indentation only when it exceeds a modest threshold.
                float indent = currentX - baseMargin;
                if (indent > 5f) p.SetMarginLeft(indent);

                layoutDoc.Add(p);
                currentParagraph.Clear();
            }

            void FlushTable()
            {
                if (currentTable != null)
                {
                    currentTable.GetAccessibilityProperties().SetRole("Table");
                    layoutDoc.Add(currentTable);
                    currentTable = null;
                    currentTableCols = 0;
                }
            }

            // Helper to start a new table when we have buffered rows.
            void StartTableIfNeeded()
            {
                if (tableRowsBuffer.Count >= 2) // need at least 2 rows to be a real table
                {
                    FlushParagraph();
                    currentTableCols = tableRowsBuffer.Max(r => r.Columns.Count);
                    currentTable = new iText.Layout.Element.Table(currentTableCols);
                    currentTable.SetWidth(iText.Layout.Properties.UnitValue.CreatePercentValue(100));
                    foreach (var row in tableRowsBuffer)
                    {
                        foreach (var col in row.Columns)
                        {
                            var cell = new iText.Layout.Element.Cell();
                            var txt = new iText.Layout.Element.Text(col.Text);
                            if (col.IsBold) txt.SetBold();
                            txt.SetFontSize(col.FontSize);
                            cell.Add(new iText.Layout.Element.Paragraph(txt));
                            currentTable.AddCell(cell);
                        }
                        // Pad missing cells if this row has fewer columns.
                        for (int pad = row.Columns.Count; pad < currentTableCols; pad++)
                            currentTable.AddCell(new iText.Layout.Element.Cell());
                    }
                    layoutDoc.Add(currentTable);
                }
                tableRowsBuffer.Clear();
            }

            // ---------------------------------------------------------------
            // 4️⃣ Merge the streams of lines and images in visual order.
            // ---------------------------------------------------------------
            while (lineIdx < sortedLines.Count || imgIdx < sortedImages.Count)
            {
                bool takeImage = false;
                if (imgIdx < sortedImages.Count && lineIdx < sortedLines.Count)
                    takeImage = sortedImages[imgIdx].Y > sortedLines[lineIdx].Y; // higher Y means earlier on page (origin bottom)
                else if (imgIdx < sortedImages.Count)
                    takeImage = true;

                if (takeImage)
                {
                    // Images are atomic – flush any pending text structures.
                    FlushParagraph();
                    FlushTable();
                    var elem = sortedImages[imgIdx++];
                    try
                    {
                        var imgData = iText.IO.Image.ImageDataFactory.Create(elem.ImageBytes);
                        var img = new iText.Layout.Element.Image(imgData);
                        // Preserve exact dimensions.
                        if (elem.ImageWidth > 0 && elem.ImageHeight > 0)
                        {
                            img.SetWidth(elem.ImageWidth);
                            img.SetHeight(elem.ImageHeight);
                        }
                        else
                        {
                            img.SetMaxWidth(475f);
                        }
                        img.GetAccessibilityProperties().SetRole("Figure");
                        img.GetAccessibilityProperties().SetAlternateDescription("Extracted Figure");
                        float indent = elem.X - baseMargin;
                        if (indent > 5f) img.SetMarginLeft(indent);
                        layoutDoc.Add(img);
                    }
                    catch { }
                }
                else // process a line of text
                {
                    var line = sortedLines[lineIdx++];

                    // Determine if this line should be part of a table.
                    if (line.IsTable)
                    {
                        // Buffer table rows – we will render only after we see a non‑table line.
                        tableRowsBuffer.Add(line);
                    }
                    else
                    {
                        // Non‑table line – first flush any buffered table rows.
                        StartTableIfNeeded();

                        // Header detection: if this line's max font is significantly larger than base,
                        // ensure it starts a fresh paragraph (i.e., don't concatenate with previous).
                        bool isHeader = line.MaxFontSize >= baseFontSize + 3f;
                        if (isHeader && currentParagraph.Any())
                        {
                            // Flush the previous paragraph before starting the header.
                            FlushParagraph();
                        }

                        // Merge the column fragments into the running paragraph.
                        foreach (var frag in line.Columns)
                        {
                            if (currentParagraph.Any())
                            {
                                var last = currentParagraph.Last();
                                // If the previous fragment ends with punctuation, start a new paragraph.
                                char lastChar = last.Text.Length > 0 ? last.Text.Last() : ' ';
                                if (lastChar != '.' && lastChar != '?' && lastChar != '!' && lastChar != ':' && frag.Text.Length > 20)
                                {
                                    currentParagraph.Add(frag);
                                }
                                else
                                {
                                    FlushParagraph();
                                    currentParagraph.Add(frag);
                                    currentX = frag.X;
                                }
                            }
                            else
                            {
                                currentParagraph.Add(frag);
                                currentX = frag.X;
                            }
                        }
                    }
                }
            }

            // End of page – flush any remaining structures.
            FlushParagraph();
            StartTableIfNeeded(); // this will render a table if we ended on a table row.

            if (pageNum < sourceDoc.GetNumberOfPages())
                layoutDoc.Add(new iText.Layout.Element.AreaBreak(iText.Layout.Properties.AreaBreakType.NEXT_PAGE));
        }

        layoutDoc.Close();
        return outputStream.ToArray();
    }
}
