/**
 * API client to interact with the .NET PDF Remediation Backend.
 */

export const uploadBatchArchive = async (file: File): Promise<Blob> => {
  const baseUrl = process.env.NEXT_PUBLIC_API_BASE_URL || 'http://localhost:5246';
  
  const formData = new FormData();
  formData.append('file', file);

  const response = await fetch(`${baseUrl}/api/remediation/batch`, {
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
