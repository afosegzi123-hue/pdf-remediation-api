/**
 * API client to interact with the .NET PDF Remediation Backend.
 * 
 * Uses Next.js rewrites to proxy /api/* requests through the same Vercel domain,
 * completely eliminating CORS issues.
 */

export const uploadBatchArchive = async (file: File): Promise<Blob> => {
  const formData = new FormData();
  formData.append('file', file);

  // Use AbortController with a generous timeout to handle Render free-tier cold starts
  // (cold starts can take 1-3+ minutes on Render's free plan)
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), 300_000); // 5 minute timeout

  try {
    const response = await fetch('/api/remediation/batch', {
      method: 'POST',
      body: formData,
      signal: controller.signal,
    });

    clearTimeout(timeoutId);

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
  } catch (error: any) {
    clearTimeout(timeoutId);
    
    if (error.name === 'AbortError') {
      throw new Error(
        'The server took too long to respond. Render free-tier services may take 1-3 minutes to wake up from sleep. Please try again.'
      );
    }
    throw error;
  }
};

/**
 * Wake up the Render backend by hitting the health endpoint.
 * Call this early (e.g. on page load) so the server is warm by the time the user uploads.
 */
export const warmUpBackend = async (): Promise<boolean> => {
  try {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 180_000); // 3 minute timeout
    
    const response = await fetch('/api/health', {
      method: 'GET',
      signal: controller.signal,
    });
    
    clearTimeout(timeoutId);
    return response.ok;
  } catch {
    return false;
  }
};
