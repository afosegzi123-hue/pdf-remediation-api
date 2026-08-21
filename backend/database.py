from sqlalchemy import create_engine, Column, Integer, String, Boolean, DateTime, Float
from sqlalchemy.orm import sessionmaker, declarative_base
from datetime import datetime
import os

# Uses standard PostgreSQL connection string from Supabase
DATABASE_URL = os.getenv("SUPABASE_DB_URL", "sqlite:///./temp_fallback.db")

engine = create_engine(DATABASE_URL)
SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)
Base = declarative_base()

class BatchSession(Base):
    __tablename__ = "batch_sessions"
    id = Column(String, primary_key=True, index=True)
    status = Column(String)
    total_files = Column(Integer, default=0)
    successful_files = Column(Integer, default=0)
    failed_files = Column(Integer, default=0)
    created_at = Column(DateTime, default=datetime.utcnow)

class RemediationLog(Base):
    __tablename__ = "remediation_logs"
    id = Column(Integer, primary_key=True, index=True, autoincrement=True)
    batch_session_id = Column(String, index=True)
    original_file_name = Column(String)
    file_size_bytes = Column(Integer)
    processing_duration_ms = Column(Float)
    is_ocr_applied = Column(Boolean, default=False)
    is_structure_rebuilt = Column(Boolean, default=False)
    is_accessible_tagged = Column(Boolean, default=False)
    error_message = Column(String, nullable=True)

# Create tables if using SQLite fallback, though Supabase will use migrations
Base.metadata.create_all(bind=engine)

def get_db():
    db = SessionLocal()
    try:
        yield db
    finally:
        db.close()
