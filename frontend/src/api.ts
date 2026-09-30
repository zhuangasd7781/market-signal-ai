export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const response = await fetch(`/api${path}`, {
    ...options, headers: { ...(options.body ? { 'Content-Type': 'application/json' } : {}), 'X-Market-Signal': 'web', ...options.headers },
  });
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    throw new Error(body?.message ?? '服務暫時無法使用，請稍後再試。');
  }
  return response.status === 204 ? undefined as T : response.json();
}
