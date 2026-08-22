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

            var ascent = textInfo.GetAscentLine().GetStartPoint();
            var descent = textInfo.GetDescentLine().GetStartPoint();
            float approxSize = ascent.Get(1) - descent.Get(1);
            if (approxSize > 0) 
                approxSize = approxSize * 0.8333f; 
            else 
                approxSize = textInfo.GetFontSize();

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
        
        layoutDoc.SetMargins(0, 0, 0, 0);

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

            float baseFontSize = 10f;
            if (textFragments.Any())
            {
                var fsGrp = textFragments
                    .GroupBy(f => Math.Round(f.FontSize))
                    .OrderByDescending(g => g.Count())
                    .First();
                baseFontSize = (float)(double)fsGrp.Key;
            }

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
                    
                    if (gap < 25f)
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

            var sortedLines = assembledLines.OrderByDescending(l => l.Y).ToList();
            var sortedImages = listener.Elements
                .Where(e => e.IsImage)
                .OrderByDescending(e => e.Y)
                .ToList();

            int lineIdx = 0, imgIdx = 0;

            var currentParagraphLines = new List<AssembledLine>();
            bool currentParaIsHeader = false;
            float? previousParaBottomY = null;

            var tableRowsBuffer = new List<AssembledLine>();

            void FlushParagraph()
            {
                if (!currentParagraphLines.Any()) return;

                var p = new iText.Layout.Element.Paragraph();
                
                p.SetMarginBottom(0f);
                p.SetMarginTop(0f);
                
                float firstLineY = currentParagraphLines.First().Y;
                float firstLineFontSize = currentParagraphLines.First().MaxFontSize;
                
                if (previousParaBottomY.HasValue)
                {
                    float yGap = previousParaBottomY.Value - firstLineY; 
                    float standardLineHeight = firstLineFontSize * 1.2f;
                    float extraSpace = yGap - standardLineHeight;
                    
                    // Cap extra space to prevent pushing elements artificially across pages
                    if (extraSpace > 0) 
                    {
                        float maxAllowedSpace = firstLineFontSize * 2f;
                        if (extraSpace > maxAllowedSpace) extraSpace = maxAllowedSpace;
                    }
                    
                    if (Math.Abs(extraSpace) > 2f)
                    {
                        p.SetMarginTop(extraSpace);
                    }
                }

                float maxFont = currentParagraphLines.Max(l => l.MaxFontSize);
                bool allBold = currentParagraphLines.All(l => l.AllBold);
                
                string full = string.Join(" ", currentParagraphLines.Select(l => l.JoinedText)).Trim();
                bool isShort = full.Length < 80;

                bool isListItem = System.Text.RegularExpressions.Regex.IsMatch(
                    currentParagraphLines.First().JoinedText, 
                    @"^([•○▪\-\*]|\d+\.|[a-zA-Z]\))(\s|$)"
                );

                if (maxFont >= baseFontSize + 2f)
                    p.GetAccessibilityProperties().SetRole("H1");
                else if (allBold && isShort)
                    p.GetAccessibilityProperties().SetRole("H2");
                else if (isListItem)
                    p.GetAccessibilityProperties().SetRole("LI");
                else
                    p.GetAccessibilityProperties().SetRole("P");

                foreach (var line in currentParagraphLines)
                {
                    foreach (var frag in line.Columns)
                    {
                        var txt = new iText.Layout.Element.Text(frag.Text + " ");
                        if (frag.IsBold) txt.SetBold();
                        txt.SetFontSize(frag.FontSize);
                        p.Add(txt);
                    }
                }

                bool allLeftsMatch = true;
                bool allRightsMatch = true;
                bool allCentersMatch = true;
                
                float firstLeft = currentParagraphLines.First().Columns.First().X;
                float firstRight = currentParagraphLines.First().Columns.Last().EndX;
                float firstCenter = (firstLeft + firstRight) / 2f;

                for (int i = 0; i < currentParagraphLines.Count; i++)
                {
                    var l = currentParagraphLines[i];
                    float left = l.Columns.First().X;
                    float right = l.Columns.Last().EndX;
                    float center = (left + right) / 2f;
                    
                    if (Math.Abs(left - firstLeft) > 15f) allLeftsMatch = false;
                    
                    if (i < currentParagraphLines.Count - 1 || currentParagraphLines.Count == 1)
                    {
                        if (Math.Abs(right - firstRight) > 20f) allRightsMatch = false;
                    }
                    
                    if (Math.Abs(center - firstCenter) > 20f) allCentersMatch = false;
                }

                float minLeft = currentParagraphLines.Min(l => l.Columns.First().X);
                float maxRight = currentParagraphLines.Max(l => l.Columns.Last().EndX);
                float firstLineIndent = firstLeft - minLeft;

                p.SetMarginLeft(minLeft);
                p.SetMarginRight(pageWidth - maxRight);

                if (firstLineIndent > 5f)
                    p.SetFirstLineIndent(firstLineIndent);

                if (currentParagraphLines.Count >= 2 && allLeftsMatch && allRightsMatch)
                {
                    p.SetTextAlignment(iText.Layout.Properties.TextAlignment.JUSTIFIED);
                }
                else if (allCentersMatch && Math.Abs(firstCenter - (pageWidth / 2f)) < 40f)
                {
                    p.SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER);
                }

                layoutDoc.Add(p);
                previousParaBottomY = currentParagraphLines.Last().Y;
                currentParagraphLines.Clear();
                currentParaIsHeader = false;
            }

            void RenderBufferedTable()
            {
                if (!tableRowsBuffer.Any()) return;

                bool isRealTable = false;
                int maxColsFound = tableRowsBuffer.Max(r => r.Columns.Count);
                if (tableRowsBuffer.Count >= 2 && maxColsFound >= 2)
                {
                    if (tableRowsBuffer.Count >= 3)
                    {
                        isRealTable = true;
                    }
                    else if (maxColsFound >= 3)
                    {
                        isRealTable = true;
                    }
                    else
                    {
                        var r1 = tableRowsBuffer[0];
                        var r2 = tableRowsBuffer[1];
                        if (r1.Columns.Count >= 2 && r2.Columns.Count >= 2)
                        {
                            bool aligned = true;
                            for (int c = 0; c < Math.Min(r1.Columns.Count, r2.Columns.Count); c++)
                            {
                                if (Math.Abs(r1.Columns[c].X - r2.Columns[c].X) > 30f)
                                {
                                    aligned = false;
                                    break;
                                }
                            }
                            if (aligned) isRealTable = true;
                        }
                    }
                }

                if (!isRealTable)
                {
                    foreach (var row in tableRowsBuffer)
                    {
                        string text = row.JoinedText;
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        currentParagraphLines.Add(row);
                        FlushParagraph();
                    }
                    tableRowsBuffer.Clear();
                    return;
                }

                FlushParagraph();

                var mergedRows = MergeMultiLineCells(tableRowsBuffer, baseFontSize);

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

                float tableMinX = mergedRows.Min(r => r.Columns.First().X);
                float tableMaxX = mergedRows.Max(r => r.Columns.Last().EndX);
                
                int maxCols = mergedRows.Max(r => r.Columns.Count);
                var table = new iText.Layout.Element.Table(maxCols);
                
                table.SetMarginLeft(tableMinX);
                float tableWidth = tableMaxX - tableMinX;
                if (tableWidth < 100f) tableWidth = 100f;
                table.SetWidth(iText.Layout.Properties.UnitValue.CreatePointValue(tableWidth));
                
                table.SetMarginTop(0f);
                table.SetMarginBottom(0f);

                for (int r = 0; r < mergedRows.Count; r++)
                {
                    bool isHeaderRow = (r == 0 && firstRowIsHeader);
                    var row = mergedRows[r];

                    foreach (var col in row.Columns)
                    {
                        var cell = new iText.Layout.Element.Cell();

                        if (isHeaderRow)
                            cell.GetAccessibilityProperties().SetRole("TH");

                        var txt = new iText.Layout.Element.Text(col.Text);
                        if (col.IsBold) txt.SetBold();
                        txt.SetFontSize(col.FontSize);
                        cell.Add(new iText.Layout.Element.Paragraph(txt).SetMargin(0f));
                        table.AddCell(cell);
                    }
                    for (int pad = row.Columns.Count; pad < maxCols; pad++)
                        table.AddCell(new iText.Layout.Element.Cell());
                }

                table.GetAccessibilityProperties().SetRole("Table");
                layoutDoc.Add(table);
                previousParaBottomY = tableRowsBuffer.Last().Y;
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

                        img.SetMargins(0, 0, 0, 0);

                        var pImg = new iText.Layout.Element.Paragraph();
                        pImg.Add(img);
                        pImg.GetAccessibilityProperties().SetRole("Figure");
                        pImg.GetAccessibilityProperties().SetAlternateDescription("Extracted Figure");
                        pImg.SetMargin(0f);
                        pImg.SetMarginLeft(elem.X);
                        
                        if (previousParaBottomY.HasValue)
                        {
                            float yGap = previousParaBottomY.Value - elem.Y; 
                            
                            if (yGap > 0)
                            {
                                float maxAllowedGap = 50f;
                                if (yGap > maxAllowedGap) yGap = maxAllowedGap;
                            }
                            
                            if (Math.Abs(yGap) > 2f) pImg.SetMarginTop(yGap);
                        }
                        
                        layoutDoc.Add(pImg);
                        previousParaBottomY = elem.Y - (elem.ImageHeight > 0 ? elem.ImageHeight : 100f);
                    }
                    catch { }
                }
                else
                {
                    var line = sortedLines[lineIdx++];

                    if (line.IsTable)
                    {
                        FlushParagraph();
                        tableRowsBuffer.Add(line);
                        continue;
                    }

                    RenderBufferedTable();

                    string lineText = line.JoinedText;
                    if (string.IsNullOrWhiteSpace(lineText)) continue;

                    bool lineIsHeader = IsHeaderLine(line, baseFontSize);
                    bool lineIsListItem = System.Text.RegularExpressions.Regex.IsMatch(lineText, @"^([•○▪\-\*]|\d+\.|[a-zA-Z]\))(\s|$)");

                    bool shouldFlush = false;
                    if (currentParagraphLines.Any())
                    {
                        var lastLineInPara = currentParagraphLines.Last();
                        
                        float lineSpacing = Math.Abs(lastLineInPara.Y - line.Y);
                        float prevAvgFont = currentParagraphLines.Average(l => l.MaxFontSize);
                        bool largeYGap = lineSpacing > (prevAvgFont * 1.6f);

                        float paraMinX = currentParagraphLines.Min(l => l.Columns.First().X);
                        bool isIndented = (line.Columns.First().X - paraMinX) > 10f;
                        
                        bool fontSizeChanged = Math.Abs(line.MaxFontSize - prevAvgFont) > 1.5f;

                        if (lineIsHeader) shouldFlush = true;
                        if (currentParaIsHeader) shouldFlush = true;
                        if (largeYGap) shouldFlush = true;
                        if (isIndented) shouldFlush = true;
                        if (fontSizeChanged) shouldFlush = true;
                        if (lineIsListItem) shouldFlush = true; 
                    }

                    if (shouldFlush)
                        FlushParagraph();

                    if (!currentParagraphLines.Any())
                    {
                        currentParaIsHeader = lineIsHeader;
                    }

                    currentParagraphLines.Add(line);
                }
            }

            FlushParagraph();
            RenderBufferedTable();
            previousParaBottomY = null;
        }

        layoutDoc.Close();
        return outputStream.ToArray();
    }
}
