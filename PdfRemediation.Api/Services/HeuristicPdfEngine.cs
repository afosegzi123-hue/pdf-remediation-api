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
}

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
            
            var startPoint = textInfo.GetBaseline().GetStartPoint();
            var endPoint = textInfo.GetBaseline().GetEndPoint();
            Elements.Add(new PdfElement { 
                Text = text, 
                Y = startPoint.Get(1), 
                X = startPoint.Get(0),
                EndX = endPoint.Get(0)
            });
        }
        else if (type == EventType.RENDER_IMAGE)
        {
            try {
                var imageInfo = (ImageRenderInfo)data;
                var image = imageInfo.GetImage();
                if (image == null) return;
                
                var ctm = imageInfo.GetImageCtm();
                // CTM columns: [scaleX, 0, 0, scaleY, translateX, translateY]
                float width  = Math.Abs(ctm.Get(Matrix.I11));   // rendered width in points
                float height = Math.Abs(ctm.Get(Matrix.I22));   // rendered height in points
                float x      = ctm.Get(Matrix.I31);
                float y      = ctm.Get(Matrix.I32) + height;    // top edge for sorting

                Elements.Add(new PdfElement { 
                    ImageBytes = image.GetImageBytes(), 
                    Y = y,
                    X = x,
                    ImageWidth = width,
                    ImageHeight = height
                });
            } catch { }
        }
    }

    public ICollection<EventType> GetSupportedEvents()
    {
        return new HashSet<EventType> { EventType.RENDER_TEXT, EventType.RENDER_IMAGE };
    }
}

public class TextBlockFeature
{
    public float FontSize { get; set; }
    public float IsBoldFloat { get; set; } 
    public float WhitespaceAbove { get; set; }
    public string TagLabel { get; set; } = "";
}

public class TextBlockPrediction
{
    public string PredictedTag { get; set; } = "";
}

public class HeuristicPdfEngine
{
    public HeuristicPdfEngine()
    {
    }

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
            info.SetCreator("PDF Remediation Suite API (Supercharged B)");
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
            
            // ── Step 1: Assemble text lines by grouping fragments on the same Y ──
            var textFragments = listener.Elements.Where(e => !e.IsImage).ToList();
            var lineGroups = textFragments
                .GroupBy(e => Math.Round(e.Y / 3) * 3)
                .Select(g => {
                    var sorted = g.OrderBy(e => e.X).ToList();
                    return new { 
                        Y = (float)g.Key, 
                        Fragments = sorted 
                    };
                }).ToList();

            // ── Step 2: Compute the page's true left margin (most common X) ──
            float baseMargin = 0;
            if (textFragments.Count > 0)
            {
                // Round X to nearest 5pt, find the most frequent one
                baseMargin = textFragments
                    .GroupBy(f => Math.Round(f.X / 5) * 5)
                    .OrderByDescending(g => g.Count())
                    .First().Key
                    is double v ? (float)v : 0;
            }

            // ── Step 3: Detect table rows ──
            // A "table row" is a line where the text fragments fall into 3+ distinct X-columns
            var tableRowYs = new HashSet<double>();
            foreach (var lg in lineGroups)
            {
                var xClusters = lg.Fragments
                    .Select(f => Math.Round(f.X / 30) * 30)  // cluster within 30pt
                    .Distinct()
                    .Count();
                if (xClusters >= 3)
                    tableRowYs.Add(lg.Y);
            }

            // ── Step 4: Build assembled lines ──
            var assembledLines = new List<PdfElement>();
            foreach (var lg in lineGroups)
            {
                var sb = new System.Text.StringBuilder();
                for (int j = 0; j < lg.Fragments.Count; j++)
                {
                    if (j > 0 && (lg.Fragments[j].X - lg.Fragments[j-1].EndX) > 4f) 
                        sb.Append("\t");  // use tab for column gaps
                    sb.Append(lg.Fragments[j].Text);
                }
                assembledLines.Add(new PdfElement {
                    Text = sb.ToString(),
                    Y = lg.Y,
                    X = lg.Fragments.First().X
                });
            }

            // ── Step 5: Merge all elements and sort top-to-bottom ──
            var combined = new List<PdfElement>();
            combined.AddRange(assembledLines);
            combined.AddRange(listener.Elements.Where(e => e.IsImage));
            var sortedElements = combined.OrderByDescending(e => e.Y).ToList();
            
            // ── Step 6: Render elements into the new tagged PDF ──
            var currentParagraph = new System.Text.StringBuilder();
            float currentX = baseMargin;
            bool inTable = false;
            iText.Layout.Element.Table? currentTable = null;
            int tableColCount = 0;

            void FlushParagraph() {
                if (currentParagraph.Length == 0) return;
                var trimmed = currentParagraph.ToString().Trim();
                var p = new iText.Layout.Element.Paragraph(trimmed);
                
                var isShort = trimmed.Length < 60;
                var isTitleCase = trimmed.Length > 0 && char.IsUpper(trimmed[0]);
                var isAllCaps = trimmed.Length > 3 && trimmed.Take(Math.Min(20, trimmed.Length)).All(c => !char.IsLetter(c) || char.IsUpper(c));
                
                if (isAllCaps && isShort) {
                    p.GetAccessibilityProperties().SetRole("H1");
                    p.SetFontSize(14).SetBold();
                } else if (isShort && isTitleCase && trimmed.EndsWith(":")) {
                    p.GetAccessibilityProperties().SetRole("H2");
                    p.SetFontSize(12).SetBold();
                } else if (isShort && isTitleCase && !trimmed.Contains(',')) {
                    p.GetAccessibilityProperties().SetRole("H2");
                    p.SetFontSize(12).SetBold();
                } else {
                    p.GetAccessibilityProperties().SetRole("P");
                    p.SetFontSize(11);
                }
                
                // Only indent if genuinely indented relative to the page baseline
                float indent = currentX - baseMargin;
                if (indent > 10f)
                    p.SetMarginLeft(indent);

                layoutDoc.Add(p);
                currentParagraph.Clear();
            }

            void FlushTable() {
                if (currentTable != null) {
                    currentTable.GetAccessibilityProperties().SetRole("Table");
                    layoutDoc.Add(currentTable);
                    currentTable = null;
                    inTable = false;
                }
            }

            foreach (var elem in sortedElements)
            {
                if (elem.IsImage)
                {
                    FlushParagraph();
                    FlushTable();
                    try {
                        var imageData = iText.IO.Image.ImageDataFactory.Create(elem.ImageBytes);
                        var img = new iText.Layout.Element.Image(imageData);

                        // ── FIX #1: Constrain image to original rendered size ──
                        if (elem.ImageWidth > 0 && elem.ImageHeight > 0)
                        {
                            img.ScaleToFit(elem.ImageWidth, elem.ImageHeight);
                        }
                        else
                        {
                            // Fallback: cap at 80% of A4 width (approx 475pt)
                            img.SetMaxWidth(475f);
                        }

                        img.GetAccessibilityProperties().SetRole("Figure");
                        img.GetAccessibilityProperties().SetAlternateDescription("Extracted Figure");
                        
                        float indent = elem.X - baseMargin;
                        if (indent > 10f)
                            img.SetMarginLeft(indent);

                        layoutDoc.Add(img);
                    } catch { }
                }
                else 
                {
                    var trimmed = elem.Text.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    bool isTableRow = tableRowYs.Contains(Math.Round(elem.Y / 3) * 3);

                    if (isTableRow)
                    {
                        // ── FIX #2: Render as a proper <Table> ──
                        FlushParagraph();
                        var cells = trimmed.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                        
                        if (!inTable || currentTable == null || tableColCount != cells.Length)
                        {
                            FlushTable();
                            tableColCount = Math.Max(cells.Length, 2);
                            currentTable = new iText.Layout.Element.Table(tableColCount);
                            currentTable.SetWidth(iText.Layout.Properties.UnitValue.CreatePercentValue(100));
                            inTable = true;
                        }

                        foreach (var cell in cells)
                        {
                            var tableCell = new iText.Layout.Element.Cell();
                            tableCell.Add(new iText.Layout.Element.Paragraph(cell.Trim()).SetFontSize(10));
                            currentTable.AddCell(tableCell);
                        }
                        // Pad remaining columns if fewer cells than expected
                        for (int pad = cells.Length; pad < tableColCount; pad++)
                        {
                            currentTable.AddCell(new iText.Layout.Element.Cell());
                        }
                    }
                    else
                    {
                        FlushTable();
                        
                        if (currentParagraph.Length > 0)
                        {
                            var lastChar = currentParagraph[currentParagraph.Length - 1];
                            if (lastChar != '.' && lastChar != '?' && lastChar != '!' && lastChar != ':' && trimmed.Length > 20)
                            {
                                currentParagraph.Append(" " + trimmed);
                            }
                            else
                            {
                                FlushParagraph();
                                currentParagraph.Append(trimmed);
                                currentX = elem.X;
                            }
                        }
                        else
                        {
                            currentParagraph.Append(trimmed);
                            currentX = elem.X;
                        }
                    }
                }
            }
            FlushParagraph();
            FlushTable();
            
            if (pageNum < sourceDoc.GetNumberOfPages())
            {
                layoutDoc.Add(new iText.Layout.Element.AreaBreak(iText.Layout.Properties.AreaBreakType.NEXT_PAGE));
            }
        }

        layoutDoc.Close();
        return outputStream.ToArray();
    }
}
