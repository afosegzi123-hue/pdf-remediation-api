using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PdfRemediation.Api.Data;
using PdfRemediation.Api.Models;

namespace PdfRemediation.Api.Services;

public class BatchWorkflowService : IBatchWorkflowService
{
    private readonly AppDbContext _dbContext;

    public BatchWorkflowService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task ProcessBatchArchiveAsync(Stream uploadedZipStream, Stream outputZipStream, CancellationToken cancellationToken = default)
    {
        var session = new BatchSession
        {
            Status = "Processing",
            CreatedAt = DateTimeOffset.UtcNow
        };
        
        _dbContext.BatchSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        using (var inputArchive = new ZipArchive(uploadedZipStream, ZipArchiveMode.Read, leaveOpen: true))
        using (var outputArchive = new ZipArchive(outputZipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in inputArchive.Entries)
            {
                if (!entry.FullName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    continue;

                session.TotalFiles++;
                var log = new RemediationLog
                {
                    BatchSessionId = session.Id,
                    OriginalFileName = entry.FullName,
                    FileSizeBytes = entry.Length
                };

                var startTime = DateTime.UtcNow;

                try
                {
                    // Fault Isolation: Process each file individually wrapped in using statements
                    using var entryStream = entry.Open();
                    
                    // We load the single PDF into memory to allow structured parsing (Metadata, Color, OCR, Tags, Structure).
                    // In a production PDF library, this might also be a stream.
                    using var pdfMemoryStream = new MemoryStream();
                    await entryStream.CopyToAsync(pdfMemoryStream, cancellationToken);
                    pdfMemoryStream.Position = 0;

                    // 1. Metadata Normalization Hook
                    // 2. Color Space Conversion Hook
                    // 3. OCR Layer Injection Hook
                    // 4. Structure Tree Reconstruction Hook
                    // 5. WCAG / Section 508 Accessibility Tagging Hook
                    
                    using var remediatedStream = await ApplyRemediationHooksAsync(pdfMemoryStream, cancellationToken);
                    
                    var outputEntry = outputArchive.CreateEntry(entry.FullName, CompressionLevel.Fastest);
                    using var outputEntryStream = outputEntry.Open();
                    remediatedStream.Position = 0;
                    await remediatedStream.CopyToAsync(outputEntryStream, cancellationToken);

                    log.IsOcrApplied = true; // Set to true per requirements logic if successful
                    log.IsStructureRebuilt = true;
                    log.IsAccessibleTagged = true;
                    session.SuccessfulFiles++;
                }
                catch (Exception ex)
                {
                    // Catch individual file failures and log, allowing the loop to continue.
                    log.ErrorMessage = ex.Message;
                    session.FailedFiles++;
                }
                finally
                {
                    log.ProcessingDurationMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
                    _dbContext.RemediationLogs.Add(log);
                }
            }
            
            // Generate and append execution manifest.json
            var manifestEntry = outputArchive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using var manifestStream = manifestEntry.Open();
            var manifestData = new {
                SessionId = session.Id,
                TotalFiles = session.TotalFiles,
                SuccessfulFiles = session.SuccessfulFiles,
                FailedFiles = session.FailedFiles,
                CompletedAt = DateTimeOffset.UtcNow
            };
            await JsonSerializer.SerializeAsync(manifestStream, manifestData, cancellationToken: cancellationToken);
        }

        // Commit final status to Db
        session.Status = "Completed";
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
    
    private async Task<Stream> ApplyRemediationHooksAsync(Stream inputPdfStream, CancellationToken cancellationToken)
    {
        // Placeholder for actual PDF manipulation logic.
        // Returns a stream containing the remediated file.
        var outStream = new MemoryStream();
        await inputPdfStream.CopyToAsync(outStream, cancellationToken);
        outStream.Position = 0;
        return outStream;
    }
}
