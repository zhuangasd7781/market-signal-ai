import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { api } from './api';
import { ProviderLogo } from './ProviderLogo';
import type { Action, Product, Watchlist } from './types';

export function SignalBadge({ action, quantity, changed = false, reserveChangeSpace = false }: { action: Action; quantity: number | null; changed?: boolean; reserveChangeSpace?: boolean }) {
  const label = { ADD: '加碼', HOLD: '持有', REDUCE: '減碼', EXIT: '出場' }[action];
  return <span className="signal-wrap"><span className={`signal ${action.toLowerCase()}`}>{label}{quantity != null && quantity > 0 && (action === 'ADD' || action === 'REDUCE') ? ` ${quantity}` : ''}</span>{(changed || reserveChangeSpace) && <span className={`changed${changed ? '' : ' changed-placeholder'}`} title={changed ? '相較前次分析，動作或數量已改變' : undefined} aria-hidden={!changed}>已變更</span>}</span>;
}
function formatRelativeTime(value: string, now: number) {
  const minutes = Math.max(0, Math.floor((now - Date.parse(value)) / 60000));
  return minutes < 1 ? '剛剛' : minutes < 60 ? `${minutes} 分鐘前` : minutes < 1440 ? `${Math.floor(minutes / 60)} 小時前` : `${Math.floor(minutes / 1440)} 天前`;
}

export function RelativeTime({ value, providerTimes }: { value: string | null; providerTimes?: { name: string; analyzedAt: string | null }[] }) {
  const [now, setNow] = useState(Date.now());
  useEffect(() => { const timer = setInterval(() => setNow(Date.now()), 60000); return () => clearInterval(timer); }, []);
  if (!value) return <span className="muted">尚未分析</span>;
  const title = providerTimes
    ? providerTimes.map(({ name, analyzedAt }) => `${name}：${analyzedAt ? `${formatRelativeTime(analyzedAt, now)}（${new Date(analyzedAt).toLocaleString('zh-TW')}）` : '尚未分析'}`).join('\n')
    : new Date(value).toLocaleString('zh-TW');
  return <time dateTime={value} title={title}>{formatRelativeTime(value, now)}</time>;
}
export function ErrorState({ message, retry }: { message: string; retry?: () => void }) {
  return <div className="error" role="alert">{message}{retry && <button onClick={retry}>重試</button>}</div>;
}
export function LoadingState() { return <div className="loading" role="status" aria-label="載入中">{[1, 2, 3].map(n => <div className="skeleton" key={n} />)}</div>; }
export function ProductLink({ product, children }: { product: Product; children: ReactNode }) {
  return <Link to={`/products/${encodeURIComponent(product.symbol)}?market=${encodeURIComponent(product.market)}`}>{children}</Link>;
}
export function WatchlistTable({ data }: { data: Watchlist }) {
  return <div className="table-scroll"><table><thead><tr><th>追蹤商品</th>{data.providers.map(p => <th key={p.code}><span className="provider-heading"><ProviderLogo code={p.code} />{p.displayName}</span></th>)}<th>最後分析</th><th aria-label="查看詳情" /></tr></thead><tbody>
    {data.items.map(({ product, signals, lastAnalyzedAt }) => <tr key={product.id}><td><ProductLink product={product}><strong className="symbol">{product.symbol}</strong>{' '}<span className="product-name">{product.name}</span></ProductLink><span className="market">{product.market} · {product.assetType}{product.isLeveraged ? ' · 2×' : ''}</span></td>
      {data.providers.map(p => { const signal = signals.find(s => s.provider === p.code); return <td key={p.code}>{signal ? <span title={new Date(signal.analyzedAt).toLocaleString('zh-TW')}><SignalBadge {...signal} reserveChangeSpace /></span> : <span className="muted">尚未分析</span>}</td>; })}
      <td className="time"><RelativeTime value={lastAnalyzedAt} providerTimes={data.providers.map(p => ({ name: p.displayName, analyzedAt: signals.find(s => s.provider === p.code)?.analyzedAt ?? null }))} /></td><td><ProductLink product={product}><span className="row-arrow" aria-label={`查看 ${product.symbol} 詳情`}><svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="m9 5 7 7-7 7" /></svg></span></ProductLink></td></tr>)}
  </tbody></table></div>;
}

export function AddProductDialog({ trackedIds, close, added }: { trackedIds: number[]; close: () => void; added: () => Promise<void> }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [query, setQuery] = useState('');
  const [products, setProducts] = useState<Product[]>([]);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  useEffect(() => { dialog.current?.showModal(); }, []);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setError('');
    const timer = setTimeout(() => {
      api<Product[]>(`/products/search?q=${encodeURIComponent(query)}`, { signal: controller.signal })
        .then(setProducts).catch(e => { if (!controller.signal.aborted) setError(e.message); })
        .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    }, 180);
    return () => { clearTimeout(timer); controller.abort(); };
  }, [query]);
  async function add(product: Product) {
    setBusy(true); setError('');
    try { await api('/watchlist', { method: 'POST', body: JSON.stringify({ productId: product.id }) }); await added(); close(); }
    catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }
  return <dialog ref={dialog} onCancel={event => { if (busy) event.preventDefault(); else close(); }} aria-labelledby="add-title"><div className="dialog-header"><h2 id="add-title">加入追蹤商品</h2><button className="icon-button" aria-label="關閉" disabled={busy} onClick={close}>×</button></div>
    <p className="muted">追蹤你關注的商品，無須先設定持倉。</p><input autoFocus aria-label="搜尋商品代號或名稱" maxLength={100} placeholder="輸入商品代號或名稱…" value={query} onChange={e => setQuery(e.target.value)} />
    {error && <ErrorState message={error} />}{loading ? <LoadingState /> : <div className="search-results">{products.length === 0 && <p>找不到符合的商品。</p>}{products.map(p => <div className="search-result" key={p.id}><div><strong>{p.symbol}</strong><span>{p.name} <small>{p.market}</small></span></div><button disabled={busy || trackedIds.includes(p.id)} onClick={() => void add(p)}>{trackedIds.includes(p.id) ? '已追蹤' : '加入'}</button></div>)}</div>}
  </dialog>;
}
