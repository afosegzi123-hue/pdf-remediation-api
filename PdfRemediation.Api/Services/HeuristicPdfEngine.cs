using iText.Kernel.Pdf;
using System.IO;
using System.Collections.Generic;

namespace PdfRemediation.Api.Services;

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
        // Pure lightweight heuristic engine matching the ML.NET dataset
        string tag = "P";
        
        if (feature.FontSize >= 20f && feature.IsBoldFloat > 0.5f)
        {
            tag = "H1";
        }
        else if (feature.FontSize >= 16f && feature.IsBoldFloat > 0.5f)
        {
            tag = "H2";
        }
        else if (feature.FontSize >= 14f && feature.IsBoldFloat > 0.5f)
        {
            tag = "H3";
        }

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
        
        // 1. Metadata Normalization
        if (options.NormalizeMetadata)
        {
            var info = pdfDoc.GetDocumentInfo();
            info.SetTitle("Remediated Document");
            info.SetCreator("PDF Remediation Suite API (Supercharged B)");
            info.SetAuthor("Automated System");
        }

        // 2. Language and Accessibility Tags
        if (options.TagLanguage)
        {
            var catalog = pdfDoc.GetCatalog();
            catalog.SetLang(new PdfString("en-US"));
            
            var viewerPreferences = new PdfViewerPreferences();
            viewerPreferences.SetDisplayDocTitle(true);
            catalog.SetViewerPreferences(viewerPreferences);
        }

        // 3. Heuristic Auto-Tagging via Reconstruction
        pdfDoc.SetTagged();
        var layoutDoc = new iText.Layout.Document(pdfDoc);

        // Read text from the original document
        using var sourceReader = new PdfReader(new MemoryStream(pdfBytes));
        using var sourceDoc = new PdfDocument(sourceReader);

        for (int i = 1; i <= sourceDoc.GetNumberOfPages(); i++)
        {
            var page = sourceDoc.GetPage(i);
            var strategy = new iText.Kernel.Pdf.Canvas.Parser.Listener.LocationTextExtractionStrategy();
            var processor = new iText.Kernel.Pdf.Canvas.Parser.PdfCanvasProcessor(strategy);
            processor.ProcessPageContent(page);
            
            string pageText = strategy.GetResultantText();
            var lines = pageText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                // Heuristic: Is it a heading?
                var isShort = trimmed.Length < 40;
                var isTitleCase = trimmed.Length > 0 && char.IsUpper(trimmed[0]);
                
                var paragraph = new iText.Layout.Element.Paragraph(trimmed);

                if (isShort && isTitleCase && trimmed.EndsWith(":"))
                {
                    paragraph.GetAccessibilityProperties().SetRole("H1");
                    paragraph.SetFontSize(16).SetBold();
                }
                else if (isShort && isTitleCase)
                {
                    paragraph.GetAccessibilityProperties().SetRole("H2");
                    paragraph.SetFontSize(14).SetBold();
                }
                else
                {
                    paragraph.GetAccessibilityProperties().SetRole("P");
                    paragraph.SetFontSize(11);
                }

                layoutDoc.Add(paragraph);
            }
            
            if (i < sourceDoc.GetNumberOfPages())
            {
                layoutDoc.Add(new iText.Layout.Element.AreaBreak(iText.Layout.Properties.AreaBreakType.NEXT_PAGE));
            }
        }

        layoutDoc.Close();
        return outputStream.ToArray();
    }
}
