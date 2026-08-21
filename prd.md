# Product Requirements Document (PRD): Web-Based PDF Remediation Suite

## 1. Executive Summary
An enterprise-grade, high-performance web-based PDF remediation platform designed to process documents synchronously in batch archives (ZIP format). The system ensures full compliance with accessibility standards (WCAG 2.1 AA / Section 508), applies OCR text layers, rebuilds logical structure trees, strips redundant metadata, and normalizes color spaces.

## 2. Core Architecture & Tech Stack
* **Frontend:** Next.js (Static Export) hosted natively within the Python container.
* **Backend API:** Python FastAPI hosted on Hugging Face Spaces (Docker).
* **Database & Storage:** Supabase (PostgreSQL for session logging, S3-compatible Buckets for file persistence).
* **Batch & Single Processing:** Endpoints to handle both standalone `.pdf` files and `.zip` batch archives.

## 3. Functional Requirements
* **FR-01: Upload Endpoints:** Accept both `.pdf` and `.zip` archives via REST API (`POST /api/remediation/single` and `POST /api/remediation/batch`).
* **FR-02: Granular Accessibility Selection:** Allow users to specify target remediation steps via UI checkboxes (Metadata, OCR, Auto-Tagging).
* **FR-03: AI Layout Engine (Auto-Tagging):** Utilize Python-based Deep Learning models (e.g., LayoutParser) to automatically detect reading order, headings, and tables, converting them to PDF MCIDs.
* **FR-04: Admin Portal:** Provide a secure, password-protected permalink (`/akin`) for administrators to view processing logs and permanently delete files from Supabase to manage storage limits.
* **FR-05: Scalability & Security:** Implement strict ZIP bomb protection, rate-limiting (slowapi), and asynchronous multi-threading to handle concurrent user operations effectively.