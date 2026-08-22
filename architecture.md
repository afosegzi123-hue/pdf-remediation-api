# System Architecture
[Client Browser]
  |
  | (HTTPS POST multipart/form-data)
  |
[Render: .NET 8 Web API Docker Container]
  |  Handles `/api/remediation` endpoints
  |  [C# iText7 Heuristic 1:1 Engine]
  |
  +-- [Vercel: Next.js Frontend] (UI & Admin Portal hosted independently)
  |
  +-- [Supabase API]
       |-- [PostgreSQL Managed Database (Session & Log tracking)]
       |-- [S3-Compatible Storage Buckets (File persistence)]

## Component Technical Specs
* **Frontend:** Next.js (React) deployed on Vercel.
* **API Framework:** ASP.NET Core 8 Web API (`PdfRemediation.Api`).
* **PDF Engine:** `iText7` (C#) custom heuristic parser.
* **Database Driver:** `Supabase-CSharp` / `Npgsql.EntityFrameworkCore.PostgreSQL`.
* **Container Environment:** .NET 8 Alpine/Linux base image (Render).