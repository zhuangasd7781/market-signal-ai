import { Link } from 'react-router-dom';
import type { AnalysisView, NewsEvidenceContext } from './types';

function NewsEvidenceDetails({ news }: { news?: NewsEvidenceContext | null }) {
  return <>{news?.newsContextId ? <>
    <Link to={`/news/${news.newsContextId}`}>NewsContext #{news.newsContextId}</Link>
    <span> · {({ FRESH: '最新', STALE: '過期', UNKNOWN: '時效未知' } as Record<string, string>)[news.freshness] ?? '時效未知'} · {news.events.length} 個事件</span>
    {news.status === 'PARTIAL' && <span> · 資料部分可用</span>}
    {news.generatedAt && <div className="small muted">更新時間 {new Date(news.generatedAt).toLocaleString('zh-TW', { timeZone: 'Asia/Taipei', hour12: false })}</div>}
  </> : <span className="muted">{news ? '無資料，未使用新聞證據' : '此分析未記錄新聞證據'}</span>}
    {news?.newsRefreshFailed && <p className="small">市場情報更新失敗；{news.newsContextId ? '本次使用先前情報。' : '本次未使用新聞證據。'}</p>}
  </>;
}

export function AnalysisNewsEvidence({ news }: { news?: NewsEvidenceContext | null }) {
  return <div className="analysis-news-evidence"><strong>市場情報：</strong><NewsEvidenceDetails news={news} /></div>;
}

// Summarize only the displayed reports, not today's latest news unrelated to those reports.
export function ProductNewsEvidence({ analyses }: { analyses: AnalysisView[] }) {
  const groups = new Map<string, { news: NewsEvidenceContext | null | undefined; providers: string[] }>();
  for (const analysis of analyses) {
    const news = analysis.context?.newsContext;
    const key = JSON.stringify([news?.newsContextId ?? null, news?.freshness, news?.status, news?.newsRefreshFailed]);
    const group = groups.get(key);
    if (group) group.providers.push(analysis.displayName);
    else groups.set(key, { news, providers: [analysis.displayName] });
  }
  if (!groups.size) return null;
  return <section className="analysis-news-evidence" aria-label="本次分析使用的市場情報"><strong>市場情報：</strong>
    {Array.from(groups.entries()).map(([key, group]) => <div className="analysis-news-group" key={key}>
      {groups.size > 1 && <div className="small muted">{group.providers.join('、')}</div>}
      <NewsEvidenceDetails news={group.news} />
    </div>)}
  </section>;
}
