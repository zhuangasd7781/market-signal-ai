import { errorText } from './displayText';

export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const response = await fetch(`/api${path}`, {
    ...options, headers: { ...(options.body ? { 'Content-Type': 'application/json' } : {}), 'X-Market-Signal': 'web', ...options.headers },
  });
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    throw new Error(errorText(body?.message ?? '服務暫時無法使用，請稍後再試。', response.status));
  }
  return response.status === 204 ? undefined as T : response.json();
}
export async function apiOptional<T>(path: string): Promise<T | null> {
  const response = await fetch(`/api${path}`, { headers: { 'X-Market-Signal': 'web' } });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error('服務暫時無法使用，請稍後再試。');
  return response.json();
}
