import { useEffect, useState } from 'react';
import { api } from './api';
import { ErrorState, LoadingState } from './components';
type Setting={provider:string;displayName:string;enabled:boolean;visible:boolean;configuredModel:string;actualModel:string|null;isMock:boolean};
function NewsAnalysisSettings() {
 const [settings,setSettings]=useState<{refreshNewsBeforeAnalysis:boolean}>();
 const [busy,setBusy]=useState(false);const [error,setError]=useState('');const [saved,setSaved]=useState(false);
 useEffect(()=>{void api<{refreshNewsBeforeAnalysis:boolean}>('/ai/analysis-settings').then(setSettings).catch(e=>setError(e.message));},[]);
 async function save(e:React.FormEvent){e.preventDefault();if(!settings || busy)return;setBusy(true);setError('');setSaved(false);try{await api('/ai/analysis-settings',{method:'PUT',body:JSON.stringify(settings)});setSaved(true);}catch(e){setError((e as Error).message);}finally{setBusy(false);}}
 return <section className="panel"><h2>市場情報設定</h2><p className="small muted">控制每次強制分析是否先使用 DeepSeek Flash 更新市場情報。關閉時沿用既有情報；開啟會增加新聞整理 API 用量。此設定不影響自動分析排程，也不會啟用 GPT。</p>
 {error&&<ErrorState message={error}/>} {!settings ? !error&&<LoadingState/> : <form className="position-form" onSubmit={e=>void save(e)}><label><span>分析前更新市場情報</span><input type="checkbox" checked={settings.refreshNewsBeforeAnalysis} disabled={busy} onChange={e=>{setSettings({refreshNewsBeforeAnalysis:e.target.checked});setSaved(false);}}/></label><button className="primary" aria-label="儲存市場情報設定" disabled={busy}>{busy?'儲存中…':'儲存'}</button></form>}
 {saved&&<p className="small" role="status">市場情報設定已儲存</p>}</section>;
}
export function AISettings(){
 const [rows,setRows]=useState<Setting[]>();const [error,setError]=useState('');const [busy,setBusy]=useState<string|null>(null);const [saved,setSaved]=useState('');
 useEffect(()=>{void api<Setting[]>('/ai/settings').then(setRows).catch(e=>setError(e.message));},[]);
 function change(provider:string,patch:Partial<Setting>){setSaved('');setRows(old=>old?.map(r=>r.provider===provider?{...r,...patch}:r));}
 async function save(row:Setting){setBusy(row.provider);setError('');setSaved('');try{await api(`/ai/settings/${encodeURIComponent(row.provider)}`,{method:'PUT',body:JSON.stringify({enabled:row.enabled,visible:row.visible,configuredModel:row.configuredModel})});setSaved(`${row.displayName} 設定已儲存`);}catch(e){setError((e as Error).message);}finally{setBusy(null);}}
 return <><div className="page-heading"><div><span className="eyebrow">AI 分析設定</span><h1>AI 設定</h1><p className="muted">控制自動與手動分析使用的 AI。顯示設定只影響首頁與商品頁；停止分析請關閉啟用。歷史分析仍保留。儲存設定不會執行分析。</p></div></div>{error&&<ErrorState message={error}/>}<p role="status">{saved}</p><NewsAnalysisSettings />{!rows?!error&&<LoadingState/>:rows.map(row=><section className="panel" key={row.provider}><h2>{row.displayName}{row.isMock?'（示範）':''}</h2><form className="position-form" onSubmit={e=>{e.preventDefault();void save(row);}}><label><span>啟用 {row.displayName}</span><input aria-label={`啟用 ${row.displayName}`} type="checkbox" checked={row.enabled} onChange={e=>change(row.provider,{enabled:e.target.checked})}/></label><label><span>顯示 {row.displayName}</span><input aria-label={`顯示 ${row.displayName}`} type="checkbox" checked={row.visible !== false} onChange={e=>change(row.provider,{visible:e.target.checked})}/></label><label>設定模型<input aria-label={`${row.displayName} 設定模型`} required maxLength={100} value={row.configuredModel} readOnly={row.isMock} onChange={e=>change(row.provider,{configuredModel:e.target.value})}/></label><button className="primary" disabled={busy!==null} aria-label={`儲存 ${row.displayName}`}>{busy===row.provider?'儲存中…':'儲存'}</button></form><p className="small muted">最近分析實際模型：{row.actualModel??'尚無分析'}。修改設定不會改寫歷史分析。</p></section>)}</>;
}
