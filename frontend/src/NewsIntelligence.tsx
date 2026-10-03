import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api, apiOptional } from './api';
import { ErrorState, LoadingState } from './components';

export interface NewsContext {
  id: number; windowStart: string; windowEnd: string; generatedAt: string; status: string;
  searchProvider: string; configuredModel: string; collectorModel: string; eventCount: number;
  searchResultCount: number; filteredResultCount: number; unknownTimeCount: number; collectorInputCount: number;
  usage: { inputTokens: number; outputTokens: number; cachedTokens: number | null } | null;
  promptVersion: string;
  coverage: { publisher: string; topic: string; status: string; resultCount: number; limitation: string | null }[];
  events: { id: string; category: string; title: string; summary: string; eventTime: string | null;
    publishedAt: string; direction: string; importance: string; relevance: number; confidence: number;
    timeQuality: string; timeQualityReason?: string | null; sources: { resultId: string; publisher: string; url: string; publishedAt: string; sourceType: string }[] }[];
}
const time = (value: string) => new Date(value).toLocaleString('zh-TW', { timeZone: 'Asia/Taipei', hour12: false });
const category: Record<string,string> = { MACRO: '總體經濟／利率', TECH: '科技／半導體', TAIWAN: '台灣市場' };
const importance: Record<string,string> = { HIGH: '高重要性', MEDIUM: '中重要性', LOW: '低重要性' };
const direction: Record<string,[string,string]> = { POSITIVE: ['正向證據','positive'], NEGATIVE: ['負向證據','negative'], NEUTRAL: ['中性證據','neutral'], MIXED: ['多空並存','warning'], UNKNOWN: ['方向未明','unknown'] };
function sourceLink(value: string) { try { const u = new URL(value); return ['https:', 'http:'].includes(u.protocol) && !u.username && !u.password ? u.href : undefined; } catch { return undefined; } }

export function NewsIntelligence() {
  const { contextId } = useParams(); const navigate = useNavigate();
  const [data, setData] = useState<NewsContext | null>(null); const [loading, setLoading] = useState(true);
  const [page, setPage] = useState({ contextId: 0, count: 10 }); const sentinel = useRef<HTMLDivElement>(null);
  const visibleCount = data && page.contextId === data.id ? page.count : 10;
  const [busy, setBusy] = useState(false); const [error, setError] = useState(''); const refreshing = useRef(false);
  useEffect(() => { let alive = true; setLoading(true); setError('');
    apiOptional<NewsContext>(contextId ? `/news-context/${contextId}` : '/news-context/latest')
      .then(value => { if (alive) setData(value); }).catch(e => { if (alive) setError((e as Error).message); })
      .finally(() => { if (alive) setLoading(false); }); return () => { alive = false; };
  }, [contextId]);
  useEffect(() => {
    const target = sentinel.current;
    if (!target || !data || visibleCount >= data.events.length) return;
    const observer = new IntersectionObserver(entries => {
      if (entries.some(entry => entry.isIntersecting)) {
        observer.disconnect();
        setPage({ contextId: data.id, count: Math.min(visibleCount + 10, data.events.length) });
      }
    }, { threshold: 0.1 });
    observer.observe(target); return () => observer.disconnect();
  }, [data, visibleCount]);
  async function refresh() {
    if (refreshing.current) return; refreshing.current = true; setBusy(true); setError('');
    try { const next = await api<NewsContext>('/news-context/refresh', { method: 'POST' }); setData(next); if (contextId) navigate('/news'); }
    catch (e) { setError((e as Error).message); } finally { refreshing.current = false; setBusy(false); }
  }
  return <><div className="page-heading"><div><span className="eyebrow">全球金融新聞</span><h1>市場情報</h1><p className="muted">事件摘要與來源證據，供後續分析參考；本階段不產生交易指令。</p></div>
    <button className="primary" disabled={busy || loading} onClick={() => void refresh()}>{busy ? '正在搜尋與整理…' : '立即更新市場情報'}</button></div>
    {error && <ErrorState message={error} />}
    {busy && <p role="status" className="muted">重新取得最新新聞並交由 DeepSeek Flash 整理，請稍候。前一份情報會保留。</p>}
    {loading ? <LoadingState /> : !data ? <section className="panel empty"><h2>{contextId ? "找不到這筆市場情報" : "尚未建立市場情報"}</h2><p>立即更新，搜尋執行當下往前 72 小時的新聞。</p>{contextId && <Link to="/news">查看最新情報</Link>}</section> : <>
      <section className="panel news-overview"><dl className="news-meta"><div><dt>最近更新（台北時間）</dt><dd>{time(data.generatedAt)}</dd></div><div><dt>情報範圍</dt><dd>生成當下往前 72 小時</dd></div><div><dt>事件數</dt><dd>{data.events.length}</dd></div><div><dt>資料狀態</dt><dd>{data.status === 'PARTIAL' ? '部分涵蓋' : data.status === 'EMPTY' ? '沒有合適事件' : '可用'}</dd></div></dl>
        <p className="small muted">{time(data.windowStart)} ～ {time(data.windowEnd)}（台北時間）</p><p className="small muted">情報 #{data.id} · {data.collectorModel} · <Link to={`/news/${data.id}`}>這份情報的固定連結</Link>{contextId && <> · <Link to="/news">查看最新情報</Link></>}</p>
        {data.status === 'PARTIAL' && <p className="news-limitation">RSS 僅涵蓋設定來源的近期文章，並非完整網路搜尋；時間不明、來源失敗與篇數限制均可能影響涵蓋程度。</p>}
        <details><summary>資料來源與處理紀錄</summary><p>取得 {data.searchResultCount} 篇 · 窗口內去重後 {data.filteredResultCount} 篇 · 送入模型 {data.collectorInputCount} 篇 · 發布時間不明 {data.unknownTimeCount} 篇（未送入模型）</p>
          <ul className="news-coverage">{data.coverage.map((s, i) => <li key={i}>{s.publisher} · {category[s.topic] ?? '其他'} · {s.status === 'AVAILABLE' ? '可用' : '無法取得'} · {s.resultCount} 篇{s.status !== 'AVAILABLE' && s.limitation?.match(/HTTP \d{3}/) && `（${s.limitation.match(/HTTP \d{3}/)?.[0]}）`}</li>)}</ul>
          <p>Prompt：{data.promptVersion} · 設定 Model：{data.configuredModel}</p>{data.usage && <p>Token：輸入 {data.usage.inputTokens.toLocaleString('zh-TW')} · 輸出 {data.usage.outputTokens.toLocaleString('zh-TW')} · 快取 {data.usage.cachedTokens?.toLocaleString('zh-TW') ?? '未提供'}</p>}
        </details>
      </section>
      <section className="news-events" aria-label="市場事件">{data.events.length === 0 ? <div className="panel empty"><h2>此窗口沒有合適的市場事件</h2><p>不會以假新聞或舊事件補足數量。</p></div> : data.events.slice(0, visibleCount).map(e => {
        const [label, tone] = direction[e.direction] ?? direction.UNKNOWN;
        return <article key={e.id} className="panel news-event"><div className="news-tags"><span className={`analysis-status analysis-status--${e.importance === 'HIGH' ? 'warning' : 'neutral'}`}>{importance[e.importance] ?? '重要性未明'}</span><span className="small muted">{category[e.category] ?? '其他市場事件'}</span><span className={`analysis-status analysis-status--${tone}`}>{label}</span></div>
          <h2>{e.title}</h2><p className="news-summary">{e.summary}</p>
          <dl className="news-meta"><div><dt>事件時間</dt><dd>{e.eventTime ? time(e.eventTime) : '無法確認'}</dd></div><div><dt>最近來源發布時間</dt><dd>{time(e.publishedAt)}</dd></div><div><dt>相關程度／整理信心值</dt><dd>{e.relevance}% ／ {e.confidence}%</dd></div></dl>
          {e.timeQuality === 'RECAP' && <p className="news-limitation">回顧事件：發生時間早於這次窗口，不應當作新的市場催化因素。</p>}
          {e.timeQualityReason && <p className="news-limitation">模型提供的事件時間未能由來源原文確認，已標示為未知。</p>}
          {e.timeQuality === 'UNKNOWN' && <p className="small muted">來源未提供足以確認事件時間的資訊；發布時間不等於事件發生時間。</p>}
          <h3>原始來源</h3><ul className="news-sources">{e.sources.map(s => <li key={s.resultId}><a href={sourceLink(s.url)} target="_blank" rel="noopener noreferrer">{s.publisher} ↗</a><span className="small muted">{time(s.publishedAt)}</span></li>)}</ul>
        </article>;
      })}</section>
      {visibleCount < data.events.length ? <div ref={sentinel} className="news-load-more" role="status">已顯示 {visibleCount}／{data.events.length} 筆，捲到底部載入更多</div> : data.events.length > 0 && <p className="news-load-more muted">已顯示全部 {data.events.length} 筆事件</p>}
    </>}
  </>;
}
