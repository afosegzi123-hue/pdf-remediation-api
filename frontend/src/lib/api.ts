export interface RemediationOptions {
  normalize_metadata: boolean;
  tag_language: boolean;
  auto_tag_structure: boolean;
}

// Ensure the API base URL targets the Render .NET backend
const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || 'https://pdf-remediation-api.onrender.com';

export async function processPdf(file: File, options: RemediationOptions): Promise<{ blob: Blob, filename: string }> {
  const formData = new FormData();
  formData.append('file', file);
  
  // The .NET API expects options as a JSON string under the key 'optionsJson'
  formData.append('optionsJson', JSON.stringify({
    NormalizeMetadata: options.normalize_metadata,
    TagLanguage: options.tag_language,
    AutoTagStructure: options.auto_tag_structure
  }));

  const response = await fetch(`${API_BASE_URL}/api/remediation/process`, {
    method: 'POST',
    body: formData,
  });

  if (!response.ok) {
    const errorText = await response.text();
    throw new Error(errorText || 'Processing failed.');
  }

  const blob = await response.blob();
  const contentDisposition = response.headers.get('Content-Disposition');
  let filename = `remediated_${file.name}`;
  if (contentDisposition) {
    const filenameMatch = contentDisposition.match(/filename="?(.+)"?/);
    if (filenameMatch && filenameMatch.length === 2) {
      filename = filenameMatch[1];
    }
  }

  return { blob, filename };
}

export async function fetchAdminFiles(token: string): Promise<any[]> {
  // In a full implementation, this hits a GET endpoint secured by JWT.
  // We mock the response for the frontend UI.
  return [];
}
