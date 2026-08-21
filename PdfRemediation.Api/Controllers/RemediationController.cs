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

            // Buffer the output ZIP in memory so we return a clean, complete file
            using var outputStream = new System.IO.MemoryStream();
            using var uploadStream = file.OpenReadStream();
            
            await _workflowService.ProcessBatchArchiveAsync(uploadStream, outputStream, cancellationToken);

            outputStream.Position = 0;
            return File(outputStream.ToArray(), "application/zip", "remediated_batch.zip");
        }
        catch (System.Exception ex)
        {
            Console.WriteLine($"ERROR in ProcessBatch: {ex}");
            Response.StatusCode = 500;
            return Content($"Internal server error: {ex.Message}", "text/plain");
        }
    }
}
