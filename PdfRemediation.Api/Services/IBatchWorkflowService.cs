using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PdfRemediation.Api.Services;

public interface IBatchWorkflowService
{
    Task ProcessBatchArchiveAsync(Stream uploadedZipStream, Stream outputZipStream, CancellationToken cancellationToken = default);
}
