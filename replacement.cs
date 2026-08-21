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
    
    public float FontSize { get; set; }
    public bool IsBold { get; set; }
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
            
            var font = textInfo.GetFont();
            var fontProgram = font?.GetFontProgram();
            var fontName = fontProgram?.GetFontNames()?.GetFontName()?.ToLowerInvariant() ?? "";
            bool isBold = fontName.Contains("bold") || fontName.Contains("black") || fontName.Contains("heavy");

            var startPoint = textInfo.GetBaseline().GetStartPoint();
            var endPoint = textInfo.GetBaseline().GetEndPoint();
            
            var ascent = textInfo.GetAscentLine().GetStartPoint();
            var descent = textInfo.GetDescentLine().GetStartPoint();
            float approxSize = ascent.Get(1) - descent.Get(1);
            if (approxSize <= 0) approxSize = textInfo.GetFontSize();

            Elements.Add(new PdfElement { 
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
            try {
                var imageInfo = (ImageRenderInfo)data;
                var image = imageInfo.GetImage();
                if (image == null) return;
                
                var ctm = imageInfo.GetImageCtm();
                float width  = Math.Abs(ctm.Get(Matrix.I11));
                float height = Math.Abs(ctm.Get(Matrix.I22));
                float x      = ctm.Get(Matrix.I31);
                float y      = ctm.Get(Matrix.I32) + height;

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
    public HeuristicPdfEngine() { }

    public class RemediationOptions
    {
        public bool NormalizeMetadata { get; set; } = true;
        public bool TagLanguage { get; set; } = true;
        public bool AutoTagStructure { get; set; } = false;
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
        public bool IsTable => Columns.Count >= 3;
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
            
            float baseMargin = 0;
            float baseFontSize = 10f;
            if (textFragments.Count > 0)
            {
                var marginGrp = textFragments.GroupBy(f => Math.Round(f.X / 5) * 5).OrderByDescending(g => g.Count()).FirstOrDefault();
                if (marginGrp != null) baseMargin = (float)(double)marginGrp.Key;
                    
                var fsGrp = textFragments.GroupBy(f => Math.Round(f.FontSize)).OrderByDescending(g => g.Count()).FirstOrDefault();
                if (fsGrp != null) baseFontSize = (float)(double)fsGrp.Key;
            }

            var lineGroups = textFragments
                .GroupBy(e => Math.Round(e.Y / 3) * 3)
                .Select(g => new { Y = (float)g.Key, Fragments = g.OrderBy(e => e.X).ToList() })
                .ToList();

            var assembledLines = new List<AssembledLine>();
            foreach (var lg in lineGroups)
            {
                var line = new AssembledLine { Y = lg.Y };
                if (lg.Fragments.Count == 0) continue;
                
                var current = new MergedFragment { 
                    X = lg.Fragments[0].X, 
                    EndX = lg.Fragments[0].EndX, 
                    Text = lg.Fragments[0].Text,
                    FontSize = lg.Fragments[0].FontSize,
                    IsBold = lg.Fragments[0].IsBold
                };
                
                for (int j = 1; j < lg.Fragments.Count; j++)
                {
                    var next = lg.Fragments[j];
                    float gap = next.X - current.EndX;
                    
                    if (gap < 15f) // merge into same column (normal space or kerning)
                    {
                        current.Text += (gap > 2f ? " " : "") + next.Text;
                        current.EndX = next.EndX;
                        if (next.FontSize > current.FontSize) current.FontSize = next.FontSize;
                        if (next.IsBold) current.IsBold = true;
                    }
                    else
                    {
                        line.Columns.Add(current);
                        current = new MergedFragment {
                            X = next.X, EndX = next.EndX, Text = next.Text,
                            FontSize = next.FontSize, IsBold = next.IsBold
                        };
                    }
                }
                line.Columns.Add(current);
                assembledLines.Add(line);
            }

            var sortedLines = assembledLines.OrderByDescending(l => l.Y).ToList();
            var sortedImages = listener.Elements.Where(e => e.IsImage).OrderByDescending(e => e.Y).ToList();
            
            int imgIdx = 0;
            int lineIdx = 0;

            var currentParagraph = new List<MergedFragment>();
            float currentX = baseMargin;
            bool inTable = false;
            iText.Layout.Element.Table? currentTable = null;
            int tableColCount = 0;

            void FlushParagraph() {
                if (currentParagraph.Count == 0) return;
                
                var p = new iText.Layout.Element.Paragraph();
                float maxFontSize = 0;
                bool anyBold = false;
                string fullText = "";

                foreach (var frag in currentParagraph) {
                    var t = new iText.Layout.Element.Text(frag.Text + " ");
                    if (frag.IsBold) { t.SetBold(); anyBold = true; }
                    t.SetFontSize(frag.FontSize);
                    p.Add(t);
                    if (frag.FontSize > maxFontSize) maxFontSize = frag.FontSize;
                    fullText += frag.Text + " ";
                }
                
                fullText = fullText.Trim();
                var isShort = fullText.Length < 60;
                
                if (maxFontSize > baseFontSize + 3f) {
                    p.GetAccessibilityProperties().SetRole("H1");
                } else if (anyBold && isShort && maxFontSize >= baseFontSize) {
                    p.GetAccessibilityProperties().SetRole("H2");
                } else {
                    p.GetAccessibilityProperties().SetRole("P");
                }
                
                float indent = currentX - baseMargin;
                if (indent > 10f) p.SetMarginLeft(indent);

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

            while (lineIdx < sortedLines.Count || imgIdx < sortedImages.Count)
            {
                bool processImage = false;
                if (imgIdx < sortedImages.Count && lineIdx < sortedLines.Count)
                    processImage = sortedImages[imgIdx].Y > sortedLines[lineIdx].Y;
                else if (imgIdx < sortedImages.Count)
                    processImage = true;

                if (processImage)
                {
                    FlushParagraph();
                    FlushTable();
                    var elem = sortedImages[imgIdx++];
                    try {
                        var imageData = iText.IO.Image.ImageDataFactory.Create(elem.ImageBytes);
                        var img = new iText.Layout.Element.Image(imageData);
                        if (elem.ImageWidth > 0 && elem.ImageHeight > 0)
                            img.ScaleToFit(elem.ImageWidth, elem.ImageHeight);
                        else
                            img.SetMaxWidth(475f);

                        img.GetAccessibilityProperties().SetRole("Figure");
                        img.GetAccessibilityProperties().SetAlternateDescription("Extracted Figure");
                        
                        float indent = elem.X - baseMargin;
                        if (indent > 10f) img.SetMarginLeft(indent);
                        layoutDoc.Add(img);
                    } catch { }
                }
                else 
                {
                    var line = sortedLines[lineIdx++];
                    
                    if (line.IsTable)
                    {
                        FlushParagraph();
                        if (!inTable || currentTable == null || tableColCount != line.Columns.Count)
                        {
                            FlushTable();
                            tableColCount = line.Columns.Count;
                            currentTable = new iText.Layout.Element.Table(tableColCount);
                            currentTable.SetWidth(iText.Layout.Properties.UnitValue.CreatePercentValue(100));
                            inTable = true;
                        }

                        foreach (var col in line.Columns)
                        {
                            var tableCell = new iText.Layout.Element.Cell();
                            var t = new iText.Layout.Element.Text(col.Text);
                            if (col.IsBold) t.SetBold();
                            t.SetFontSize(col.FontSize);
                            tableCell.Add(new iText.Layout.Element.Paragraph(t));
                            currentTable.AddCell(tableCell);
                        }
                    }
                    else
                    {
                        FlushTable();
                        string lineText = string.Join(" ", line.Columns.Select(c => c.Text)).Trim();
                        if (string.IsNullOrEmpty(lineText)) continue;
                        
                        if (currentParagraph.Count > 0)
                        {
                            var lastFrag = currentParagraph.Last();
                            var lastChar = lastFrag.Text.Length > 0 ? lastFrag.Text.Last() : ' ';
                            if (lastChar != '.' && lastChar != '?' && lastChar != '!' && lastChar != ':' && lineText.Length > 20)
                            {
                                currentParagraph.AddRange(line.Columns);
                            }
                            else
                            {
                                FlushParagraph();
                                currentParagraph.AddRange(line.Columns);
                                currentX = line.Columns.First().X;
                            }
                        }
                        else
                        {
                            currentParagraph.AddRange(line.Columns);
                            currentX = line.Columns.First().X;
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
