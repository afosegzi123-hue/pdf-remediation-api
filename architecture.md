# System Architecture
[Client Browser]
│
▼ (HTTPS POST multipart/form-data)
[Hugging Face Space: Python Docker Monolith]
├──► (Port 7860) [FastAPI Web Server]
│    ├──► Serves Next.js Static Files (Frontend UI & Admin Portal)
│    └──► Handles `/api/remediation` endpoints
├──► [Deep Learning Layout Engine (PyTorch / LayoutParser)]
└──► [Supabase API]
     ├──► [PostgreSQL Managed Database (Session & Log tracking)]
     └──► [S3-Compatible Storage Buckets (File persistence)]

## Component Technical Specs
* **Frontend:** Next.js (React) static export.
* **API Framework:** FastAPI (`uvicorn` ASGI server).
* **AI & PDF Manipulation:** `PyMuPDF` (fitz), `layoutparser`, `torch`.
* **Database Driver:** `supabase-py` (or `sqlalchemy` + `psycopg2`).
* **Container Environment:** Python 3.10 slim base runtime (Ubuntu).