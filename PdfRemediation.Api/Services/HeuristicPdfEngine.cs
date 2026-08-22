using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Geom;
using iText.Kernel.Events;
using iText.Kernel.Pdf.Canvas;
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
    public bool IsItalic { get; set; }
    public float FontColorR { get; set; }
    public float FontColorG { get; set; }
    public float FontColorB { get; set; }
    public string OriginalFontName { get; set; } = "";
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

            var ctm = textInfo.GetTextMatrix();
            float scaleY = Math.Abs(ctm.Get(Matrix.I22));
            float approxSize = textInfo.GetFontSize() * scaleY;
            
            if (approxSize <= 0) 
                approxSize = 10f;

            var font = textInfo.GetFont();
            var fontProgram = font?.GetFontProgram();
            var fontName = fontProgram?.GetFontNames()?.GetFontName()?.ToLowerInvariant() ?? "";
            bool isBold = fontName.Contains("bold") || fontName.Contains("black") || fontName.Contains("heavy");
            bool isItalic = fontName.Contains("italic") || fontName.Contains("oblique");

            // Extract font color from graphics state
            float colorR = 0, colorG = 0, colorB = 0;
            try
            {
                var gs = textInfo.GetGraphicsState();
                if (gs != null)
                {
                    var fillColor = gs.GetFillColor();
                    if (fillColor != null)
                    {
                        var cv = fillColor.GetColorValue();
                        int numComponents = fillColor.GetNumberOfComponents();
                        if (numComponents == 3) // RGB
                        {
                            colorR = cv[0]; colorG = cv[1]; colorB = cv[2];
                        }
                        else if (numComponents == 1) // Gray
                        {
                            colorR = colorG = colorB = cv[0];
                        }
                        else if (numComponents == 4) // CMYK
                        {
                            float c1 = cv[0], m1 = cv[1], y1 = cv[2], k1 = cv[3];
                            colorR = (1 - c1) * (1 - k1);
                            colorG = (1 - m1) * (1 - k1);
                            colorB = (1 - y1) * (1 - k1);
                        }
                    }
                }
            }
            catch { }

            var startPoint = textInfo.GetBaseline().GetStartPoint();
            var endPoint = textInfo.GetBaseline().GetEndPoint();

            Elements.Add(new PdfElement
            {
                Text = text,
                Y = startPoint.Get(1),
                X = startPoint.Get(0),
                EndX = endPoint.Get(0),
                FontSize = approxSize,
                IsBold = isBold,
                IsItalic = isItalic,
                FontColorR = colorR,
                FontColorG = colorG,
                FontColorB = colorB,
                OriginalFontName = fontName
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
    public List<PdfElement> Elements { get; set; } = new List<PdfElement>();
    
    public string Text => string.Join("", Elements.Select((e, i) => 
        (i > 0 && e.X - Elements[i-1].EndX > 2f ? " " : "") + e.Text)).Trim();
        
    public float FontSize => Elements.Any() ? Elements.Max(e => e.FontSize) : 0;
    public bool IsBold => Elements.Any() && Elements.All(e => e.IsBold);
    public bool IsItalic => Elements.Any() && Elements.All(e => e.IsItalic);
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
    /// Maps an original font name to the closest standard PDF font, respecting bold and italic.
    /// </summary>
    private static string MapToStandardFont(string originalFontName, bool isBold, bool isItalic)
    {
        string lower = (originalFontName ?? "").ToLowerInvariant();

        // Symbol fonts
        if (lower.Contains("symbol")) return "Symbol";
        if (lower.Contains("zapf") || lower.Contains("dingbat")) return "ZapfDingbats";

        // Monospace
        if (lower.Contains("courier") || lower.Contains("mono") || lower.Contains("consolas") || lower.Contains("menlo"))
        {
            if (isBold && isItalic) return "Courier-BoldOblique";
            if (isBold) return "Courier-Bold";
            if (isItalic) return "Courier-Oblique";
            return "Courier";
        }

        // Sans-serif
        if (lower.Contains("arial") || lower.Contains("helvetica") || lower.Contains("verdana") ||
            lower.Contains("tahoma") || lower.Contains("calibri") || lower.Contains("segoe") ||
            lower.Contains("sans") || lower.Contains("gothic") || lower.Contains("franklin"))
        {
            if (isBold && isItalic) return "Helvetica-BoldOblique";
            if (isBold) return "Helvetica-Bold";
            if (isItalic) return "Helvetica-Oblique";
            return "Helvetica";
        }

        // Serif (default for academic/formal documents)
        if (isBold && isItalic) return "Times-BoldItalic";
        if (isBold) return "Times-Bold";
        if (isItalic) return "Times-Italic";
        return "Times-Roman";
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
                    if (rows[i].Columns[c].Elements.Any())
                    {
                        if (current.Columns[c].Elements.Any())
                        {
                            current.Columns[c].Elements.Add(new PdfElement 
                            { 
                                Text = " ", 
                                X = 0, EndX = 0, 
                                FontSize = current.Columns[c].FontSize, 
                                Y = current.Y 
                            });
                        }
                        
                        // Normalize the baseline (Y) of the merged row so TextRise evaluates correctly
                        float offset = current.Y - rows[i].Y;
                        var normalizedElements = rows[i].Columns[c].Elements.Select(e => new PdfElement 
                        {
                            Text = e.Text,
                            X = e.X, EndX = e.EndX,
                            FontSize = e.FontSize,
                            IsBold = e.IsBold,
                            IsItalic = e.IsItalic,
                            FontColorR = e.FontColorR,
                            FontColorG = e.FontColorG,
                            FontColorB = e.FontColorB,
                            OriginalFontName = e.OriginalFontName,
                            ImageBytes = e.ImageBytes,
                            ImageHeight = e.ImageHeight,
                            ImageWidth = e.ImageWidth,
                            Y = e.Y + offset 
                        });
                        
                        current.Columns[c].Elements.AddRange(normalizedElements);
                    }
                }
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
                X = c.X, EndX = c.EndX, 
                Elements = c.Elements.ToList()
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
        
        // Custom roles must be mapped to standard roles in a Tagged PDF to prevent iText validation errors.
        var structTreeRoot = pdfDoc.GetStructTreeRoot().GetPdfObject();
        var roleMap = structTreeRoot.GetAsDictionary(PdfName.RoleMap);
        if (roleMap == null) {
            roleMap = new PdfDictionary();
            structTreeRoot.Put(PdfName.RoleMap, roleMap);
        }
        roleMap.Put(new PdfName("Header"), new PdfName(iText.Kernel.Pdf.Tagging.StandardRoles.NONSTRUCT));
        roleMap.Put(new PdfName("Footer"), new PdfName(iText.Kernel.Pdf.Tagging.StandardRoles.NONSTRUCT));
        
        var layoutDoc = new iText.Layout.Document(pdfDoc);
        layoutDoc.SetMargins(0, 0, 0, 0);

        // Font cache shared across all pages
        var fontCache = new Dictionary<string, iText.Kernel.Font.PdfFont>();

        using var sourceReader = new PdfReader(new MemoryStream(pdfBytes));
        using var sourceDoc = new PdfDocument(sourceReader);

        for (int pageNum = 1; pageNum <= sourceDoc.GetNumberOfPages(); pageNum++)
        {
            var page = sourceDoc.GetPage(pageNum);
            var pageSize = page.GetPageSize();
            
            // Create the page explicitly in the output document to match source dimensions and margins
            var newPage = pdfDoc.AddNewPage(new iText.Kernel.Geom.PageSize(pageSize));
            var cropBox = page.GetCropBox();
            if (cropBox != null) {
                newPage.SetCropBox(cropBox);
            }
            newPage.SetRotation(page.GetRotation());

            float pageWidth = pageSize.GetWidth();
            float pageHeight = pageSize.GetHeight();

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

            // ---------------------------------------------------------------
            // Helper: Create a fully styled iText Text element
            // ---------------------------------------------------------------
            iText.Layout.Element.Text CreateStyledText(string textContent, PdfElement styleSource, float lineY, float scaleFactor = 1f)
            {
                string fontKey = MapToStandardFont(styleSource.OriginalFontName, styleSource.IsBold, styleSource.IsItalic);
                if (!fontCache.TryGetValue(fontKey, out var pdfFont))
                {
                    try { pdfFont = iText.Kernel.Font.PdfFontFactory.CreateFont(fontKey); }
                    catch { pdfFont = iText.Kernel.Font.PdfFontFactory.CreateFont("Helvetica"); }
                    fontCache[fontKey] = pdfFont;
                }

                var txt = new iText.Layout.Element.Text(textContent);
                txt.SetFont(pdfFont);
                txt.SetFontSize(styleSource.FontSize * scaleFactor);

                // Apply font color
                txt.SetFontColor(new iText.Kernel.Colors.DeviceRgb(
                    styleSource.FontColorR, styleSource.FontColorG, styleSource.FontColorB));

                // Apply text rise for superscripts/subscripts
                if (styleSource.Y > lineY + 2f) txt.SetTextRise(styleSource.Y - lineY);
                else if (styleSource.Y < lineY - 2f) txt.SetTextRise(styleSource.Y - lineY);

                return txt;
            }

            // ---------------------------------------------------------------
            // 1. Group text fragments into lines (sliding window on Y)
            // ---------------------------------------------------------------
            var sortedFragments = textFragments.OrderByDescending(e => e.Y).ToList();
            var lineGroupsList = new List<List<PdfElement>>();

            if (sortedFragments.Any())
            {
                var currentGroup = new List<PdfElement> { sortedFragments[0] };
                lineGroupsList.Add(currentGroup);
                float currentY = sortedFragments[0].Y;

                for (int i = 1; i < sortedFragments.Count; i++)
                {
                    float yDiff = Math.Abs(currentY - sortedFragments[i].Y);
                    if (yDiff < 6f)
                    {
                        currentGroup.Add(sortedFragments[i]);
                    }
                    else
                    {
                        currentGroup = new List<PdfElement> { sortedFragments[i] };
                        lineGroupsList.Add(currentGroup);
                        currentY = sortedFragments[i].Y;
                    }
                }
            }

            // ---------------------------------------------------------------
            // 2. Assemble lines with merged columns
            // ---------------------------------------------------------------
            var assembledLines = new List<AssembledLine>();
            foreach (var group in lineGroupsList)
            {
                if (!group.Any()) continue;
                
                var fragments = group.OrderBy(e => e.X).ToList();

                // Use the dominant (most frequent) Y as the baseline anchor
                float dominantY = group
                    .GroupBy(e => Math.Round(e.Y * 2f) / 2f)
                    .OrderByDescending(g => g.Count())
                    .First()
                    .First().Y;

                var line = new AssembledLine { Y = dominantY };

                var cur = new MergedFragment { X = fragments[0].X, EndX = fragments[0].EndX };
                cur.Elements.Add(fragments[0]);

                for (int i = 1; i < fragments.Count; i++)
                {
                    var next = fragments[i];
                    float gap = next.X - cur.EndX;
                    
                    if (gap < 25f)
                    {
                        cur.Elements.Add(next);
                        cur.EndX = next.EndX;
                    }
                    else
                    {
                        line.Columns.Add(cur);
                        cur = new MergedFragment { X = next.X, EndX = next.EndX };
                        cur.Elements.Add(next);
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

            // ---------------------------------------------------------------
            // Helper: Render batched styled fragments into a paragraph
            // ---------------------------------------------------------------
            void RenderFragmentsIntoParagraph(iText.Layout.Element.Paragraph p, MergedFragment frag, float lineY, bool appendSpace, float scaleFactor = 1f)
            {
                if (!frag.Elements.Any()) return;

                var sb = new System.Text.StringBuilder();
                var currentEl = frag.Elements[0];
                sb.Append(currentEl.Text);

                for (int i = 1; i < frag.Elements.Count; i++)
                {
                    var e = frag.Elements[i];

                    bool sameFont = Math.Abs(e.FontSize - currentEl.FontSize) < 0.1f;
                    bool sameBold = e.IsBold == currentEl.IsBold;
                    bool sameItalic = e.IsItalic == currentEl.IsItalic;
                    bool sameY = Math.Abs(e.Y - currentEl.Y) < 1f;
                    bool sameColor = Math.Abs(e.FontColorR - currentEl.FontColorR) < 0.02f
                                  && Math.Abs(e.FontColorG - currentEl.FontColorG) < 0.02f
                                  && Math.Abs(e.FontColorB - currentEl.FontColorB) < 0.02f;

                    if (sameFont && sameBold && sameItalic && sameY && sameColor)
                    {
                        if (e.X - frag.Elements[i - 1].EndX > 2f)
                        {
                            if (!sb.ToString().EndsWith(" ") && !e.Text.StartsWith(" "))
                                sb.Append(" ");
                        }
                        sb.Append(e.Text);
                    }
                    else
                    {
                        // Flush accumulated text with current style
                        p.Add(CreateStyledText(sb.ToString(), currentEl, lineY, scaleFactor));

                        sb.Clear();
                        if (e.X - frag.Elements[i - 1].EndX > 2f && !e.Text.StartsWith(" "))
                            sb.Append(" ");
                        sb.Append(e.Text);
                        currentEl = e;
                    }
                }

                // Flush remaining
                if (sb.Length > 0)
                {
                    if (appendSpace && !sb.ToString().EndsWith(" "))
                        sb.Append(" ");
                    p.Add(CreateStyledText(sb.ToString(), currentEl, lineY, scaleFactor));
                }
            }

            // ---------------------------------------------------------------
            // FlushParagraph: render accumulated paragraph lines
            // ---------------------------------------------------------------
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

                    if (extraSpace > 0)
                    {
                        float maxAllowedSpace = firstLineFontSize * 2f;
                        if (extraSpace > maxAllowedSpace) extraSpace = maxAllowedSpace;
                    }

                    if (Math.Abs(extraSpace) > 2f)
                        p.SetMarginTop(extraSpace);
                }

                float maxFont = currentParagraphLines.Max(l => l.MaxFontSize);
                bool allBold = currentParagraphLines.All(l => l.AllBold);
                string full = string.Join(" ", currentParagraphLines.Select(l => l.JoinedText)).Trim();
                bool isShort = full.Length < 80;

                bool isListItem = System.Text.RegularExpressions.Regex.IsMatch(
                    currentParagraphLines.First().JoinedText,
                    @"^([•○▪\-\*]|\d+\.|[a-zA-Z]\))(\s|$)"
                );

                // Detect page headers (top 0.5 inch) and footers (bottom 0.5 inch)
                // We must be careful not to strip structure from a real heading (H1/H2) that happens to be at the top of the page!
                bool isPotentialHeading = (maxFont >= baseFontSize + 2f) || (allBold && isShort);
                bool isPageHeader = (firstLineY > pageHeight - 36f) && isShort && !isPotentialHeading;
                bool isPageFooter = (firstLineY < 36f) && isShort;
                bool isMarginalia = isPageHeader || isPageFooter;

                if (isPageHeader)
                {
                    p.GetAccessibilityProperties().SetRole("Header");
                }
                else if (isPageFooter)
                {
                    p.GetAccessibilityProperties().SetRole("Footer");
                }
                else if (maxFont >= baseFontSize + 2f)
                    p.GetAccessibilityProperties().SetRole("H1");
                else if (allBold && isShort)
                    p.GetAccessibilityProperties().SetRole("H2");
                else if (isListItem)
                    p.GetAccessibilityProperties().SetRole("LI");
                else
                    p.GetAccessibilityProperties().SetRole("P");

                // Calculate exact line spacing to preserve original paragraph height
                float avgSpacing = firstLineFontSize * 1.2f;
                if (currentParagraphLines.Count >= 2)
                {
                    float totalSpacing = 0;
                    for (int i = 1; i < currentParagraphLines.Count; i++)
                        totalSpacing += Math.Abs(currentParagraphLines[i-1].Y - currentParagraphLines[i].Y);
                    avgSpacing = totalSpacing / (currentParagraphLines.Count - 1);
                }
                p.SetMultipliedLeading(0f); // Disable proportional leading
                p.SetFixedLeading(avgSpacing); // Force exact absolute leading

                // Render each line, allowing natural text flow (no \n) to enable true iText alignment
                for (int lineIndex = 0; lineIndex < currentParagraphLines.Count; lineIndex++)
                {
                    var line = currentParagraphLines[lineIndex];

                    for (int colIdx = 0; colIdx < line.Columns.Count; colIdx++)
                    {
                        var frag = line.Columns[colIdx];
                        bool isLastCol = (colIdx == line.Columns.Count - 1);
                        bool isLastLine = (lineIndex == currentParagraphLines.Count - 1);
                        bool appendSpace = !(isLastCol && isLastLine);
                        RenderFragmentsIntoParagraph(p, frag, line.Y, appendSpace);
                    }
                }

                // ----- Alignment Detection -----
                // Calculate robust left/right margins using medians to ignore stray trailing spaces or outliers
                var allLefts = currentParagraphLines.Select(l => l.Columns.First().X).OrderBy(x => x).ToList();
                var allRights = currentParagraphLines.Select(l => l.Columns.Last().EndX).OrderBy(x => x).ToList();
                
                float medianLeft = allLefts[allLefts.Count / 2];
                float medianRight = allRights[allRights.Count / 2];
                
                // For bounding box purposes, we MUST use absolute extremes to prevent premature text wrapping
                float minLeft = allLefts.First();
                float maxRight = allRights.Last();

                float firstLineIndent = currentParagraphLines.First().Columns.First().X - medianLeft;
                if (firstLineIndent > 5f)
                    p.SetFirstLineIndent(firstLineIndent);

                int matchingLefts = 0;
                int matchingRights = 0;
                int rightCheckCount = 0;
                int leftCheckCount = 0;

                for (int i = 0; i < currentParagraphLines.Count; i++)
                {
                    var l = currentParagraphLines[i];
                    
                    if (i > 0 || firstLineIndent <= 5f)
                    {
                        leftCheckCount++;
                        if (Math.Abs(l.Columns.First().X - medianLeft) <= 15f) 
                            matchingLefts++;
                    }
                    
                    if (i < currentParagraphLines.Count - 1 || currentParagraphLines.Count == 1)
                    {
                        rightCheckCount++;
                        if (Math.Abs(l.Columns.Last().EndX - medianRight) <= 20f) 
                            matchingRights++;
                    }
                }
                
                // If 80% of lines match the median margins, consider it justified/aligned
                bool allLeftsMatch = leftCheckCount > 0 && ((float)matchingLefts / leftCheckCount) >= 0.8f;
                bool allRightsMatch = rightCheckCount > 0 && ((float)matchingRights / rightCheckCount) >= 0.8f;
                
                bool isJustified = (currentParagraphLines.Count >= 2 && allLeftsMatch && allRightsMatch);
                bool isRightAligned = (currentParagraphLines.Count >= 2 && !allLeftsMatch && allRightsMatch);
                
                bool isCentered = false;
                if (currentParagraphLines.Count == 1 && isShort)
                {
                    float centerOfText = (minLeft + maxRight) / 2f;
                    if (minLeft > 80f && (pageWidth - maxRight) > 80f && Math.Abs(centerOfText - (pageWidth / 2f)) < 30f)
                        isCentered = true;
                }

                // ----- Apply True Alignment and Dynamic Bounding Boxes -----
                float bottomY = currentParagraphLines.Last().Y - (firstLineFontSize * 0.2f);
                float exactWidth = maxRight - minLeft;

                if (isJustified) 
                {
                    p.SetTextAlignment(iText.Layout.Properties.TextAlignment.JUSTIFIED);
                    p.SetFixedPosition(pageNum, minLeft, bottomY, exactWidth + 10f); // Generous slack to avoid wrapping
                }
                else if (isCentered) 
                {
                    p.SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER);
                    p.SetFixedPosition(pageNum, 0, bottomY, pageWidth); // Use full page width so it never wraps
                }
                else if (isRightAligned)
                {
                    p.SetTextAlignment(iText.Layout.Properties.TextAlignment.RIGHT);
                    p.SetFixedPosition(pageNum, 0, bottomY, maxRight); // Box from 0 to maxRight so it never wraps
                }
                else 
                {
                    // Default to Left Aligned
                    p.SetTextAlignment(iText.Layout.Properties.TextAlignment.LEFT);
                    if (currentParagraphLines.Count == 1) {
                        p.SetFixedPosition(pageNum, minLeft, bottomY, pageWidth - minLeft); // Full remaining width to prevent wrapping single lines (like headers)
                    } else {
                        p.SetFixedPosition(pageNum, minLeft, bottomY, exactWidth + 10f); // Generous slack for multi-line text
                    }
                }

                layoutDoc.Add(p);
                previousParaBottomY = currentParagraphLines.Last().Y;
                currentParagraphLines.Clear();
                currentParaIsHeader = false;
            }

            // ---------------------------------------------------------------
            // RenderBufferedTable: render accumulated table rows
            // ---------------------------------------------------------------
            void RenderBufferedTable()
            {
                if (!tableRowsBuffer.Any()) return;

                bool isRealTable = false;
                int maxColsFound = tableRowsBuffer.Max(r => r.Columns.Count);
                var multiColRows = tableRowsBuffer.Where(r => r.Columns.Count >= 2).ToList();

                // Only classify as a table if we have multiple columns AND they are strictly aligned vertically
                if (multiColRows.Count >= 2)
                {
                    int alignedPairs = 0;
                    for (int i = 0; i < multiColRows.Count - 1; i++)
                    {
                        var r1 = multiColRows[i];
                        var r2 = multiColRows[i + 1];
                        
                        bool aligned = true;
                        int colsToCheck = Math.Min(r1.Columns.Count, r2.Columns.Count);
                        // Must have at least 2 columns to even check alignment
                        if (colsToCheck < 2) continue;

                        for (int c = 0; c < colsToCheck; c++)
                        {
                            // Columns must align within 20 points to be considered a real table grid
                            if (Math.Abs(r1.Columns[c].X - r2.Columns[c].X) > 20f)
                            { 
                                aligned = false; 
                                break; 
                            }
                        }
                        if (aligned) alignedPairs++;
                    }

                    // We need at least 1 aligned pair (i.e. 2 aligned rows) to consider it a table
                    // But if there are many rows, we want a decent percentage to align
                    if (alignedPairs >= 1 && (float)alignedPairs / (multiColRows.Count - 1) >= 0.5f)
                    {
                        isRealTable = true;
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
                    var first = mergedRows[0];
                    var second = mergedRows[1];
                    firstRowIsHeader = (first.AllBold && !second.AllBold) || first.MaxFontSize > second.MaxFontSize + 0.5f;
                }

                int maxCols = mergedRows.Max(r => r.Columns.Count);

                // Compute exact column widths from original positions
                var referenceRows = mergedRows.Where(r => r.Columns.Count == maxCols).ToList();
                if (!referenceRows.Any()) referenceRows = mergedRows;

                float[] colWidths = new float[maxCols];
                for (int c = 0; c < maxCols; c++)
                {
                    var rowsWithCol = referenceRows.Where(r => r.Columns.Count > c).ToList();
                    if (rowsWithCol.Any())
                    {
                        float colStart = rowsWithCol.Min(r => r.Columns[c].X);
                        if (c < maxCols - 1)
                        {
                            var rowsWithNextCol = referenceRows.Where(r => r.Columns.Count > c + 1).ToList();
                            if (rowsWithNextCol.Any())
                                colWidths[c] = rowsWithNextCol.Min(r => r.Columns[c + 1].X) - colStart;
                            else
                                colWidths[c] = rowsWithCol.Max(r => r.Columns[c].EndX) - colStart;
                        }
                        else
                        {
                            colWidths[c] = rowsWithCol.Max(r => r.Columns[c].EndX) - colStart;
                        }
                    }
                    if (colWidths[c] < 15f) colWidths[c] = 15f;
                }

                var colUnitWidths = colWidths.Select(w => 
                    iText.Layout.Properties.UnitValue.CreatePointValue(w)).ToArray();
                var table = new iText.Layout.Element.Table(colUnitWidths);

                float tableMinX = mergedRows.Min(r => r.Columns.First().X);
                table.SetMargin(0f);

                for (int r = 0; r < mergedRows.Count; r++)
                {
                    bool isHeaderRow = (r == 0 && firstRowIsHeader);
                    var row = mergedRows[r];

                    float rowHeight = baseFontSize * 1.5f;
                    if (r < mergedRows.Count - 1)
                        rowHeight = Math.Abs(row.Y - mergedRows[r+1].Y);
                    else if (mergedRows.Count >= 2)
                        rowHeight = Math.Abs(mergedRows[r-1].Y - row.Y);
                    
                    // Clamp row height to a reasonable minimum
                    if (rowHeight < baseFontSize) rowHeight = baseFontSize * 1.2f;

                    foreach (var col in row.Columns)
                    {
                        int colspan = (row.Columns.Count == 1 && maxCols > 1) ? maxCols : 1;
                        var cell = new iText.Layout.Element.Cell(1, colspan);
                        
                        // Enforce exact cell height to prevent the table from expanding upwards and overlapping text
                        cell.SetHeight(rowHeight);
                        cell.SetPadding(0f);

                        if (isHeaderRow)
                            cell.GetAccessibilityProperties().SetRole("TH");

                        var pCell = new iText.Layout.Element.Paragraph().SetMargin(0f);
                        pCell.SetMultipliedLeading(0.9f);
                        
                        // Slightly reduce font size by 10% to prevent standard fonts from wrapping inside the exact column widths
                        RenderFragmentsIntoParagraph(pCell, col, row.Y, false, 0.90f);
                        cell.Add(pCell);
                        table.AddCell(cell);
                    }

                    if (row.Columns.Count > 1)
                    {
                        for (int pad = row.Columns.Count; pad < maxCols; pad++)
                            table.AddCell(new iText.Layout.Element.Cell());
                    }
                }

                table.GetAccessibilityProperties().SetRole("Table");
                
                float tableTopY = mergedRows.First().Y + (baseFontSize * 1.2f);
                float tableBlockWidth = pageWidth - tableMinX;
                
                // Draw the table from the TOP down, so if it expands, it flows downwards and never crushes text above it
                var rect = new iText.Kernel.Geom.Rectangle(tableMinX, 0, tableBlockWidth, tableTopY);
                var canvas = new iText.Layout.Canvas(pdfDoc.GetPage(pageNum), rect);
                canvas.Add(table);
                canvas.Close();

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

                        float imgBottomY = elem.Y - (elem.ImageHeight > 0 ? elem.ImageHeight : 0f);
                        pImg.SetFixedPosition(pageNum, elem.X, imgBottomY, pageWidth - elem.X);

                        layoutDoc.Add(pImg);
                        previousParaBottomY = imgBottomY;
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

                    if (tableRowsBuffer.Any())
                    {
                        float yGap = Math.Abs(tableRowsBuffer.Last().Y - line.Y);
                        if (yGap < 20f)
                        {
                            tableRowsBuffer.Add(line);
                            continue;
                        }
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
                        
                        bool largeYGap = false;
                        if (currentParagraphLines.Count >= 2)
                        {
                            float totalSpacing = 0;
                            for (int i = 1; i < currentParagraphLines.Count; i++)
                                totalSpacing += Math.Abs(currentParagraphLines[i-1].Y - currentParagraphLines[i].Y);
                            float avgSpacing = totalSpacing / (currentParagraphLines.Count - 1);
                            
                            if (lineSpacing > avgSpacing + (prevAvgFont * 0.5f))
                                largeYGap = true;
                        }
                        else
                        {
                            if (lineSpacing > prevAvgFont * 2.5f)
                                largeYGap = true;
                        }

                        float paraMinX = currentParagraphLines.Min(l => l.Columns.First().X);
                        bool isIndented = (line.Columns.First().X - paraMinX) > 15f;

                        float paraMaxX = Math.Max(currentParagraphLines.Max(l => l.Columns.Last().EndX), line.Columns.Last().EndX);
                        float prevLineEndX = lastLineInPara.Columns.Last().EndX;
                        bool prevLineShort = (paraMaxX - prevLineEndX) > 35f;

                        bool fontSizeChanged = Math.Abs(line.MaxFontSize - prevAvgFont) > 1.5f;

                        if (lineIsHeader) shouldFlush = true;
                        if (currentParaIsHeader) shouldFlush = true;
                        if (largeYGap) shouldFlush = true;
                        if (isIndented) shouldFlush = true;
                        if (prevLineShort) shouldFlush = true;
                        if (fontSizeChanged) shouldFlush = true;
                        if (lineIsListItem) shouldFlush = true;
                    }

                    if (shouldFlush)
                        FlushParagraph();

                    if (!currentParagraphLines.Any())
                        currentParaIsHeader = lineIsHeader;

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
