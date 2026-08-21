'use client';

import { useState } from 'react';
import UploadDropzone from '@/components/UploadDropzone';
import { processPdf, RemediationOptions } from '@/lib/api';

export default function Home() {
  const [file, setFile] = useState<File | null>(null);
  const [status, setStatus] = useState<'idle' | 'uploading' | 'processing' | 'completed' | 'error'>('idle');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  
  const [options, setOptions] = useState<RemediationOptions>({
    normalize_metadata: true,
    tag_language: true,
    auto_tag_structure: false,
  });

  const handleFileSelect = (selectedFile: File) => {
    setFile(selectedFile);
    setStatus('idle');
    setErrorMessage(null);
  };

  const handleProcess = async () => {
    if (!file) return;

    setStatus('uploading');
    setErrorMessage(null);

    try {
      setStatus('processing');
      const { blob, filename } = await processPdf(file, options);

      const downloadUrl = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = downloadUrl;
      link.download = filename;
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.URL.revokeObjectURL(downloadUrl);

      setStatus('completed');
    } catch (error: any) {
      console.error(error);
      setErrorMessage(error.message || 'An unexpected error occurred during processing.');
      setStatus('error');
    }
  };

  return (
    <main className="min-h-screen bg-slate-50 flex flex-col items-center py-20 px-4 sm:px-6 lg:px-8">
      <div className="w-full max-w-4xl space-y-12">
        
        <div className="text-center space-y-4">
          <h1 className="text-4xl md:text-5xl font-extrabold tracking-tight text-slate-900">
            PDF Remediation <span className="text-blue-600">Suite</span>
          </h1>
          <p className="text-lg text-slate-600 max-w-2xl mx-auto">
            Ensure full WCAG 2.1 AA / Section 508 compliance. Upload a single PDF or a batch ZIP archive.
          </p>
        </div>

        <div className="bg-white shadow-xl shadow-slate-200/50 rounded-3xl p-8 border border-slate-100">
          <UploadDropzone 
            onFileSelect={handleFileSelect} 
            isLoading={status === 'uploading' || status === 'processing'} 
          />

          {file && (
            <div className="mt-8 flex flex-col items-center animate-in fade-in slide-in-from-bottom-4">
              <div className="flex items-center space-x-3 mb-6 p-4 bg-slate-50 rounded-xl border border-slate-200 w-full max-w-md">
                <svg className="w-8 h-8 text-blue-500 flex-shrink-0" fill="currentColor" viewBox="0 0 20 20">
                  <path fillRule="evenodd" d="M4 4a2 2 0 012-2h4.586A2 2 0 0112 2.586L15.414 6A2 2 0 0116 7.414V16a2 2 0 01-2 2H6a2 2 0 01-2-2V4zm2 6a1 1 0 011-1h6a1 1 0 110 2H7a1 1 0 01-1-1zm1 3a1 1 0 100 2h6a1 1 0 100-2H7z" clipRule="evenodd" />
                </svg>
                <div className="flex-1 min-w-0">
                  <p className="text-sm font-medium text-slate-900 truncate">
                    {file.name}
                  </p>
                  <p className="text-xs text-slate-500">
                    {(file.size / 1024 / 1024).toFixed(2)} MB
                  </p>
                </div>
              </div>

              <div className="w-full max-w-md bg-slate-50 p-4 rounded-xl border border-slate-200 mb-6 space-y-3">
                <h3 className="text-sm font-semibold text-slate-700">Remediation Options</h3>
                <label className="flex items-center space-x-3">
                  <input type="checkbox" checked={options.normalize_metadata} onChange={e => setOptions({...options, normalize_metadata: e.target.checked})} className="form-checkbox h-4 w-4 text-blue-600 rounded border-slate-300" disabled={status !== 'idle'} />
                  <span className="text-sm text-slate-700">Normalize Metadata</span>
                </label>
                <label className="flex items-center space-x-3">
                  <input type="checkbox" checked={options.tag_language} onChange={e => setOptions({...options, tag_language: e.target.checked})} className="form-checkbox h-4 w-4 text-blue-600 rounded border-slate-300" disabled={status !== 'idle'} />
                  <span className="text-sm text-slate-700">Set Language & Accessibility Flags</span>
                </label>
                <label className="flex items-center space-x-3">
                  <input type="checkbox" checked={options.auto_tag_structure} onChange={e => setOptions({...options, auto_tag_structure: e.target.checked})} className="form-checkbox h-4 w-4 text-blue-600 rounded border-slate-300" disabled={status !== 'idle'} />
                  <span className="text-sm text-slate-700">Auto-Tag Structure (Supercharged Option B)</span>
                </label>
              </div>

              <button
                onClick={handleProcess}
                disabled={status === 'uploading' || status === 'processing'}
                className="w-full max-w-md bg-blue-600 hover:bg-blue-700 disabled:bg-blue-400 text-white font-semibold py-3 px-6 rounded-xl shadow-sm transition-all duration-200 flex items-center justify-center space-x-2"
              >
                {status === 'idle' || status === 'completed' || status === 'error' ? (
                  <span>Start Remediation</span>
                ) : (
                  <>
                    <svg className="animate-spin -ml-1 mr-3 h-5 w-5 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                    </svg>
                    <span>Processing...</span>
                  </>
                )}
              </button>
            </div>
          )}

          {/* Status Messages */}
          {status === 'completed' && (
            <div className="mt-6 p-4 bg-green-50 text-green-800 rounded-xl border border-green-200 text-center animate-in fade-in slide-in-from-bottom-2">
              <p className="font-semibold flex items-center justify-center gap-2">
                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" /></svg>
                Processing Complete!
              </p>
              <p className="text-sm mt-1 text-green-700">Your remediated ZIP archive has been downloaded automatically.</p>
            </div>
          )}

          {status === 'error' && errorMessage && (
            <div className="mt-6 p-4 bg-red-50 text-red-800 rounded-xl border border-red-200 text-center animate-in fade-in slide-in-from-bottom-2">
              <p className="font-semibold flex items-center justify-center gap-2">
                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" /></svg>
                Processing Failed
              </p>
              <p className="text-sm mt-1 text-red-700">{errorMessage}</p>
            </div>
          )}
        </div>
      </div>
    </main>
  );
}
