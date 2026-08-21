from fastapi import Request
from slowapi import Limiter
from slowapi.util import get_remote_address
from datetime import datetime, timedelta
from jose import jwt
import os

# Rate Limiter based on IP Address
limiter = Limiter(key_func=get_remote_address)

# Admin Authentication
SECRET_KEY = os.getenv("JWT_SECRET_KEY", "super-secret-fallback-key-change-me")
ALGORITHM = "HS256"
ADMIN_USERNAME = os.getenv("ADMIN_USERNAME", "admin")
ADMIN_PASSWORD = os.getenv("ADMIN_PASSWORD", "admin123") # In production, hash this!

def create_access_token(data: dict):
    to_encode = data.copy()
    expire = datetime.utcnow() + timedelta(hours=2)
    to_encode.update({"exp": expire})
    return jwt.encode(to_encode, SECRET_KEY, algorithm=ALGORITHM)

def verify_token(token: str):
    try:
        payload = jwt.decode(token, SECRET_KEY, algorithms=[ALGORITHM])
        return payload.get("sub") == ADMIN_USERNAME
    except jwt.JWTError:
        return False

# Security check for ZIP Bombs
def is_safe_zip_extraction(uncompressed_size: int, max_allowed_mb: int = 500) -> bool:
    # Reject extraction if uncompressed size exceeds limit (e.g. 500MB)
    max_bytes = max_allowed_mb * 1024 * 1024
    return uncompressed_size <= max_bytes
