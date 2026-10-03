import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from './api';
import { ErrorState, LoadingState } from './components';
import type { Product } from './types';
import { marketText, referenceTypeText } from './displayText';

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
  const [removing, setRemoving] = useState<Mapping>();
  const load = useCallback(async () => {
    const [references, available] = await Promise.all([api<Mapping[]>(path + query), api<Instrument[]>('/market-reference-instruments')]);
    setRows(references); setInstruments(available);
  }, [path, query]);
  useEffect(() => { void load().catch(e => setError((e as Error).message)); }, [load]);

  async function mutate(action: () => Promise<unknown>, message: string) {
    setBusy(true); setError(''); setSaved('');
    try { await action(); await load(); setMapping(undefined); setRemoving(undefined); setSaved(message); }
    catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }

  return <section className="panel market-references" aria-label="市場參考標的">
    <div className="section-heading"><h2>市場參考標的</h2><button disabled={busy || !rows} onClick={() => {
      setMapping({ instrumentId: String(instruments[0]?.id ?? ''), referenceType: 'UNDERLYING' }); setRemoving(undefined);
    }}>新增此商品參考標的</button></div>
    <p className="small muted">參考標的僅作為分析證據；決策只針對 {product.symbol}。修改參考設定後，下一次 GPT／DeepSeek 分析會共同使用新設定，不會立即觸發 AI。</p>
    {error && <ErrorState message={error} retry={() => { setError(''); void load().catch(e => setError((e as Error).message)); }} />}
    <p role="status">{saved}</p>
    {!rows ? !error && <LoadingState /> : rows.length === 0 ? <p className="muted">尚未設定參考標的，分析將使用商品本身資料。</p> :
      <ul className="reference-list">{rows.map(row => <li key={row.id}><div><strong>{referenceTypeText(row.referenceType)}</strong><span className="small muted"> {meanings[row.referenceType]}</span>
        <p>{row.instrument.symbol} · {row.instrument.name} · {marketText(row.instrument.market)}</p></div>
        <div className="reference-actions"><button disabled={busy} aria-label={`修改 ${row.instrument.symbol} 參考設定`} onClick={() => { setMapping({ id: row.id, instrumentId: String(row.instrument.id), referenceType: row.referenceType }); setRemoving(undefined); }}>修改</button>
          <button disabled={busy} aria-label={`移除 ${row.instrument.symbol} 參考設定`} onClick={() => { setRemoving(row); setMapping(undefined); }}>移除</button></div></li>)}</ul>}
    {mapping && <form className="position-form" aria-label="商品參考設定表單" onSubmit={e => {
      e.preventDefault();
      void mutate(() => api(path + (mapping.id ? `/${mapping.id}` : '') + query, {
        method: mapping.id ? 'PUT' : 'POST',
        body: JSON.stringify({ instrumentId: Number(mapping.instrumentId), referenceType: mapping.referenceType }),
      }), '商品參考設定已儲存');
    }}>
      <label>參考類型<select aria-label="參考類型" value={mapping.referenceType} onChange={e => setMapping({ ...mapping, referenceType: e.target.value })}>{types.map(type => <option key={type} value={type}>{referenceTypeText(type)}</option>)}</select></label>
      <label>共用市場標的<select aria-label="共用市場標的" required value={mapping.instrumentId} onChange={e => setMapping({ ...mapping, instrumentId: e.target.value })}><option value="">選擇標的</option>{instruments.map(item => <option key={item.id} value={item.id}>{item.symbol} · {item.name} （{marketText(item.market)}）</option>)}</select></label>
      <button className="primary" disabled={busy || instruments.length === 0}>儲存參考設定</button><button type="button" disabled={busy} onClick={() => setMapping(undefined)}>取消</button>
      {instruments.length === 0 && <p className="small muted">請先到 <Link className="inline-link" to="/settings/reference-instruments">共用市場標的設定</Link> 建立共用標的。</p>}
    </form>}
    {removing && <div className="confirm-remove" role="alert"><span>確認移除 {removing.instrument.symbol} 的 參考設定？歷史分析輸入會保留。</span>
      <button disabled={busy} onClick={() => void mutate(() => api(path + `/${removing.id}` + query, { method: 'DELETE' }), '商品參考設定已移除')}>確認移除參考標的</button>
      <button disabled={busy} onClick={() => setRemoving(undefined)}>取消移除</button></div>}
  </section>;
}
