# Antigravity Kickoff Prompt: PDF Remediation Suite

Hello. You are assigned as the Principal Solution Architect & .NET Engineer for the Web-Based PDF Remediation Software.

I have established the comprehensive project specification files in the root of this workspace:
1. `prd.md` - Core requirements, Render/Vercel split, and synchronous ZIP batch upload rules.
2. `architecture.md` - Technical design of the .NET 8 Web API container and PostgreSQL integration.
3. `schema.md` - PostgreSQL database schema (`BatchSessions` and `RemediationLogs`).
4. `data.md` - Data flow lifecycle for multipart ZIP ingestion and stream processing.
5. `tasks.md` - Your highly structured, phase-gated execution roadmap.
6. `skills.md` - Standard operating procedures for memory management, stream disposal, and fault isolation.
7. `agents.md` - Subagent roles and orchestration rules.
8. `prompt.md` - Execution guidelines.

Please execute the following initialization protocol:
1. Read and index all eight markdown specification files above.
2. Confirm you understand the strict requirement to stop for my approval after every phase.
3. Execute **Task 1** from `tasks.md` (Scaffolding the .NET 8 Web API project structure and installing necessary NuGet packages like `Npgsql.EntityFrameworkCore.PostgreSQL`).
4. Run `dotnet build` to verify the scaffolding compiles cleanly, output a status summary, and pause, awaiting my confirmation to move to Task 2.