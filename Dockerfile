# Stage 1: Build the Next.js Frontend
FROM node:20-alpine AS frontend-builder
WORKDIR /app/frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ .
RUN npm run build
# Output will be in /app/frontend/out due to output: 'export' in next.config.ts

# Stage 2: Build the Python FastAPI Backend & LayoutParser
FROM python:3.10-slim

# Install system dependencies for PyMuPDF and Detectron2/LayoutParser
RUN apt-get update && apt-get install -y \
    build-essential \
    libgl1-mesa-glx \
    libglib2.0-0 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Install Python requirements
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy Backend code
COPY backend/ ./backend/

# Copy built static frontend from Stage 1 into the backend directory
COPY --from=frontend-builder /app/frontend/out ./frontend/out

# Expose Hugging Face Space default port
EXPOSE 7860

# Run Uvicorn server
CMD ["uvicorn", "backend.main:app", "--host", "0.0.0.0", "--port", "7860"]
