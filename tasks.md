# Implementation Tasks Roadmap

- [x] **Phase 1 (Backend Setup):** Initialize .NET 8 Web API project structure (`PdfRemediation.Api`) with EF Core, JWT, and Supabase integration. Configure `render.yaml` for deployment.
- [x] **Phase 2 (Strict 1:1 Layout Engine):** Implement the `HeuristicPdfEngine.cs` using iText7. Replace automatic flow with absolute `SetFixedPosition` pagination to guarantee visual fidelity.
- [x] **Phase 3 (Alignment & Bounds):** Build algorithms for true paragraph alignment detection (Left, Right, Centered, Justified).
- [x] **Phase 4 (Table Fixes & Custom Roles):** Fix table overlaps with exact cell height bounds and 10% dynamic font scaling. Map custom `Header`/`Footer` string tags to `StandardRoles.NONSTRUCT`.
- [ ] **Phase 5 (Next.js Admin Portal):** Construct the Next.js `/akin` dashboard connected to Supabase for file management and log tracking.