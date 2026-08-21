from fastapi import FastAPI, UploadFile, File, Form, Depends, HTTPException, Request
from fastapi.responses import Response, FileResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
from sqlalchemy.orm import Session
from backend.database import get_db, BatchSession, RemediationLog
from backend.storage import upload_to_storage, delete_from_storage
from backend.security import limiter, verify_token, is_safe_zip_extraction
from backend.services.pdf_engine import apply_remediation
import zipfile
import io
import json
import uuid
from datetime import datetime
import asyncio
from concurrent.futures import ThreadPoolExecutor

app = FastAPI(title="PDF Remediation API")
app.state.limiter = limiter

# Thread pool for CPU bound PDF operations to not block the async event loop
executor = ThreadPoolExecutor(max_workers=4)

# Options mapping from frontend
def parse_options(options_str: str) -> dict:
    try:
        return json.loads(options_str)
    except:
        return {"normalize_metadata": True, "tag_language": True, "auto_tag_structure": False}

@app.post("/api/remediation/single")
@limiter.limit("10/minute")
async def process_single(request: Request, file: UploadFile = File(...), options: str = Form("{}"), db: Session = Depends(get_db)):
    if not file.filename.endswith(".pdf"):
        raise HTTPException(status_code=400, detail="File must be a PDF")
        
    opts = parse_options(options)
    pdf_bytes = await file.read()
    
    loop = asyncio.get_running_loop()
    try:
        # Run CPU bound task in thread pool
        remediated_bytes = await loop.run_in_executor(executor, apply_remediation, pdf_bytes, opts)
        
        # Log to Supabase DB
        log = RemediationLog(original_file_name=file.filename, file_size_bytes=len(pdf_bytes), is_accessible_tagged=opts.get("auto_tag_structure", False))
        db.add(log)
        db.commit()
        
        # Upload to Supabase Storage (for Admin access)
        upload_to_storage("remediated-pdfs", f"single_{log.id}_{file.filename}", remediated_bytes)
        
        return Response(content=remediated_bytes, media_type="application/pdf", headers={"Content-Disposition": f"attachment; filename=remediated_{file.filename}"})
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/api/remediation/batch")
@limiter.limit("5/minute")
async def process_batch(request: Request, file: UploadFile = File(...), options: str = Form("{}"), db: Session = Depends(get_db)):
    if not file.filename.endswith(".zip"):
        raise HTTPException(status_code=400, detail="File must be a ZIP archive")
        
    opts = parse_options(options)
    zip_bytes = await file.read()
    
    session_id = str(uuid.uuid4())
    db_session = BatchSession(id=session_id, status="Processing")
    db.add(db_session)
    db.commit()

    output_zip_buffer = io.BytesIO()
    
    try:
        with zipfile.ZipFile(io.BytesIO(zip_bytes), 'r') as input_zip:
            # ZIP Bomb Protection
            total_uncompressed = sum(info.file_size for info in input_zip.infolist())
            if not is_safe_zip_extraction(total_uncompressed):
                raise Exception("ZIP extraction size exceeds safe limits (ZIP Bomb protection).")
                
            with zipfile.ZipFile(output_zip_buffer, 'w', zipfile.ZIP_DEFLATED) as output_zip:
                for entry in input_zip.infolist():
                    if not entry.filename.lower().endswith('.pdf'):
                        continue
                        
                    db_session.total_files += 1
                    file_data = input_zip.read(entry.filename)
                    
                    try:
                        loop = asyncio.get_running_loop()
                        remediated_data = await loop.run_in_executor(executor, apply_remediation, file_data, opts)
                        output_zip.writestr(entry.filename, remediated_data)
                        db_session.successful_files += 1
                        
                        upload_to_storage("remediated-pdfs", f"batch_{session_id}_{entry.filename}", remediated_data)
                    except Exception as e:
                        db_session.failed_files += 1
                        output_zip.writestr(f"{entry.filename}.error.txt", str(e))
                        output_zip.writestr(entry.filename, file_data)
                        
    except Exception as e:
        db_session.status = f"Failed: {str(e)}"
        db.commit()
        raise HTTPException(status_code=500, detail=str(e))
        
    db_session.status = "Completed"
    db.commit()
    
    return Response(content=output_zip_buffer.getvalue(), media_type="application/zip", headers={"Content-Disposition": f"attachment; filename=remediated_batch.zip"})

@app.get("/api/admin/files")
def list_admin_files(token: str, db: Session = Depends(get_db)):
    if not verify_token(token):
        raise HTTPException(status_code=401, detail="Unauthorized")
    logs = db.query(RemediationLog).all()
    return logs

# Mount frontend static files
# Ensure the Next.js `out` directory exists or fallback to a dummy response
import os
frontend_path = os.path.join(os.path.dirname(__file__), "..", "frontend", "out")
if os.path.exists(frontend_path):
    app.mount("/", StaticFiles(directory=frontend_path, html=True), name="frontend")
else:
    @app.get("/")
    def index():
        return {"message": "Frontend not built yet. Run npm run build in /frontend and ensure output is 'export'."}
