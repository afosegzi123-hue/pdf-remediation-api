/**
 * API client to interact with the Python FastAPI Backend.
 * Uses relative URLs since the frontend is served statically by the backend itself.
 */

// Since the frontend and backend are on the same origin in Hugging Face, we use relative paths.
const API_BASE_URL = '';

export interface RemediationOptions {
  normalize_metadata: boolean;
  tag_language: boolean;
  auto_tag_structure: boolean;
}

export const processPdf = async (file: File, options: RemediationOptions): Promise<{ blob: Blob, filename: string }> => {
  const formData = new FormData();
  formData.append('file', file);
  formData.append('options', JSON.stringify(options));

  const isZip = file.name.toLowerCase().endsWith('.zip');
  const endpoint = isZip ? '/api/remediation/batch' : '/api/remediation/single';

  const response = await fetch(`${API_BASE_URL}${endpoint}`, {
    method: 'POST',
    body: formData,
  });

  if (!response.ok) {
    let errorMessage = `Server returned ${response.status} ${response.statusText}.`;
    try {
      const errorData = await response.text();
      if (errorData) {
        errorMessage += ` Details: ${errorData}`;
      }
    } catch {
      // Ignore
    }
    throw new Error(errorMessage);
  }

  // Get filename from Content-Disposition header if possible
  const contentDisposition = response.headers.get('Content-Disposition');
  let filename = isZip ? 'remediated_batch.zip' : `remediated_${file.name}`;
  if (contentDisposition) {
    const filenameMatch = contentDisposition.match(/filename="?([^"]+)"?/);
    if (filenameMatch && filenameMatch.length === 2) {
      filename = filenameMatch[1];
    }
  }

  const blob = await response.blob();
  return { blob, filename };
};

export const fetchAdminFiles = async (token: string) => {
  const response = await fetch(`${API_BASE_URL}/api/admin/files?token=${encodeURIComponent(token)}`, {
    method: 'GET',
  });
  if (!response.ok) throw new Error("Unauthorized");
  return response.json();
};
