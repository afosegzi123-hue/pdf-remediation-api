# Data Flow & Pipeline Model

1. **Client Submission:** The Vercel frontend uploads a ZIP bundle via multipart form-data to Render (`POST /api/remediation/batch`).
2. **Session Initialization:** The .NET 8 API creates a `BatchSession` entity in PostgreSQL with status `Processing`.
3. **Stream Decompression:** Using `System.IO.Compression.ZipArchive`, the application enumerates entries in memory/temp storage without exhausting system heap space.
4. **Item Pipeline Execution:** For every valid `.pdf` entry:
   * Load document stream into memory model.
   * Run structural inspection and remediation sequence (Metadata, Color, OCR, Tags, Structure).
   * Record individual item status metrics in `RemediationLogs`.
5. **Compilation & Response:** Remediated items and an execution manifest are packaged into an outbound `ZipArchive`, returned synchronously to the client connection, and the session status is updated to `Completed`.