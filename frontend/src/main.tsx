import { StrictMode, useCallback, useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter, Link, Navigate, Outlet, Route, Routes, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { api, apiOptional } from './api';
import { ProviderLogo } from './ProviderLogo';
import { AddProductDialog, ErrorState, LoadingState, RelativeTime, SignalBadge, WatchlistTable } from './components';
import type { AnalysisView, MarketQuote, Position, Product, Provider, Watchlist } from './types';
import './styles.css';
import './theme.css';
import { ThemeToggle } from './ThemeToggle';
import { AISettings } from './AISettings';
import { AnalysisScheduleSettings } from './AnalysisScheduleSettings';
import { PromptSettings } from './PromptSettings';

function AppShell() {
  const navigate = useNavigate();
  if (sessionStorage.getItem('demo-session') !== 'yes') return <Navigate to="/login" replace />;
  return <><header className="topbar"><Link className="brand" to="/"><span className="brand-mark">m<span>∕</span></span>Market Signal <span className="brand-ai">AI</span></Link><nav><Link className="nav-link" to="/">我的追蹤</Link><Link className="nav-link" to="/settings/ai">AI 設定</Link><Link className="nav-link" to="/settings/schedule">分析排程</Link><Link className="nav-link" to="/settings/prompts">Prompt 設定</Link></nav><div className="account"><ThemeToggle /><span className="avatar">D</span><span className="account-name">Demo 投資人</span><button className="text-button" onClick={() => { sessionStorage.removeItem('demo-session'); navigate('/login'); }}>離開示範</button></div></header><main><Outlet /></main><footer><Link to="/settings/prompts">Prompt 設定</Link><span>MARKET SIGNAL AI</span><Link to="/settings/schedule">分析排程</Link><Link to="/settings/ai">AI 設定</Link><span>獨立觀點，自主判斷。</span></footer></>;
}
function Login() {
  const navigate = useNavigate();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  async function enter() {
    setBusy(true); setError('');
    try { await api('/me'); sessionStorage.setItem('demo-session', 'yes'); navigate('/'); }
    catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }
  return <div className="login"><div className="login-theme"><ThemeToggle /></div><span className="eyebrow">MARKET SIGNAL AI</span><h1>多個觀點。<br /><span className="muted">一眼掌握。</span></h1><p>追蹤你關注的投資商品，<br />並排檢視各 AI 分析師的獨立訊號。</p><button className="primary" disabled={busy} onClick={() => void enter()}>{busy ? '連線中…' : '進入示範看板 →'}</button>{error && <ErrorState message={error} />}<p className="login-note">Phase 1 · 示範模式<br />Google 登入將於下一階段提供。<br />此模式共用示範帳戶，請勿輸入私人資料。</p></div>;
}
function Home() {
  const [data, setData] = useState<Watchlist>();
  const [error, setError] = useState('');
  const [query, setQuery] = useState('');
  const [adding, setAdding] = useState(false);
  const load = useCallback(async () => { setError(''); try { setData(await api<Watchlist>('/watchlist')); } catch (e) { setError((e as Error).message); } }, []);
  useEffect(() => { void load(); }, [load]);
  const filtered = data?.items.filter(({ product: p }) => `${p.symbol} ${p.name}`.toLowerCase().includes(query.trim().toLowerCase())) ?? [];
  return <><div className="page-heading"><div><span className="eyebrow">YOUR SIGNAL BOARD</span><h1>我的追蹤 <span className="count">{data?.items.length ?? '—'}</span></h1><p className="muted">每個 AI 獨立分析，讓不同觀點並排呈現。</p></div><button className="primary" onClick={() => setAdding(true)}>＋ 加入商品</button></div>
    <section className="board" aria-label="AI 訊號看板"><div className="board-toolbar"><label className="search-box"><span aria-hidden="true">⌕</span><input aria-label="搜尋追蹤商品" placeholder="搜尋追蹤商品…" value={query} onChange={e => setQuery(e.target.value)} /></label><span className="toolbar-note"><span className="status-dot" />獨立 AI 訊號</span></div>
      {error ? <ErrorState message={error} retry={() => void load()} /> : !data ? <LoadingState /> : data.items.length === 0 ? <div className="empty"><span className="empty-icon">＋</span><h2>目前沒有追蹤商品</h2><p>加入你想讓 AI 分析的股票、ETF 或其他投資商品。</p><button className="primary" onClick={() => setAdding(true)}>＋ 加入第一個商品</button></div> : filtered.length === 0 ? <div className="empty"><h2>沒有符合的追蹤商品</h2><button onClick={() => setQuery('')}>清除搜尋</button></div> : <WatchlistTable data={{ ...data, items: filtered }} />}
      <div className="board-footer"><span>各 AI 訊號皆為獨立觀點，不代表共識。</span><span>時間顯示各商品最近一次分析</span></div></section>
    <div className="legend"><span>訊號說明</span><span><i className="legend-dot add-dot" />加碼</span><span><i className="legend-dot hold-dot" />持有</span><span><i className="legend-dot reduce-dot" />減碼</span><span><i className="legend-dot exit-dot" />出場</span></div>
    {adding && <AddProductDialog trackedIds={data?.items.map(x => x.product.id) ?? []} close={() => setAdding(false)} added={load} />}</>;
}
function PositionSection({ product }: { product: Product }) {
  const [position, setPosition] = useState<Position | null>(null);
  const [quote, setQuote] = useState<MarketQuote | null>(null);
  const [quoteError, setQuoteError] = useState(false);
  const [loading, setLoading] = useState(true);
  const [editing, setEditing] = useState(false);
  const [quantity, setQuantity] = useState('');
  const [cost, setCost] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const path = `/products/${encodeURIComponent(product.symbol)}/position?market=${encodeURIComponent(product.market)}`;
  const load = useCallback(async () => {
    setLoading(true); setError('');
    try { const result = await api<{ position: Position | null }>(path); setPosition(result.position); }
    catch (e) { setError((e as Error).message); }
    finally { setLoading(false); }
  }, [path]);
  useEffect(() => { void load(); }, [load]);
  useEffect(() => {
    setQuote(null); setQuoteError(false);
    void apiOptional<MarketQuote>(`/products/${encodeURIComponent(product.symbol)}/market?market=${encodeURIComponent(product.market)}`)
      .then(setQuote).catch(() => setQuoteError(true));
  }, [product.symbol, product.market]);
  async function save(event: React.FormEvent) {
    event.preventDefault(); setBusy(true); setError('');
    try {
      await api(path, { method: 'PUT', body: JSON.stringify({ quantity: Number(quantity), averageCost: Number(cost) }) });
      setEditing(false); await load();
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }
  return <section className="panel"><div className="section-heading"><h2>我的持倉</h2><button onClick={() => { setQuantity(String(position?.quantity ?? '')); setCost(String(position?.averageCost ?? '')); setEditing(true); }} disabled={loading}> {position ? '編輯持倉' : '設定持倉'}</button></div>
    {error && <ErrorState message={error} retry={() => void load()} />}{loading ? <LoadingState /> : editing ? <form onSubmit={save} className="position-form"><label>持有數量（{product.quantityUnit}）<input type="number" min="0" max="999999999999" step="0.000001" required value={quantity} onChange={e => setQuantity(e.target.value)} /></label><label>平均成本（每股／單位）<input type="number" min="0" max="999999999999" step="0.000001" required value={cost} onChange={e => setCost(e.target.value)} /></label><button className="primary" disabled={busy}>儲存</button><button type="button" disabled={busy} onClick={() => setEditing(false)}>取消</button></form> : position ? <div className="position-grid"><div><span>持有數量</span><strong>{position.quantity} <small>{product.quantityUnit}</small></strong></div><div><span>平均成本</span><strong>{position.averageCost.toLocaleString()}</strong></div><div><span>目前損益</span><strong>—</strong></div><div><span>報酬率</span><strong>—</strong></div></div> : <p className="muted">尚未設定持倉。追蹤商品不代表持有。</p>}
    <p className="small muted">{quote ? `最近報價 ${quote.price.toLocaleString()} 元 · 行情時間 ${new Date(quote.marketTime).toLocaleString('zh-TW', { timeZone: 'Asia/Taipei' })}（可能延遲）；損益與報酬率尚未計算。` : quoteError ? '目前無法讀取行情。' : '尚無市場報價，因此不計算損益與報酬率。'}{product.quantityUnit === '張' && ` 1 張 = ${product.unitSize.toLocaleString()} 股／單位。`}</p></section>;
}
function AnalysisCard({ analysis }: { analysis: AnalysisView }) {
  const a = analysis.result;
  return <article className="analysis-card"><div className="section-heading"><div><h3 className="provider-heading"><ProviderLogo code={analysis.provider} />{analysis.displayName}</h3><span className="small muted">{analysis.model} · <RelativeTime value={analysis.createdAt} /></span></div><SignalBadge action={a.action} quantity={a.quantity} /></div><div className="confidence">{analysis.model.startsWith('mock') ? '示範信心值' : '分析信心值'} <strong>{a.confidence}%</strong></div><h4>Root Event <span className="tag">{a.rootEvent.status}</span></h4><p>{a.rootEvent.summary}</p><dl className="context-grid">{[['市場狀態', a.marketRegime], ['趨勢', a.trend], ['動能', a.momentum], ['量價', a.volume], ['風險報酬', a.riskReward]].map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value === 'UNKNOWN' ? '資料不足' : value}</dd></div>)}</dl>
      {([['主要原因', a.reasons], ['風險', a.risks], ['Bull Case · 多方觀點', a.bullCase], ['Bear Case · 空方觀點', a.bearCase]] as const).map(([title, items]) => <div key={title}><h4>{title}</h4><ul>{items.map(item => <li key={item}>{item}</li>)}</ul></div>)}<div className="invalidation"><h4>什麼情況會使分析失效？</h4><p>{a.invalidation}</p></div><h4>下一步</h4>{a.nextActions.map((next, i) => <div className="next-action" key={i}><p>{next.condition}</p><SignalBadge action={next.action} quantity={next.quantity} /></div>)}</article>;
}
function ProductDetail() {
  const { symbol = '' } = useParams();
  const [params] = useSearchParams();
  const market = params.get('market') ?? 'TW';
  const navigate = useNavigate();
  const [product, setProduct] = useState<Product>();
  const [providers, setProviders] = useState<Provider[]>([]);
  const [analyses, setAnalyses] = useState<AnalysisView[]>([]);
  const [error, setError] = useState('');
  const [analysisError, setAnalysisError] = useState('');
  const [history, setHistory] = useState<AnalysisView[] | null>(null);
  const [showHistory, setShowHistory] = useState(false);
  const [busy, setBusy] = useState(false);
  const [tracked, setTracked] = useState(false);
  const [confirmRemove, setConfirmRemove] = useState(false);
  const base = `/products/${encodeURIComponent(symbol)}`;
  const query = `?market=${encodeURIComponent(market)}`;
  const load = useCallback(async () => {
    setError(''); setAnalysisError('');
    try {
      const [p, ps, watchlist] = await Promise.all([api<Product>(base + query), api<Provider[]>('/ai/providers'), api<Watchlist>('/watchlist')]);
      setProduct(p); setProviders(ps); setTracked(watchlist.items.some(x => x.product.id === p.id));
      try { setAnalyses(await api<AnalysisView[]>(base + '/analysis' + query)); } catch (e) { setAnalysisError((e as Error).message); }
    } catch (e) { setError((e as Error).message); }
  }, [base, query]);
  useEffect(() => { setProduct(undefined); setAnalyses([]); setHistory(null); setShowHistory(false); setConfirmRemove(false); void load(); }, [load]);
  async function changeWatch() {
    if (!product) return;
    setBusy(true); setError('');
    try {
      await api(tracked ? `/watchlist/${product.id}` : '/watchlist', { method: tracked ? 'DELETE' : 'POST', ...(tracked ? {} : { body: JSON.stringify({ productId: product.id }) }) });
      if (tracked) navigate('/'); else setTracked(true);
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }
  async function toggleHistory() {
    if (showHistory) { setShowHistory(false); return; }
    setBusy(true);
    try { setHistory(await api<AnalysisView[]>(base + '/analysis/history' + query)); setShowHistory(true); }
    catch (e) { setAnalysisError((e as Error).message); }
    finally { setBusy(false); }
  }
  return <><Link className="back" to="/">← 返回我的追蹤</Link>{error && <ErrorState message={error} retry={() => void load()} />}{!product ? !error && <LoadingState /> : <><div className="page-heading detail-heading"><div><span className="eyebrow">{product.market} / {product.assetType}{product.isLeveraged && ' / LEVERAGED'}</span><h1>{product.symbol}</h1><p>{product.name}</p></div><div>{confirmRemove ? <div className="confirm-remove"><span>移除追蹤？持倉與歷史會保留。</span><button disabled={busy} onClick={() => void changeWatch()}>確認移除</button><button onClick={() => setConfirmRemove(false)}>取消</button></div> : <button disabled={busy} onClick={() => tracked ? setConfirmRemove(true) : void changeWatch()}>{tracked ? '移除追蹤' : '＋ 加入追蹤'}</button>}</div></div>
      <PositionSection key={product.id} product={product} /><div className="section-heading analysis-heading"><div><h2>AI 獨立觀點</h2><p className="muted small">各自分析，保留分歧。請留意模型與分析時間。</p></div><button disabled={busy} onClick={() => void toggleHistory()}>{showHistory ? '收起歷史' : '分析歷史'}</button></div>
      {analysisError && <ErrorState message={analysisError} retry={() => void load()} />}{showHistory && <section className="panel"><h3>分析歷史（最近 100 筆）</h3>{history?.length === 0 && <p className="muted">尚無分析紀錄。</p>}{history?.map(a => <div className="history-row" key={a.id}><span>{a.displayName}</span><SignalBadge action={a.result.action} quantity={a.result.quantity} /><time>{new Date(a.createdAt).toLocaleString('zh-TW')}</time></div>)}</section>}
      <div className="analysis-grid">{providers.map(p => { const a = analyses.find(x => x.provider === p.code); return a ? <AnalysisCard key={p.code} analysis={a} /> : <article className="analysis-card" key={p.code}><h3 className="provider-heading"><ProviderLogo code={p.code} />{p.displayName}</h3><p className="muted">{analysisError ? '暫時無法載入分析。' : '尚未分析。'}</p></article>; })}</div></>}</>;
}

createRoot(document.getElementById('root')!).render(<StrictMode><BrowserRouter><Routes><Route path="/login" element={<Login />} /><Route element={<AppShell />}><Route path="/" element={<Home />} /><Route path="/settings/ai" element={<AISettings />} /><Route path="/settings/schedule" element={<AnalysisScheduleSettings />} /><Route path="/settings/prompts" element={<PromptSettings />} /><Route path="/products/:symbol" element={<ProductDetail />} /><Route path="*" element={<div className="empty"><h1>找不到此頁面</h1><Link to="/">返回我的追蹤</Link></div>} /></Route></Routes></BrowserRouter></StrictMode>);
