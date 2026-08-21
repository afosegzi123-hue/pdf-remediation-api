# Product Requirements Document (PRD): Web-Based PDF Remediation Suite

## 1. Executive Summary
An enterprise-grade, high-performance web-based PDF remediation platform designed to process documents synchronously in batch archives (ZIP format). The system ensures full compliance with accessibility standards (WCAG 2.1 AA / Section 508), applies OCR text layers, rebuilds logical structure trees, strips redundant metadata, and normalizes color spaces.

## 2. Core Architecture & Tech Stack
* **Frontend:** Next.js deployed on Vercel.
* **Backend API:** .NET 8 Web API (`net8.0`) hosted on Render via a multi-stage Docker container.
* **Database & Persistence:** PostgreSQL managed via Entity Framework Core (`Npgsql.EntityFrameworkCore.PostgreSQL`).
* **Batch Processing Pattern:** Synchronous multi-file archive endpoint (`multipart/form-data` ZIP upload returning a structured ZIP stream output).

## 3. Functional Requirements
* **FR-01: Synchronous ZIP Batch Upload:** Accept ZIP archives containing multiple target PDFs up to 100MB via a REST API endpoint (`POST /api/remediation/batch`).
* **FR-02: Comprehensive Remediation Pipeline:**
  1. *Metadata Normalization:* Strip generator tags, clean document information dictionaries, and set standard document viewing properties.
  2. *Color Space Conversion:* Normalize DeviceGray/DeviceCMYK color profiles to device-independent sRGB where required.
  3. *OCR Layer Injection:* Analyze scanned raster pages and overlay searchable invisible text layers.
  4. *Structure Tree Reconstruction:* Synthesize logical structural tags (`<Document>`, `<Part>`, `<H1>`, `<H2>`, `<P>`, `<Table>`).
  5. *WCAG / Section 508 Accessibility Tagging:* Embed alternative text defaults, explicit reading order attributes, and primary document language markers (`/Lang en-US`).
* **FR-03: Archive Response & Logging:** Return a structured output ZIP containing remediated PDFs and an execution `manifest.json` report, while logging operational metrics to PostgreSQL.