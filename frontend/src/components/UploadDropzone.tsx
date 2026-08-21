'use client';

import React, { useCallback, useState } from 'react';

interface UploadDropzoneProps {
  onFileSelect: (file: File) => void;
  isLoading: boolean;
}

export default function UploadDropzone({ onFileSelect, isLoading }: UploadDropzoneProps) {
  const [isDragOver, setIsDragOver] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(true);
  }, []);

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(false);
  }, []);

  const validateAndSelect = (file: File) => {
    setError(null);
    const isZip = file.name.toLowerCase().endsWith('.zip') || file.type === 'application/zip';
    const isPdf = file.name.toLowerCase().endsWith('.pdf') || file.type === 'application/pdf';
    
    if (!isZip && !isPdf) {
      setError('Please upload a valid .zip archive or a .pdf file.');
      return;
    }
    onFileSelect(file);
  };

  const handleDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(false);

    if (isLoading) return;

    if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
      const file = e.dataTransfer.files[0];
      validateAndSelect(file);
    }
  }, [isLoading, onFileSelect]);

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files.length > 0) {
      const file = e.target.files[0];
      validateAndSelect(file);
    }
  };

  return (
    <div className="w-full max-w-2xl mx-auto">
      <div
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
        onDrop={handleDrop}
        className={`relative flex flex-col items-center justify-center w-full h-64 p-6 border-2 border-dashed rounded-2xl transition-all duration-200 ease-in-out ${
          isDragOver
            ? 'border-blue-500 bg-blue-50 scale-[1.02] shadow-lg shadow-blue-100'
            : 'border-slate-300 bg-slate-50 hover:bg-slate-100'
        } ${isLoading ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'}`}
      >
        <input
          type="file"
          accept=".zip,application/zip,.pdf,application/pdf"
          onChange={handleFileChange}
          disabled={isLoading}
          className="absolute inset-0 w-full h-full opacity-0 cursor-pointer disabled:cursor-not-allowed"
        />
        
        <div className="flex flex-col items-center text-center space-y-4">
          <div className={`p-4 rounded-full ${isDragOver ? 'bg-blue-100 text-blue-600' : 'bg-slate-200 text-slate-500'}`}>
            <svg
              className="w-8 h-8"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              xmlns="http://www.w3.org/2000/svg"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12"
              />
            </svg>
          </div>
          <div>
            <p className="text-lg font-semibold text-slate-700">
              {isDragOver ? 'Drop the file here...' : 'Drag & drop a ZIP or PDF file'}
            </p>
            <p className="text-sm text-slate-500 mt-1">
              or click anywhere to browse your files
            </p>
          </div>
        </div>
      </div>
      
      {error && (
        <div className="mt-4 p-4 text-sm text-red-700 bg-red-100 rounded-lg animate-in fade-in slide-in-from-top-2">
          {error}
        </div>
      )}
    </div>
  );
}
