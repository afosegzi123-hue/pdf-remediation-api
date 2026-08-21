using System;

namespace PdfRemediation.Api.Models;

public class RemediationLog
{
    public Guid Id { get; set; }
    public Guid BatchSessionId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public bool IsOcrApplied { get; set; }
    public bool IsStructureRebuilt { get; set; }
    public bool IsAccessibleTagged { get; set; }
    public int ProcessingDurationMs { get; set; }
    public string? ErrorMessage { get; set; }

    // Navigation property
    public BatchSession? BatchSession { get; set; }
}
