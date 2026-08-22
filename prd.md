# Product Requirements Document (PRD): Web-Based PDF Remediation Suite

## 1. Executive Summary
An enterprise-grade, high-performance web-based PDF remediation platform designed to process documents synchronously in batch archives (ZIP format). The system ensures full compliance with accessibility standards (WCAG 2.1 AA / Section 508) by recreating the document structure tree, while maintaining strict **1:1 visual layout fidelity** with the original document (absolute pagination, true alignment, and strict table boundaries).

## 2. Core Architecture & Tech Stack (Supercharged Option B)
* **Frontend:** Next.js hosted independently on Vercel.
* **Backend API:** .NET 8 (C#) Web API hosted on Render's Free Tier (512MB RAM constraint).
* **PDF Engine:** iText7 (C#) for binary PDF manipulation and heuristic logic.
* **Database & Storage:** Supabase (PostgreSQL for session logging, S3-Compatible Buckets for file persistence).
* **Batch & Single Processing:** Endpoints to handle both standalone `.pdf` files and `.zip` batch archives.

## 3. Functional Requirements
* **FR-01: Upload Endpoints:** Accept both `.pdf` and `.zip` archives via REST API (`POST /api/remediation/single` and `POST /api/remediation/batch`).
* **FR-02: Granular Accessibility Selection:** Allow users to specify target remediation steps via UI checkboxes (Metadata, Auto-Tagging, etc.).
* **FR-03: Heuristic Layout Engine (Strict 1:1 Replica):** Utilize a custom C# iText7 algorithm to parse text, detect alignment (Left, Right, Centered, Justified), map headers/footers to custom tags, fix table overlaps via strict cell heights, and enforce absolute positioning to prevent overflow or blank pages.
* **FR-04: Admin Portal:** Provide a secure, password-protected permalink (`/akin`) for administrators to view processing logs and permanently delete files from Supabase.
* **FR-05: Scalability & Security:** Implement strict ZIP bomb protection, rate-limiting, CORS, and memory-efficient stream processing to survive Render's strict RAM limits.