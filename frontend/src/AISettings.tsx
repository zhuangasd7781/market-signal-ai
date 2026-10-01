import { useEffect, useState } from 'react';
import { api } from './api';
import { ErrorState, LoadingState } from './components';
type Setting={provider:string;displayName:string;enabled:boolean;configuredModel:string;actualModel:string|null;isMock:boolean};
export function AISettings(){
 const [rows,setRows]=useState<Setting[]>();const [error,setError]=useState('');const [busy,setBusy]=useState<string|null>(null);const [saved,setSaved]=useState('');
 useEffect(()=>{void api<Setting[]>('/ai/settings').then(setRows).catch(e=>setError(e.message));},[]);
 function change(provider:string,patch:Partial<Setting>){setSaved('');setRows(old=>old?.map(r=>r.provider===provider?{...r,...patch}:r));}
 async function save(row:Setting){setBusy(row.provider);setError('');setSaved('');try{await api(`/ai/settings/${encodeURIComponent(row.provider)}`,{method:'PUT',body:JSON.stringify({enabled:row.enabled,configuredModel:row.configuredModel})});setSaved(`${row.displayName} 設定已儲存`);}catch(e){setError((e as Error).message);}finally{setBusy(null);}}
 return <><div className="page-heading"><div><span className="eyebrow">AI SETTINGS</span><h1>AI 設定</h1><p className="muted">控制自動與手動分析使用的 AI。儲存設定不會執行分析。</p></div></div>{error&&<ErrorState message={error}/>}<p role="status">{saved}</p>{!rows?!error&&<LoadingState/>:rows.map(row=><section className="panel" key={row.provider}><h2>{row.displayName}{row.isMock?'（Mock）':''}</h2><form className="position-form" onSubmit={e=>{e.preventDefault();void save(row);}}><label><span>啟用 {row.displayName}</span><input aria-label={`啟用 ${row.displayName}`} type="checkbox" checked={row.enabled} onChange={e=>change(row.provider,{enabled:e.target.checked})}/></label><label>Configured Model<input aria-label={`${row.displayName} Configured Model`} required maxLength={100} value={row.configuredModel} readOnly={row.isMock} onChange={e=>change(row.provider,{configuredModel:e.target.value})}/></label><button className="primary" disabled={busy!==null} aria-label={`儲存 ${row.displayName}`}>{busy===row.provider?'儲存中…':'儲存'}</button></form><p className="small muted">最近分析實際模型：{row.actualModel??'尚無分析'}。修改設定不會改寫歷史分析。</p></section>)}</>;
}
