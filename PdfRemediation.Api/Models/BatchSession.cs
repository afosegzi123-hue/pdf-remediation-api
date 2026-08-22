using System;
using System.Collections.Generic;

namespace PdfRemediation.Api.Models
{
    public class BatchSession
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int TotalFiles { get; set; }
        public int SuccessfulFiles { get; set; }
        public int FailedFiles { get; set; }
        public string Status { get; set; } = "Pending";

        public List<RemediationLog> RemediationLogs { get; set; } = new();
    }
}
