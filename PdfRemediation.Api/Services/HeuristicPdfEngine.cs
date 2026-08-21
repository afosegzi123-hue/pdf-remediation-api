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

            var ascent = textInfo.GetAscentLine().GetStartPoint();
            var descent = textInfo.GetDescentLine().GetStartPoint();
            float approxSize = ascent.Get(1) - descent.Get(1);
            if (approxSize <= 0) approxSize = textInfo.GetFontSize();

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
                float width  = Math.Abs(ctm.Get(Matrix.I11));
                float height = Math.Abs(ctm.Get(Matrix.I22));
                float x      = ctm.Get(Matrix.I31);
                float y      = ctm.Get(Matrix.I32) + height;

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

public class MergedFragment
{
    public float X { get; set; }
    public float EndX { get; set; }
    public string Text { get; set; } = "";
    public float FontSize { get; set; }
    public bool IsBold { get; set; }
}

public class AssembledLine
{
    public float Y { get; set; }
    public List<MergedFragment> Columns { get; set; } = new List<MergedFragment>();
    public bool IsTable => Columns.Count >= 2;
    public float MaxFontSize => Columns.Count > 0 ? Columns.Max(c => c.FontSize) : 0;
    public bool AllBold => Columns.Count > 0 && Columns.All(c => c.IsBold);
    public string JoinedText => string.Join(" ", Columns.Select(c => c.Text)).Trim();
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

    private static bool IsHeaderLine(AssembledLine line, float baseFontSize)
    {
        if (line.MaxFontSize >= baseFontSize + 2f) return true;
        if (line.AllBold && line.JoinedText.Length < 80) return true;
        return false;
    }

    /// <summary>
    /// Merge consecutive buffered table rows whose Y-gap is within normal
    /// line-spacing into single rows (handles multi-line cell content).
    /// Does NOT merge across a style transition (bold→non-bold or font size drop),
    /// which prevents header rows from absorbing data rows.
    /// </summary>
    private static List<AssembledLine> MergeMultiLineCells(List<AssembledLine> rows, float baseFontSize)
    {
        if (rows.Count <= 1) return rows;

        float lineThreshold = Math.Max(baseFontSize * 1.8f, 14f);
        var merged = new List<AssembledLine>();
        var current = CloneLine(rows[0]);

        for (int i = 1; i < rows.Count; i++)
        {
            float yGap = Math.Abs(current.Y - rows[i].Y);
            bool smallGap = yGap < lineThreshold;
            bool sameColCount = current.Columns.Count == rows[i].Columns.Count;

            // Prevent merging across a style transition (header row → data row).
            bool boldTransition = current.AllBold && !rows[i].AllBold;
            bool fontDrop = current.MaxFontSize > rows[i].MaxFontSize + 1.5f;
            bool styleBreak = boldTransition || fontDrop;

            if (smallGap && sameColCount && !styleBreak)
            {
                for (int c = 0; c < current.Columns.Count; c++)
                {
                    string extra = rows[i].Columns[c].Text.Trim();
                    if (!string.IsNullOrEmpty(extra))
                        current.Columns[c].Text += " " + extra;
                }
                current.Y = rows[i].Y;
            }
            else
            {
                merged.Add(current);
                current = CloneLine(rows[i]);
            }
        }
        merged.Add(current);
        return merged;
    }

    private static AssembledLine CloneLine(AssembledLine src)
    {
        return new AssembledLine
        {
            Y = src.Y,
            Columns = src.Columns.Select(c => new MergedFragment
            {
                X = c.X, EndX = c.EndX, Text = c.Text,
                FontSize = c.FontSize, IsBold = c.IsBold
            }).ToList()
        };
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
            float pageWidth = page.GetPageSize().GetWidth();

            var listener = new StructuralEventListener();
            var processor = new PdfCanvasProcessor(listener);
            processor.ProcessPageContent(page);

            var textFragments = listener.Elements.Where(e => !e.IsImage).ToList();

            // -----------------------------------------------------------------
            // 1. Determine baseline margin and base font size for the page.
            // -----------------------------------------------------------------
            float baseMargin = 0f;
            float baseFontSize = 10f;
            if (textFragments.Any())
            {
                var marginGrp = textFragments
                    .GroupBy(f => Math.Round(f.X / 5) * 5)
                    .OrderByDescending(g => g.Count())
                    .First();
                baseMargin = (float)(double)marginGrp.Key;

                var fsGrp = textFragments
                    .GroupBy(f => Math.Round(f.FontSize))
                    .OrderByDescending(g => g.Count())
                    .First();
                baseFontSize = (float)(double)fsGrp.Key;
            }

            // -----------------------------------------------------------------
            // 2. Group fragments by Y (line) and merge close fragments.
            // -----------------------------------------------------------------
            var lineGroups = textFragments
                .GroupBy(e => Math.Round(e.Y / 3) * 3)
                .Select(g => new { Y = (float)g.Key, Fragments = g.OrderBy(e => e.X).ToList() })
                .ToList();

            var assembledLines = new List<AssembledLine>();
            foreach (var lg in lineGroups)
            {
                if (!lg.Fragments.Any()) continue;
                var line = new AssembledLine { Y = lg.Y };

                var cur = new MergedFragment
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
                    float gap = next.X - cur.EndX;
                    if (gap < 15f)
                    {
                        cur.Text += (gap > 2f ? " " : "") + next.Text;
                        cur.EndX = next.EndX;
                        if (next.FontSize > cur.FontSize) cur.FontSize = next.FontSize;
                        if (next.IsBold) cur.IsBold = true;
                    }
                    else
                    {
                        line.Columns.Add(cur);
                        cur = new MergedFragment
                        {
                            X = next.X, EndX = next.EndX, Text = next.Text,
                            FontSize = next.FontSize, IsBold = next.IsBold
                        };
                    }
                }
                line.Columns.Add(cur);
                assembledLines.Add(line);
            }

            // -----------------------------------------------------------------
            // 3. Sort lines and images top-to-bottom.
            // -----------------------------------------------------------------
            var sortedLines = assembledLines.OrderByDescending(l => l.Y).ToList();
            var sortedImages = listener.Elements
                .Where(e => e.IsImage)
                .OrderByDescending(e => e.Y)
                .ToList();

            int lineIdx = 0, imgIdx = 0;

            var currentParagraph = new List<MergedFragment>();
            float currentX = baseMargin;
            bool currentParaIsHeader = false;

            var tableRowsBuffer = new List<AssembledLine>();

            // ---- Local helpers ----

            void FlushParagraph()
            {
                if (!currentParagraph.Any()) return;

                var p = new iText.Layout.Element.Paragraph();
                float maxFont = currentParagraph.Max(f => f.FontSize);
                bool allBold = currentParagraph.All(f => f.IsBold);
                string full = string.Join(" ", currentParagraph.Select(f => f.Text)).Trim();
                bool isShort = full.Length < 80;

                if (maxFont >= baseFontSize + 2f)
                    p.GetAccessibilityProperties().SetRole("H1");
                else if (allBold && isShort)
                    p.GetAccessibilityProperties().SetRole("H2");
                else
                    p.GetAccessibilityProperties().SetRole("P");

                foreach (var frag in currentParagraph)
                {
                    var txt = new iText.Layout.Element.Text(frag.Text + " ");
                    if (frag.IsBold) txt.SetBold();
                    txt.SetFontSize(frag.FontSize);
                    p.Add(txt);
                }

                // ---- Positioning: detect centering or apply indentation ----
                float paraLeft  = currentParagraph.Min(f => f.X);
                float paraRight = currentParagraph.Max(f => f.EndX);
                float leftGap   = paraLeft;
                float rightGap  = pageWidth - paraRight;
                // Centered when both gaps are roughly equal and text starts
                // well to the right of the normal body margin.
                bool isCentered = Math.Abs(leftGap - rightGap) < 30f
                                  && paraLeft > baseMargin + 15f;

                if (isCentered)
                {
                    p.SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER);
                }
                else
                {
                    float indent = currentX - baseMargin;
                    if (indent > 5f) p.SetMarginLeft(indent);
                }

                layoutDoc.Add(p);
                currentParagraph.Clear();
                currentParaIsHeader = false;
            }

            void RenderBufferedTable()
            {
                if (!tableRowsBuffer.Any()) return;

                if (tableRowsBuffer.Count < 2)
                {
                    // Not enough rows – render as paragraphs instead.
                    foreach (var row in tableRowsBuffer)
                    {
                        string text = row.JoinedText;
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        currentParagraph.AddRange(row.Columns);
                        currentX = row.Columns.First().X;
                        FlushParagraph();
                    }
                    tableRowsBuffer.Clear();
                    return;
                }

                FlushParagraph();

                // Merge multi-line cells (respects style transitions).
                var mergedRows = MergeMultiLineCells(tableRowsBuffer, baseFontSize);

                // Detect whether the first row is a header row.
                bool firstRowIsHeader = false;
                if (mergedRows.Count >= 2)
                {
                    var first  = mergedRows[0];
                    var second = mergedRows[1];
                    bool firstBold   = first.AllBold;
                    bool secondBold  = second.AllBold;
                    bool firstLarger = first.MaxFontSize > second.MaxFontSize + 0.5f;
                    firstRowIsHeader = (firstBold && !secondBold) || firstLarger;
                }

                // Compute table indentation from the leftmost column across all rows.
                float tableMinX  = mergedRows.Min(r => r.Columns.First().X);
                float tableIndent = tableMinX - baseMargin;

                int maxCols = mergedRows.Max(r => r.Columns.Count);
                var table = new iText.Layout.Element.Table(maxCols);
                table.SetWidth(iText.Layout.Properties.UnitValue.CreatePercentValue(100));

                // Apply table-level indentation.
                if (tableIndent > 5f)
                {
                    table.SetMarginLeft(tableIndent);
                    // Reduce table width so it doesn't overflow the page.
                    float pctWidth = Math.Max(50f, 100f - (tableIndent / pageWidth * 100f));
                    table.SetWidth(iText.Layout.Properties.UnitValue.CreatePercentValue(pctWidth));
                }

                for (int r = 0; r < mergedRows.Count; r++)
                {
                    bool isHeaderRow = (r == 0 && firstRowIsHeader);
                    var row = mergedRows[r];

                    foreach (var col in row.Columns)
                    {
                        var cell = new iText.Layout.Element.Cell();

                        // Mark header cells with TH role.
                        if (isHeaderRow)
                            cell.GetAccessibilityProperties().SetRole("TH");

                        var txt = new iText.Layout.Element.Text(col.Text);
                        if (col.IsBold) txt.SetBold();
                        txt.SetFontSize(col.FontSize);
                        cell.Add(new iText.Layout.Element.Paragraph(txt));
                        table.AddCell(cell);
                    }
                    // Pad if this row has fewer columns than the widest row.
                    for (int pad = row.Columns.Count; pad < maxCols; pad++)
                        table.AddCell(new iText.Layout.Element.Cell());
                }

                table.GetAccessibilityProperties().SetRole("Table");
                layoutDoc.Add(table);
                tableRowsBuffer.Clear();
            }

            // -----------------------------------------------------------------
            // 4. Walk lines and images in visual order.
            // -----------------------------------------------------------------
            while (lineIdx < sortedLines.Count || imgIdx < sortedImages.Count)
            {
                bool takeImage = false;
                if (imgIdx < sortedImages.Count && lineIdx < sortedLines.Count)
                    takeImage = sortedImages[imgIdx].Y > sortedLines[lineIdx].Y;
                else if (imgIdx < sortedImages.Count)
                    takeImage = true;

                if (takeImage)
                {
                    FlushParagraph();
                    RenderBufferedTable();
                    var elem = sortedImages[imgIdx++];
                    try
                    {
                        var imgData = iText.IO.Image.ImageDataFactory.Create(elem.ImageBytes);
                        var img = new iText.Layout.Element.Image(imgData);

                        if (elem.ImageWidth > 0 && elem.ImageHeight > 0)
                            img.ScaleAbsolute(elem.ImageWidth, elem.ImageHeight);
                        else
                            img.SetMaxWidth(475f);

                        img.GetAccessibilityProperties().SetRole("Figure");
                        img.GetAccessibilityProperties().SetAlternateDescription("Extracted Figure");
                        img.SetMargins(0, 0, 0, 0);

                        float indent = elem.X - baseMargin;
                        if (indent > 5f) img.SetMarginLeft(indent);
                        layoutDoc.Add(img);
                    }
                    catch { }
                }
                else
                {
                    var line = sortedLines[lineIdx++];

                    // ---- Table path ----
                    if (line.IsTable)
                    {
                        FlushParagraph();
                        tableRowsBuffer.Add(line);
                        continue;
                    }

                    // ---- Text / header path ----
                    RenderBufferedTable();

                    string lineText = line.JoinedText;
                    if (string.IsNullOrWhiteSpace(lineText)) continue;

                    bool lineIsHeader = IsHeaderLine(line, baseFontSize);

                    bool shouldFlush = false;
                    if (currentParagraph.Any())
                    {
                        string prevText = string.Join(" ", currentParagraph.Select(f => f.Text)).Trim();
                        char lastChar = prevText.Length > 0 ? prevText[prevText.Length - 1] : ' ';
                        bool prevEndsSentence = lastChar == '.' || lastChar == '?'
                                             || lastChar == '!' || lastChar == ':';

                        float prevAvgFont = currentParagraph.Average(f => f.FontSize);
                        bool fontSizeChanged = Math.Abs(line.MaxFontSize - prevAvgFont) > 1.5f;

                        if (lineIsHeader) shouldFlush = true;
                        if (currentParaIsHeader) shouldFlush = true;
                        if (prevEndsSentence) shouldFlush = true;
                        if (fontSizeChanged) shouldFlush = true;
                    }

                    if (shouldFlush)
                        FlushParagraph();

                    if (!currentParagraph.Any())
                    {
                        currentX = line.Columns.First().X;
                        currentParaIsHeader = lineIsHeader;
                    }

                    currentParagraph.AddRange(line.Columns);
                }
            }

            // End of page.
            FlushParagraph();
            RenderBufferedTable();

            if (pageNum < sourceDoc.GetNumberOfPages())
                layoutDoc.Add(new iText.Layout.Element.AreaBreak(iText.Layout.Properties.AreaBreakType.NEXT_PAGE));
        }

        layoutDoc.Close();
        return outputStream.ToArray();
    }
}
