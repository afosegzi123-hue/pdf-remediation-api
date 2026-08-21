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
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ProcessBatch(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("No file uploaded.");
        }
        
        if (!file.FileName.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Uploaded file must be a ZIP archive.");
        }

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/zip";
        Response.Headers.Append("Content-Disposition", "attachment; filename=\"remediated_batch.zip\"");

        using var uploadStream = file.OpenReadStream();
        
        // Write the zip stream directly to the HTTP response stream to prevent buffering the whole output in memory
        await _workflowService.ProcessBatchArchiveAsync(uploadStream, Response.Body, cancellationToken);
        
        return new EmptyResult();
    }
}
