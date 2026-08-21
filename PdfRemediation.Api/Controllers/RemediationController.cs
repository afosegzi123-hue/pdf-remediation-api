using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PdfRemediation.Api.Services;

namespace PdfRemediation.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RemediationController : ControllerBase
{
    private readonly IBatchWorkflowService _workflowService;

    public RemediationController(IBatchWorkflowService workflowService)
    {
        _workflowService = workflowService;
    }

    [HttpPost("batch")]
    [RequestSizeLimit(100_000_000)] // 100MB limit
    public async Task<IActionResult> ProcessBatch(CancellationToken cancellationToken)
    {
        // Manually extract the file from the request to avoid model binding issues with proxied requests
        if (!Request.HasFormContentType)
        {
            return BadRequest("Request must be multipart/form-data.");
        }

        var form = await Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");

        if (file == null || file.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }
        
        if (!file.FileName.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Uploaded file must be a ZIP archive.");
        }

        try
        {
            Response.ContentType = "application/zip";
            Response.Headers.Append("Content-Disposition", "attachment; filename=\"remediated_batch.zip\"");

            using var uploadStream = file.OpenReadStream();
            
            // Write the zip stream directly to the HTTP response stream
            await _workflowService.ProcessBatchArchiveAsync(uploadStream, Response.Body, cancellationToken);
            
            return new EmptyResult();
        }
        catch (System.Exception ex)
        {
            Console.WriteLine($"ERROR in ProcessBatch: {ex}");
            // Only return error if headers haven't been sent yet
            if (!Response.HasStarted)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
            return new EmptyResult();
        }
    }
}
