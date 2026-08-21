using Microsoft.AspNetCore.Mvc;
using PdfRemediation.Api.Services;
using System.IO.Compression;
using System.Text.Json;

namespace PdfRemediation.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RemediationController : ControllerBase
{
    private readonly HeuristicPdfEngine _pdfEngine;
    private readonly SupabaseService _supabase;

    public RemediationController(SupabaseService supabase, HeuristicPdfEngine pdfEngine)
    {
        _pdfEngine = pdfEngine;
        _supabase = supabase;
    }

    [HttpPost("process")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> ProcessFile([FromForm] IFormFile file, [FromForm] string optionsJson)
    {
        try 
        {
            if (file == null || file.Length == 0) return BadRequest("File is empty");

            var options = JsonSerializer.Deserialize<HeuristicPdfEngine.RemediationOptions>(optionsJson) 
                          ?? new HeuristicPdfEngine.RemediationOptions();

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            var fileBytes = memoryStream.ToArray();

            // Single PDF
            if (file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var remediatedBytes = _pdfEngine.ApplyRemediation(fileBytes, options);
                await _supabase.UploadFileAsync($"remediated_{Guid.NewGuid()}.pdf", remediatedBytes);
                return File(remediatedBytes, "application/pdf", $"remediated_{file.FileName}");
            }
            
            // Batch ZIP
            if (file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var outZipStream = new MemoryStream();
                using (var archive = new ZipArchive(new MemoryStream(fileBytes), ZipArchiveMode.Read))
                using (var outArchive = new ZipArchive(outZipStream, ZipArchiveMode.Create, true))
                {
                    foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
                    {
                        using var entryStream = entry.Open();
                        using var ms = new MemoryStream();
                        await entryStream.CopyToAsync(ms);
                        
                        var remediatedBytes = _pdfEngine.ApplyRemediation(ms.ToArray(), options);
                        
                        var newEntry = outArchive.CreateEntry($"remediated_{entry.Name}");
                        using var newEntryStream = newEntry.Open();
                        await newEntryStream.WriteAsync(remediatedBytes);
                    }
                }
                return File(outZipStream.ToArray(), "application/zip", $"remediated_{file.FileName}");
            }

            return BadRequest("Invalid file type.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FATAL ERROR in ProcessFile: {ex}");
            return StatusCode(500, $"Internal Server Error: {ex.Message}");
        }
    }
}
