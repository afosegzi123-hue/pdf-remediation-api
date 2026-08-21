using System;
using System.Collections.Generic;

namespace PdfRemediation.Api.Models;

public class BatchSession
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int TotalFiles { get; set; }
    public int SuccessfulFiles { get; set; }
    public int FailedFiles { get; set; }
    public string Status { get; set; } = "Pending";

    // Navigation property
    public ICollection<RemediationLog> RemediationLogs { get; set; } = new List<RemediationLog>();
}
