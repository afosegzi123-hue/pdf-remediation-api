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
    public byte[] ImageBytes { get; set; }
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
                Elements.Add(new PdfElement { 
                    ImageBytes = image.GetImageBytes(), 
                    Y = ctm.Get(Matrix.I32) + ctm.Get(Matrix.I22), // Top roughly
                    X = ctm.Get(Matrix.I31)
                });
            } catch { } // skip unreadable images
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

    private TextBlockPrediction PredictTag(TextBlockFeature feature)
    {
        string tag = "P";
        if (feature.FontSize >= 20f && feature.IsBoldFloat > 0.5f) tag = "H1";
        else if (feature.FontSize >= 16f && feature.IsBoldFloat > 0.5f) tag = "H2";
        else if (feature.FontSize >= 14f && feature.IsBoldFloat > 0.5f) tag = "H3";
        return new TextBlockPrediction { PredictedTag = tag };
    }

    public class RemediationOptions
    {
        public bool NormalizeMetadata { get; set; } = true;
        public bool TagLanguage { get; set; } = true;
        public bool AutoTagStructure { get; set; } = false;
    }

    public byte[] ApplyRemediation(byte[] pdfBytes, RemediationOptions options)
    {
        using var inputStream = new MemoryStream(pdfBytes);
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

        for (int i = 1; i <= sourceDoc.GetNumberOfPages(); i++)
        {
            var page = sourceDoc.GetPage(i);
            var listener = new StructuralEventListener();
            var processor = new PdfCanvasProcessor(listener);
            processor.ProcessPageContent(page);
            
            // Process Text lines
            var textFragments = listener.Elements.Where(e => !e.IsImage).ToList();
            var lines = textFragments
                .GroupBy(e => Math.Round(e.Y / 4) * 4) // Group within 4 points of Y
                .Select(g => {
                    var sorted = g.OrderBy(e => e.X).ToList();
                    var sb = new System.Text.StringBuilder();
                    for (int j=0; j<sorted.Count; j++) {
                        if (j > 0 && (sorted[j].X - sorted[j-1].EndX) > 4f) sb.Append(" ");
                        sb.Append(sorted[j].Text);
                    }
                    return new PdfElement {
                        Text = sb.ToString(),
                        Y = (float)g.Key,
                        X = sorted.First().X
                    };
                }).ToList();

            var combined = new List<PdfElement>();
            combined.AddRange(lines);
            combined.AddRange(listener.Elements.Where(e => e.IsImage));
            
            // Sort top-to-bottom
            var sortedElements = combined.OrderByDescending(e => e.Y).ToList();
            
            var currentParagraph = new System.Text.StringBuilder();
            float currentX = 0;

            void FlushParagraph() {
                if (currentParagraph.Length == 0) return;
                var trimmed = currentParagraph.ToString().Trim();
                var p = new iText.Layout.Element.Paragraph(trimmed);
                
                var isShort = trimmed.Length < 40;
                var isTitleCase = trimmed.Length > 0 && char.IsUpper(trimmed[0]);
                
                if (isShort && isTitleCase && trimmed.EndsWith(":")) {
                    p.GetAccessibilityProperties().SetRole("H1");
                    p.SetFontSize(16).SetBold();
                } else if (isShort && isTitleCase) {
                    p.GetAccessibilityProperties().SetRole("H2");
                    p.SetFontSize(14).SetBold();
                } else {
                    p.GetAccessibilityProperties().SetRole("P");
                    p.SetFontSize(11);
                }
                
                float margin = Math.Max(0, currentX - 36f);
                p.SetMarginLeft(margin);
                layoutDoc.Add(p);
                currentParagraph.Clear();
            }

            foreach (var elem in sortedElements)
            {
                if (elem.IsImage)
                {
                    FlushParagraph();
                    try {
                        var imageData = iText.IO.Image.ImageDataFactory.Create(elem.ImageBytes);
                        var img = new iText.Layout.Element.Image(imageData);
                        img.GetAccessibilityProperties().SetRole("Figure");
                        img.GetAccessibilityProperties().SetAlternateDescription("Extracted Image");
                        float margin = Math.Max(0, elem.X - 36f);
                        img.SetMarginLeft(margin);
                        layoutDoc.Add(img);
                    } catch { } // if image reconstruction fails, ignore
                }
                else 
                {
                    var trimmed = elem.Text.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    
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
            FlushParagraph();
            
            if (i < sourceDoc.GetNumberOfPages())
            {
                layoutDoc.Add(new iText.Layout.Element.AreaBreak(iText.Layout.Properties.AreaBreakType.NEXT_PAGE));
            }
        }

        layoutDoc.Close();
        return outputStream.ToArray();
    }
}
