import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from './api';
import { ErrorState, LoadingState } from './components';
type PromptVersion = { id: number; version: string; content: string; createdAt: string };
type Settings = { activeVersionId: number; versions: PromptVersion[] };
export function PromptSettings() {
  const [settings, setSettings] = useState<Settings>();
  const [selectedId, setSelectedId] = useState<number>();
  const [content, setContent] = useState('');
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [busy, setBusy] = useState(false);
  const load = useCallback(async (select?: number) => {
    const data = await api<Settings>('/prompts');
    setSettings(data);
    const selected = data.versions.find(v => v.id === (select ?? data.activeVersionId))!;
    setSelectedId(selected.id); setContent(selected.content);
  }, []);
  useEffect(() => { void load().catch(e => setError((e as Error).message)); }, [load]);
  async function save(e: React.FormEvent) {
    e.preventDefault(); setBusy(true); setError(''); setMessage('');
    try {
      const version = await api<PromptVersion>('/prompts', { method: 'POST', body: JSON.stringify({ content }) });
      await load(version.id); setMessage(`已建立並啟用 ${version.version}`);
    } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }
  async function activate() {
    if (selectedId === undefined) return;
    setBusy(true); setError(''); setMessage('');
    try {
      await api('/prompts/active', { method: 'PUT', body: JSON.stringify({ versionId: selectedId }) });
      await load(selectedId); setMessage('已切換使用中的版本');
    } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }
  return <><Link className="back" to="/">← 我的追蹤</Link><div className="page-heading"><div><span className="eyebrow">INVESTMENT ANALYSIS PROMPT</span><h1>Prompt 設定</h1><p className="muted">GPT 與 DeepSeek 共用核心投資規則。儲存時建立新版本，歷史內容保留。</p></div></div>
    {error && <ErrorState message={error} retry={() => void load().catch(e => setError((e as Error).message))} />}
    {message && <p role="status">{message}</p>}
    {!settings ? !error && <LoadingState /> : <section className="panel prompt-settings">
      <p>使用中的版本：<strong>{settings.versions.find(v => v.id === settings.activeVersionId)?.version}</strong></p>
      <label>版本紀錄<select aria-label="版本紀錄" disabled={busy} value={selectedId} onChange={e => {
        const version = settings.versions.find(v => v.id === Number(e.target.value))!;
        setSelectedId(version.id); setContent(version.content); setMessage('');
      }}>{settings.versions.map(v => <option key={v.id} value={v.id}>{v.version}{v.id === settings.activeVersionId ? '（使用中）' : ''} · {new Date(v.createdAt).toLocaleString('zh-TW', { timeZone: 'Asia/Taipei' })}</option>)}</select></label>
      <form onSubmit={e => void save(e)}><label>Prompt 內容<textarea aria-label="Prompt 內容" required maxLength={30000} rows={18} disabled={busy} value={content} onChange={e => setContent(e.target.value)} /></label>
        <p className="muted small">JSON Schema、持倉數量驗證與市場資料來源由系統固定管理。修改 Prompt 不會改變輸出結構；設定操作不會執行 AI 分析。</p>
        <div className="prompt-actions"><button className="primary" disabled={busy || !content.trim()}>儲存為新版本並啟用</button><button type="button" disabled={busy || selectedId === settings.activeVersionId} onClick={() => void activate()}>啟用選取的已儲存版本</button></div>
      </form></section>}
  </>;
}
