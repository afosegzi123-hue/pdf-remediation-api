using System;

namespace PdfRemediation.Api.Models
{
    public class RemediationLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid BatchSessionId { get; set; }
        public BatchSession? BatchSession { get; set; }
        public string OriginalFileName { get; set; } = "";
        public long FileSizeBytes { get; set; }
        public bool IsOcrApplied { get; set; }
        public bool IsStructureRebuilt { get; set; }
        public bool IsAccessibleTagged { get; set; }
        public int ProcessingDurationMs { get; set; }
        public string? ErrorMessage { get; set; }
        public string? RemediatedFileName { get; set; }
        public string? DownloadUrl { get; set; }
    }
}
