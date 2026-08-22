using Microsoft.AspNetCore.Mvc;
using PdfRemediation.Api.Services;
using PdfRemediation.Api.Data;
using PdfRemediation.Api.Models;
using System.IO.Compression;
using System.Text.Json;
using System.Diagnostics;

namespace PdfRemediation.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RemediationController : ControllerBase
{
    private readonly HeuristicPdfEngine _pdfEngine;
    private readonly SupabaseService _supabase;
    private readonly IServiceProvider _serviceProvider;

    public RemediationController(SupabaseService supabase, HeuristicPdfEngine pdfEngine, IServiceProvider serviceProvider)
    {
        _pdfEngine = pdfEngine;
        _supabase = supabase;
        _serviceProvider = serviceProvider;
    }

    [HttpGet("debug-env")]
    public IActionResult DebugEnv()
    {
        var envs = Environment.GetEnvironmentVariables();
        var keys = new List<string>();
        foreach(var key in envs.Keys) keys.Add(key.ToString());
        
        var configStr = _serviceProvider.GetService<IConfiguration>()?["SUPABASE_URL"];
        var dbStr = _serviceProvider.GetService<IConfiguration>()?["DB_CONNECTION_STRING"];
        
        var parsedHost = "none";
        if (!string.IsNullOrEmpty(dbStr)) {
            parsedHost = dbStr.Length > 15 ? dbStr.Substring(0, 15) : dbStr;
        }

        return Ok(new {
            FoundKeys = keys.Where(k => k.Contains("SUPA") || k.Contains("DB_")).ToList(),
            UrlValueLength = configStr?.Length ?? -1,
            DbStringStart = parsedHost
        });
    }

    [HttpGet("diagnostics")]
    public async Task<IActionResult> RunDiagnostics()
    {
        var diagnostics = new Dictionary<string, string>();
        
        // 1. Test Database
        try {
            var db = _serviceProvider.GetService<AppDbContext>();
            if (db == null) diagnostics.Add("Database", "AppDbContext is null (Connection string missing?)");
            else {
                var canConnect = await db.Database.CanConnectAsync();
                diagnostics.Add("Database", canConnect ? "Connected Successfully!" : "CanConnectAsync returned false. Invalid connection string or firewall blocking.");
            }
        } catch (Exception ex) {
            diagnostics.Add("Database_Error", ex.Message);
        }

        // 2. Test Storage Bucket
        try {
            var files = await _supabase.ListFilesAsync();
            diagnostics.Add("Storage", $"Connected Successfully! Found {files.Count} files in bucket.");
        } catch (Exception ex) {
            diagnostics.Add("Storage_Error", ex.Message);
        }

        return Ok(diagnostics);
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

            // DB Logging Setup
            var db = _serviceProvider.GetService<AppDbContext>();
            var session = new BatchSession { TotalFiles = 1, Status = "Processing" };
            try {
                if (db != null) {
                    db.BatchSessions.Add(session);
                    await db.SaveChangesAsync();
                }
            } catch (Exception ex) {
                Console.WriteLine("DB Logging disabled/failed: " + ex.Message);
                db = null; // Disable DB for the rest of this request
            }

            // Single PDF
            if (file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var sw = Stopwatch.StartNew();
                var remediatedBytes = _pdfEngine.ApplyRemediation(fileBytes, options);
                sw.Stop();

                var publicUrl = await _supabase.UploadFileAsync($"remediated_{Guid.NewGuid()}.pdf", remediatedBytes);
                
                if (db != null) {
                    db.RemediationLogs.Add(new RemediationLog {
                        BatchSessionId = session.Id,
                        OriginalFileName = file.FileName,
                        FileSizeBytes = file.Length,
                        IsOcrApplied = false, // Add real logic if needed
                        IsStructureRebuilt = true,
                        IsAccessibleTagged = true,
                        ProcessingDurationMs = (int)sw.ElapsedMilliseconds
                    });
                    session.SuccessfulFiles = 1;
                    session.Status = "Completed";
                    await db.SaveChangesAsync();
                }

                return File(remediatedBytes, "application/pdf", $"remediated_{file.FileName}");
            }
            
            // Batch ZIP
            if (file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var outZipStream = new MemoryStream();
                using (var archive = new ZipArchive(new MemoryStream(fileBytes), ZipArchiveMode.Read))
                {
                    var pdfEntries = archive.Entries.Where(e => e.FullName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();
                    
                    if (db != null) {
                        session.TotalFiles = pdfEntries.Count;
                        await db.SaveChangesAsync();
                    }

                    using (var outArchive = new ZipArchive(outZipStream, ZipArchiveMode.Create, true))
                    {
                        foreach (var entry in pdfEntries)
                        {
                            using var entryStream = entry.Open();
                            using var ms = new MemoryStream();
                            await entryStream.CopyToAsync(ms);
                            var entryBytes = ms.ToArray();
                            
                            var sw = Stopwatch.StartNew();
                            var remediatedBytes = _pdfEngine.ApplyRemediation(entryBytes, options);
                            sw.Stop();
                            
                            if (db != null) {
                                db.RemediationLogs.Add(new RemediationLog {
                                    BatchSessionId = session.Id,
                                    OriginalFileName = entry.Name,
                                    FileSizeBytes = entryBytes.Length,
                                    IsStructureRebuilt = true,
                                    IsAccessibleTagged = true,
                                    ProcessingDurationMs = (int)sw.ElapsedMilliseconds
                                });
                                session.SuccessfulFiles++;
                                await db.SaveChangesAsync();
                            }

                            var newEntry = outArchive.CreateEntry($"remediated_{entry.Name}");
                            using var newEntryStream = newEntry.Open();
                            await newEntryStream.WriteAsync(remediatedBytes);
                        }
                    }
                }
                
                if (db != null) {
                    session.Status = "Completed";
                    await db.SaveChangesAsync();
                }

                return File(outZipStream.ToArray(), "application/zip", $"remediated_{file.FileName}");
            }

            return BadRequest("Invalid file type.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FATAL ERROR in ProcessFile: {ex}");
            try
            {
                var db = _serviceProvider.GetService<AppDbContext>();
                if (db != null)
                {
                    var session = db.BatchSessions.OrderByDescending(b => b.CreatedAt).FirstOrDefault();
                    if (session != null && session.Status == "Processing")
                    {
                        session.Status = "Failed";
                        db.SaveChanges();
                    }
                }
            }
            catch (Exception innerEx)
            {
                Console.WriteLine($"DB Failure during catch: {innerEx.Message}");
            }
            
            return StatusCode(500, $"Internal Server Error: {ex.Message}");
        }
    }
}
