/**
 * API client to interact with the .NET PDF Remediation Backend.
 * 
 * Uses Next.js rewrites to proxy /api/* requests through the same Vercel domain,
 * completely eliminating CORS issues.
 */

export const uploadBatchArchive = async (file: File): Promise<Blob> => {
  const formData = new FormData();
  formData.append('file', file);

  const response = await fetch('/api/remediation/batch', {
    method: 'POST',
    body: formData,
  });

  if (!response.ok) {
    let errorMessage = 'Failed to process the batch archive.';
    try {
      const errorData = await response.text();
      errorMessage = errorData || errorMessage;
    } catch {
      // Ignore
    }
    throw new Error(errorMessage);
  }

  // Return the streamed zip file as a Blob
  return await response.blob();
};
