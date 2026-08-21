using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PdfRemediation.Api.Services;

namespace PdfRemediation.Api.Controllers;

[Route("api/[controller]")]
public class RemediationController : ControllerBase
{
    private readonly IBatchWorkflowService _workflowService;

    public RemediationController(IBatchWorkflowService workflowService)
    {
        _workflowService = workflowService;
    }

    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok("pong");
    }

    [HttpPost("batch")]
    [RequestSizeLimit(100_000_000)] // 100MB limit
    public async Task<IActionResult> ProcessBatch(CancellationToken cancellationToken)
    {
        try
        {
            // Manually extract the file from the request
            if (!Request.HasFormContentType)
            {
                return Content("Request must be multipart/form-data.", "text/plain");
            }

            var form = await Request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");

            if (file == null || file.Length == 0)
            {
                return Content("No file uploaded.", "text/plain");
            }
            
            if (!file.FileName.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase))
            {
                return Content("Uploaded file must be a ZIP archive.", "text/plain");
            }

            Response.ContentType = "application/zip";
            Response.Headers.Append("Content-Disposition", "attachment; filename=\"remediated_batch.zip\"");

            using var uploadStream = file.OpenReadStream();
            
            await _workflowService.ProcessBatchArchiveAsync(uploadStream, Response.Body, cancellationToken);
            
            return new EmptyResult();
        }
        catch (System.Exception ex)
        {
            Console.WriteLine($"ERROR in ProcessBatch: {ex}");
            if (!Response.HasStarted)
            {
                return Content($"Internal server error: {ex.Message}", "text/plain");
            }
            return new EmptyResult();
        }
    }
}
