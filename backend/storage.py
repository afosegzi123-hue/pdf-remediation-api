import os
from supabase import create_client, Client

SUPABASE_URL = os.getenv("SUPABASE_URL", "")
SUPABASE_KEY = os.getenv("SUPABASE_SERVICE_KEY", "")

def get_supabase_client() -> Client:
    # Return a dummy client if keys aren't set (for local dev without crashing)
    if not SUPABASE_URL or not SUPABASE_KEY:
        class DummyStorage:
            def upload(self, path, file): return {}
            def download(self, path): return b""
            def remove(self, path): return {}
            def list(self): return []
            
        class DummyClient:
            def __init__(self):
                self.storage = lambda: self
            def from_(self, bucket):
                return DummyStorage()
                
        return DummyClient()
        
    return create_client(SUPABASE_URL, SUPABASE_KEY)

supabase = get_supabase_client()

def upload_to_storage(bucket_name: str, file_name: str, file_bytes: bytes):
    try:
        supabase.storage().from_(bucket_name).upload(file_name, file_bytes)
    except Exception as e:
        print(f"Storage upload failed: {e}")

def delete_from_storage(bucket_name: str, file_names: list):
    try:
        supabase.storage().from_(bucket_name).remove(file_names)
    except Exception as e:
        print(f"Storage delete failed: {e}")
