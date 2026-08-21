# Implementation Tasks Roadmap

- [x] **Task 1:** Initialize .NET 8 Web API project structure (`PdfRemediation.Api`) with necessary NuGet package references (`Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`). Verify initial project compilation.
- [x] **Task 2:** Create Entity Framework Core data context (`AppDbContext`), mapping models for `BatchSession` and `RemediationLog`. Configure connection string resolution via environment variables.
- [x] **Task 3:** Implement core batch workflow service handling multipart ZIP extraction, stream processing loops, metadata/color/OCR/tagging hooks, and error isolation.
- [x] **Task 4:** Build the API controller mapping `POST /api/remediation/batch` to ingest archives, invoke processing, log results, and return stream archives.
- [x] **Task 5:** Author the production multi-stage `Dockerfile` and Render configuration blueprint (`render.yaml`). Run container build checks.