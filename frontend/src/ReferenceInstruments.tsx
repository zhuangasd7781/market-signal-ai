import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from './api';
import { ErrorState, LoadingState } from './components';

type Instrument = { id: number; symbol: string; name: string; market: string };
type Draft = { id?: number; symbol: string; name: string; market: string };

export function ReferenceInstruments() {
  const [rows, setRows] = useState<Instrument[]>();
  const [draft, setDraft] = useState<Draft>();
  const [removing, setRemoving] = useState<Instrument>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [saved, setSaved] = useState('');
  const load = useCallback(async () => setRows(await api<Instrument[]>('/market-reference-instruments')), []);
  useEffect(() => { void load().catch(e => setError((e as Error).message)); }, [load]);

  async function mutate(action: () => Promise<unknown>, message: string) {
    setBusy(true); setError(''); setSaved('');
    try { await action(); await load(); setDraft(undefined); setRemoving(undefined); setSaved(message); }
    catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }

  return <><Link className="back" to="/">← 我的追蹤</Link>
    <div className="page-heading"><div><span className="eyebrow">MARKET REFERENCE SETTINGS</span><h1>Reference Instruments</h1>
      <p className="muted">管理共用市場標的清單。各商品使用哪些標的，請到該商品的「市場參考標的」設定。</p></div></div>
    <section className="panel reference-instruments" aria-label="Reference Instruments 管理">
      <div className="section-heading"><h2>共用標的清單</h2><button disabled={busy} onClick={() => { setDraft({ symbol: '', name: '', market: 'TW' }); setRemoving(undefined); }}>新增 Instrument</button></div>
      <p className="small muted">請輸入完整 Yahoo ticker，例如 ^TWII。儲存不會查詢行情；修改標的會套用至所有使用它的商品。使用中的標的不能刪除。</p>
      {error && <ErrorState message={error} retry={() => { setError(''); void load().catch(e => setError((e as Error).message)); }} />}
      <p role="status">{saved}</p>
      {!rows ? !error && <LoadingState /> : rows.length === 0 ? <p className="muted">尚未建立 Reference Instrument。</p> :
        <ul className="reference-list">{rows.map(item => <li key={item.id}><div><strong>{item.symbol}</strong><p>{item.name} · {item.market}</p></div>
          <div className="reference-actions"><button disabled={busy} aria-label={`修改 ${item.symbol} Instrument`} onClick={() => { setDraft({ ...item }); setRemoving(undefined); }}>修改</button>
            <button disabled={busy} aria-label={`移除 ${item.symbol} Instrument`} onClick={() => { setRemoving(item); setDraft(undefined); }}>移除</button></div></li>)}</ul>}
      {draft && <form className="position-form" aria-label="Instrument 表單" onSubmit={event => {
        event.preventDefault();
        void mutate(() => api('/market-reference-instruments' + (draft.id ? `/${draft.id}` : ''), {
          method: draft.id ? 'PUT' : 'POST',
          body: JSON.stringify({ symbol: draft.symbol.trim(), name: draft.name.trim(), market: draft.market.trim() }),
        }), 'Reference Instrument 已儲存');
      }}>
        <label>Yahoo ticker<input aria-label="Yahoo ticker" required maxLength={64} pattern="[A-Z0-9^][A-Z0-9.^=_\-]*" value={draft.symbol} onChange={e => setDraft({ ...draft, symbol: e.target.value })} /></label>
        <label>名稱<input aria-label="Reference 名稱" required maxLength={200} value={draft.name} onChange={e => setDraft({ ...draft, name: e.target.value })} /></label>
        <label>市場<input aria-label="Reference 市場" required maxLength={32} value={draft.market} onChange={e => setDraft({ ...draft, market: e.target.value })} /></label>
        <button className="primary" disabled={busy}>儲存 Instrument</button><button type="button" disabled={busy} onClick={() => setDraft(undefined)}>取消</button>
      </form>}
      {removing && <div className="confirm-remove" role="alert"><span>確認移除 {removing.symbol} Instrument？歷史分析輸入會保留。</span>
        <button disabled={busy} onClick={() => void mutate(() => api(`/market-reference-instruments/${removing.id}`, { method: 'DELETE' }), 'Reference Instrument 已移除')}>確認移除 Reference</button>
        <button disabled={busy} onClick={() => setRemoving(undefined)}>取消移除</button></div>}
    </section></>;
}
