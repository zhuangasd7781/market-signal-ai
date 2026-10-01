import { Fragment, useRef, useState } from 'react';
import { SignalBadge } from './components';
import { ProviderLogo } from './ProviderLogo';
import type { AnalysisView } from './types';

const number = (value: number | null | undefined) => value == null ? '—' : value.toLocaleString('zh-TW', { maximumFractionDigits: 4 });
const time = (value: string) => new Date(value).toLocaleString('zh-TW', { timeZone: 'Asia/Taipei', hour12: false });

function HistoryDetail({ analysis, quantityUnit, collapse }: { analysis: AnalysisView; quantityUnit: string; collapse: () => void }) {
  const a = analysis.result;
  const c = analysis.context;
  return <div className="history-detail" id={`history-detail-${analysis.id}`}><button aria-label={`收起詳細分析 ${analysis.id}`} onClick={collapse}>收起詳細</button>
    <p className="small muted">分析時間 {time(analysis.createdAt)}（台北） · 實際模型 {analysis.model} · Prompt {analysis.promptVersion ?? '未記錄'}</p>
    <div className="history-detail-grid"><div>
      <h4>Root Event · {a.rootEvent.status}</h4><p>{a.rootEvent.summary}</p>
      <dl className="context-grid">{[['市場狀態', a.marketRegime], ['趨勢', a.trend], ['動能', a.momentum], ['量價', a.volume], ['風險報酬', a.riskReward]].map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value === 'UNKNOWN' ? '資料不足' : value}</dd></div>)}</dl>
      {([['主要原因', a.reasons], ['風險', a.risks], ['多方觀點', a.bullCase], ['空方觀點', a.bearCase]] as const).map(([title, items]) => <div key={title}><h4>{title}</h4><ul>{items.map((item, index) => <li key={index}>{item}</li>)}</ul></div>)}
      <h4>分析失效條件</h4><p>{a.invalidation}</p><h4>下一步條件與行動</h4>{a.nextActions.map((next, index) => <div className="next-action" key={index}><p>{next.condition}</p><SignalBadge action={next.action} quantity={next.quantity} /></div>)}
    </div><div>
      <h4>當時輸入</h4>{c ? <><p>商品價格 {number(c.targetPrice)}</p><p>{c.position ? `持倉 ${number(c.position.quantity)} ${quantityUnit} · 平均成本 ${number(c.position.averageCost)}` : '未設定持倉'}</p><p>設定模型 {c.configuredModel ?? '未記錄'}</p><p>Skills：{c.skillIdentifiers?.length ? c.skillIdentifiers.map(id => id.includes(':') ? `${id.split(':')[0]} · ${id.split(':')[1].slice(0, 12)}` : id).join('、') : '未記錄'}</p></> : <p className="muted">此歷史紀錄未提供輸入摘要。</p>}
      <h4>市場參考資料</h4>{c?.marketReferences?.length ? c.marketReferences.map(ref => <article className="history-reference" key={ref.mappingId}>
        <strong>{ref.referenceType} · {ref.name}（{ref.symbol}）</strong><p>狀態 {ref.status} · 數值 {number(ref.currentValue)}</p><p>漲跌 {number(ref.change)} · {number(ref.changePercent)}{ref.changePercent == null ? '' : '%'}</p>
        <p>歷史日價 {ref.history.length} 筆{ref.history.length > 0 && ` · ${ref.history[0].tradeDate} 至 ${ref.history[ref.history.length - 1].tradeDate}`}</p>
        {ref.historyMetadata && <p>歷史來源 {ref.historyMetadata.source} / {ref.historyMetadata.sourceSymbol} · 品質 {ref.historyMetadata.dataQuality}{ref.historyMetadata.isFallback && ' · 替代來源'}</p>}
        {ref.historyMetadata?.reason && <p>{ref.historyMetadata.reason}</p>}{ref.error && <p>{ref.error}</p>}{ref.snapshot && <p>行情時間 {time(ref.snapshot.marketTime)}</p>}
      </article>) : <p className="muted">此歷史紀錄沒有市場參考資料。</p>}
      <h4>Token Usage</h4>{analysis.usage ? <dl className="context-grid"><div><dt>Input</dt><dd>{number(analysis.usage.inputTokens)}</dd></div><div><dt>Output</dt><dd>{number(analysis.usage.outputTokens)}</dd></div><div><dt>Cached</dt><dd>{number(analysis.usage.cachedTokens)}</dd></div></dl> : <p className="muted">未提供 Usage。</p>}
    </div></div>
  </div>;
}

export function AnalysisHistory({ analyses, quantityUnit }: { analyses: AnalysisView[]; quantityUnit: string }) {
  const scroll = useRef<HTMLDivElement>(null);
  const [expanded, setExpanded] = useState<Set<number>>(new Set());
  function toggle(id: number) { const opening = !expanded.has(id); if (opening) requestAnimationFrame(() => scroll.current?.scrollTo({ left: 0 })); setExpanded(current => { const next = new Set(current); if (next.has(id)) next.delete(id); else next.add(id); return next; }); }
  return <section className="panel history-panel"><h3>分析歷史（最近 100 筆）</h3><p className="small muted">時間使用 Asia/Taipei；展開可查看當時的分析與市場證據。</p>{analyses.length === 0 ? <p className="muted">尚無分析紀錄。</p> : <div ref={scroll} className="history-scroll" tabIndex={0} role="region" aria-label="分析歷史表格"><table className="history-table"><thead><tr><th scope="col">時間（台北）</th><th scope="col">Provider</th><th scope="col">Model</th><th scope="col">Target Price</th><th scope="col">Action</th><th scope="col">Quantity（{quantityUnit}）</th><th scope="col">Confidence</th><th scope="col">Prompt Version</th><th scope="col">詳細</th></tr></thead><tbody>{analyses.map(a => <Fragment key={a.id}><tr><td><time dateTime={a.createdAt}>{time(a.createdAt)}</time></td><td><span className="provider-heading"><ProviderLogo code={a.provider} />{a.displayName}</span></td><td>{a.model}</td><td>{number(a.context?.targetPrice)}</td><td><SignalBadge action={a.result.action} quantity={null} /></td><td>{number(a.result.quantity)}</td><td>{a.result.confidence}%</td><td>{a.promptVersion ?? '未記錄'}</td><td><button aria-label={`${expanded.has(a.id) ? '收起' : '展開'}分析 ${a.id}`} aria-expanded={expanded.has(a.id)} aria-controls={`history-detail-${a.id}`} onClick={() => toggle(a.id)}>{expanded.has(a.id) ? '收起' : '展開'}</button></td></tr>{expanded.has(a.id) && <tr className="history-expanded"><td colSpan={9}><HistoryDetail analysis={a} quantityUnit={quantityUnit} collapse={() => toggle(a.id)} /></td></tr>}</Fragment>)}</tbody></table></div>}</section>;
}
