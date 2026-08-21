'use client';

import { useState } from 'react';
import { fetchAdminFiles } from '@/lib/api';

export default function AdminPage() {
  const [password, setPassword] = useState('');
  const [token, setToken] = useState('');
  const [logs, setLogs] = useState<any[]>([]);
  const [loggedIn, setLoggedIn] = useState(false);
  const [error, setError] = useState('');

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    // In a real scenario, we would hit a /api/admin/login endpoint to get the JWT.
    // For this rewrite plan, we assume the token is obtained and we just use it.
    // For now, we simulate success if they type something.
    try {
      const data = await fetchAdminFiles(password);
      setLogs(data);
      setToken(password);
      setLoggedIn(true);
      setError('');
    } catch {
      setError('Invalid credentials or unauthorized.');
    }
  };

  if (!loggedIn) {
    return (
      <main className="min-h-screen bg-slate-50 flex items-center justify-center py-20 px-4 sm:px-6 lg:px-8">
        <form onSubmit={handleLogin} className="bg-white p-8 rounded-2xl shadow-xl w-full max-w-sm space-y-6">
          <h1 className="text-2xl font-bold text-center text-slate-900">Admin Portal</h1>
          {error && <div className="text-red-600 text-sm bg-red-50 p-3 rounded-lg">{error}</div>}
          <input
            type="password"
            placeholder="Enter Admin Token"
            className="w-full px-4 py-2 border border-slate-300 rounded-lg focus:ring-2 focus:ring-blue-500 outline-none"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
          <button type="submit" className="w-full bg-blue-600 text-white font-semibold py-2 rounded-lg hover:bg-blue-700">
            Login
          </button>
        </form>
      </main>
    );
  }

  return (
    <main className="min-h-screen bg-slate-50 py-12 px-4 sm:px-6 lg:px-8">
      <div className="max-w-6xl mx-auto space-y-6">
        <div className="flex justify-between items-center">
          <h1 className="text-3xl font-bold text-slate-900">Processed Files</h1>
          <button onClick={() => setLoggedIn(false)} className="text-sm text-slate-600 hover:text-slate-900">Logout</button>
        </div>
        
        <div className="bg-white shadow rounded-xl overflow-hidden">
          <table className="min-w-full divide-y divide-slate-200">
            <thead className="bg-slate-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-slate-500 uppercase">ID</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-slate-500 uppercase">File Name</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-slate-500 uppercase">Size</th>
                <th className="px-6 py-3 text-left text-xs font-medium text-slate-500 uppercase">Actions</th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-slate-200">
              {logs.map((log) => (
                <tr key={log.id}>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-slate-500">{log.id}</td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-slate-900">{log.original_file_name}</td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-slate-500">{log.file_size_bytes}</td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm font-medium space-x-4">
                    <button className="text-blue-600 hover:text-blue-900">Download</button>
                    <button className="text-red-600 hover:text-red-900">Delete</button>
                  </td>
                </tr>
              ))}
              {logs.length === 0 && (
                <tr>
                  <td colSpan={4} className="px-6 py-4 text-center text-sm text-slate-500">No logs found.</td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </main>
  );
}
