import { useCallback, useEffect, useState } from 'react';
import { api } from './api';
import { ErrorState, LoadingState } from './components';
import type { Product } from './types';
type Instrument = { id: number; symbol: string; name: string; market: string };
type Mapping = { id: number; referenceType: string; instrument: Instrument };
const types = ['UNDERLYING', 'BROAD_MARKET', 'SECTOR'];
const meanings: Record<string, string> = { UNDERLYING: '主要追蹤標的', BROAD_MARKET: '整體市場背景', SECTOR: '產業背景' };
export function MarketReferences({ product }: { product: Product }) {
  const path = `/products/${encodeURIComponent(product.symbol)}/references`;
  const query = `?market=${encodeURIComponent(product.market)}`;
  const [rows, setRows] = useState<Mapping[]>();
  const [instruments, setInstruments] = useState<Instrument[]>([]);
  const [error, setError] = useState('');
  const [saved, setSaved] = useState('');
  const [busy, setBusy] = useState(false);
  const [mapping, setMapping] = useState<{ id?: number; instrumentId: string; referenceType: string }>();
  const [showInstruments, setShowInstruments] = useState(false);
  const [instrument, setInstrument] = useState<{ id?: number; symbol: string; name: string; market: string }>();
  const [removing, setRemoving] = useState<{ kind: 'mapping' | 'instrument'; id: number; name: string }>();
  const load = useCallback(async () => {
    const [references, available] = await Promise.all([api<Mapping[]>(path + query), api<Instrument[]>('/market-reference-instruments')]);
    setRows(references); setInstruments(available);
  }, [path, query]);
  useEffect(() => { void load().catch(e => setError(e.message)); }, [load]);
  async function mutate(action: () => Promise<unknown>, message: string) {
    setBusy(true); setError(''); setSaved('');
    try { await action(); await load(); setMapping(undefined); setInstrument(undefined); setRemoving(undefined); setSaved(message); }
    catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }
  return <section className="panel market-references" aria-label="市場參考標的">
    <div className="section-heading"><h2>市場參考標的</h2><button disabled={busy || !rows} onClick={() => { setMapping({ instrumentId: String(instruments[0]?.id ?? ''), referenceType: 'UNDERLYING' }); setRemoving(undefined); }}>新增 Reference</button></div>
    <p className="small muted">Reference 僅作為分析證據；Decision 只針對 {product.symbol}。修改後下一次分析會讓 GPT／DeepSeek 共同使用新 Mapping，不會立即觸發 AI。</p>
    {error && <ErrorState message={error} retry={() => { setError(''); void load().catch(e => setError(e.message)); }} />}<p role="status">{saved}</p>
    {!rows ? !error && <LoadingState /> : rows.length === 0 ? <p className="muted">尚未設定 Reference，分析將使用商品本身資料。</p> : <ul className="reference-list">{rows.map(row => <li key={row.id}><div><strong>{row.referenceType}</strong><span className="small muted"> {meanings[row.referenceType]}</span><p>{row.instrument.symbol} · {row.instrument.name} · {row.instrument.market}</p></div><div className="reference-actions"><button disabled={busy} aria-label={`修改 ${row.instrument.symbol} Mapping`} onClick={() => setMapping({ id: row.id, instrumentId: String(row.instrument.id), referenceType: row.referenceType })}>修改</button><button disabled={busy} aria-label={`移除 ${row.instrument.symbol} Mapping`} onClick={() => setRemoving({ kind: 'mapping', id: row.id, name: row.instrument.symbol })}>移除</button></div></li>)}</ul>}
    {mapping && <form className="position-form" aria-label="Reference Mapping 表單" onSubmit={e => { e.preventDefault(); void mutate(() => api(path + (mapping.id ? `/${mapping.id}` : '') + query, { method: mapping.id ? 'PUT' : 'POST', body: JSON.stringify({ instrumentId: Number(mapping.instrumentId), referenceType: mapping.referenceType }) }), 'Reference Mapping 已儲存'); }}>
      <label>Reference Type<select aria-label="Reference Type" value={mapping.referenceType} onChange={e => setMapping({ ...mapping, referenceType: e.target.value })}>{types.map(type => <option key={type} value={type}>{type}</option>)}</select></label>
      <label>Reference Instrument<select aria-label="Reference Instrument" required value={mapping.instrumentId} onChange={e => setMapping({ ...mapping, instrumentId: e.target.value })}><option value="">選擇標的</option>{instruments.map(item => <option key={item.id} value={item.id}>{item.symbol} · {item.name} ({item.market})</option>)}</select></label>
      <button className="primary" disabled={busy || instruments.length === 0}>儲存 Mapping</button><button type="button" disabled={busy} onClick={() => setMapping(undefined)}>取消</button>
      {instruments.length === 0 && <p className="small muted">請先在下方新增 Reference Instrument。</p>}
    </form>}
    <button className="reference-manager-toggle" aria-expanded={showInstruments} onClick={() => setShowInstruments(!showInstruments)}>管理 Reference Instruments</button>
    {showInstruments && <div aria-label="Reference Instruments 管理"><p className="small muted">請輸入完整 Yahoo ticker，例如 ^TWII。儲存不會查詢行情；標的修改會套用至所有使用它的 Mapping。使用中的標的不能刪除。</p><button disabled={busy} onClick={() => setInstrument({ symbol: '', name: '', market: product.market })}>新增 Instrument</button>
      <ul className="reference-list">{instruments.map(item => <li key={item.id}><div><strong>{item.symbol}</strong><p>{item.name} · {item.market}</p></div><div className="reference-actions"><button disabled={busy} aria-label={`修改 ${item.symbol} Instrument`} onClick={() => setInstrument({ ...item })}>修改</button><button disabled={busy} aria-label={`移除 ${item.symbol} Instrument`} onClick={() => setRemoving({ kind: 'instrument', id: item.id, name: item.symbol })}>移除</button></div></li>)}</ul>
      {instrument && <form className="position-form" aria-label="Instrument 表單" onSubmit={e => { e.preventDefault(); void mutate(() => api('/market-reference-instruments' + (instrument.id ? `/${instrument.id}` : ''), { method: instrument.id ? 'PUT' : 'POST', body: JSON.stringify({ symbol: instrument.symbol.trim(), name: instrument.name.trim(), market: instrument.market.trim() }) }), 'Reference Instrument 已儲存'); }}>
        <label>Yahoo ticker<input aria-label="Yahoo ticker" required maxLength={64} pattern="[A-Z0-9^][A-Z0-9.^=_\-]*" value={instrument.symbol} onChange={e => setInstrument({ ...instrument, symbol: e.target.value })} /></label><label>名稱<input aria-label="Reference 名稱" required maxLength={200} value={instrument.name} onChange={e => setInstrument({ ...instrument, name: e.target.value })} /></label><label>市場<input aria-label="Reference 市場" required maxLength={32} value={instrument.market} onChange={e => setInstrument({ ...instrument, market: e.target.value })} /></label><button className="primary" disabled={busy}>儲存 Instrument</button><button type="button" disabled={busy} onClick={() => setInstrument(undefined)}>取消</button>
      </form>}
    </div>}
    {removing && <div className="confirm-remove" role="alert"><span>確認移除 {removing.name} 的 {removing.kind === 'mapping' ? 'Mapping' : 'Instrument'}？歷史分析輸入會保留。</span><button disabled={busy} onClick={() => void mutate(() => api(removing.kind === 'mapping' ? path + `/${removing.id}` + query : `/market-reference-instruments/${removing.id}`, { method: 'DELETE' }), 'Reference 已移除')}>確認移除 Reference</button><button disabled={busy} onClick={() => setRemoving(undefined)}>取消移除</button></div>}
  </section>;
}
