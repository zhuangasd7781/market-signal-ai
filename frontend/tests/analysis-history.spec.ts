import { test, expect } from '@playwright/test';

test('history table compares models and expands saved evidence without triggering analysis', async ({ page }) => {
 const product={id:1,symbol:'00631L',name:'元大台灣50正2',market:'TW',assetType:'ETF',isLeveraged:true,quantityUnit:'張',unitSize:1000};
 const providers=[{id:1,code:'openai',displayName:'GPT',sortOrder:1},{id:2,code:'deepseek',displayName:'DeepSeek',sortOrder:2}];
 const result={action:'HOLD',quantity:null,confidence:61,rootEvent:{direction:'NEUTRAL',status:'UNCONFIRMED',summary:'history-only-root-evidence'},marketRegime:'UNKNOWN',trend:'UP',momentum:'UNKNOWN',volume:'UNKNOWN',riskReward:'UNKNOWN',reasons:['history-only-reason'],risks:['history-only-risk'],bullCase:['bull'],bearCase:['bear'],invalidation:'history-only-invalidation',nextActions:[{condition:'等待量能確認',action:'HOLD',quantity:null}]};
 const rows=[{id:101,provider:'openai',displayName:'GPT',model:'actual-model-a',createdAt:'2026-10-02T02:05:00Z',result,usage:{inputTokens:1234,outputTokens:321,cachedTokens:99},promptVersion:'investment-analysis-v2',context:{targetPrice:39.62,configuredModel:'configured-model-a',promptVersionId:2,position:{quantity:3,averageCost:34},skillIdentifiers:['market-evidence-v1'],marketReferences:[{mappingId:1,referenceType:'UNDERLYING',symbol:'^TSE50',name:'Taiwan50',market:'TW',currentValue:45041.66,change:12,changePercent:0.03,status:'AVAILABLE',error:null,snapshot:{marketTime:'2026-10-01T05:30:00Z',fetchedAt:'2026-10-02T02:05:00Z'},history:[{tradeDate:'2026-09-01',close:43491},{tradeDate:'2026-10-01',close:45041}],historyMetadata:{source:'TWSE',sourceSymbol:'TAI50I',dataQuality:'CLOSE_ONLY',isFallback:true,reason:'Yahoo sparse history'}}]}},{id:100,provider:'deepseek',displayName:'DeepSeek',model:'legacy-model-b',createdAt:'2026-10-01T02:05:00Z',result:{...result,confidence:58},usage:null,promptVersion:null,context:null}];
 let force=0;
 await page.route('**/api/**',async route=>{
  const path=new URL(route.request().url()).pathname;
  if(path.endsWith('/analysis/force')){force++;await route.abort();return;}
  if(path==='/api/products/00631L'){await route.fulfill({json:product});return;}
  if(path==='/api/ai/providers'){await route.fulfill({json:providers});return;}
  if(path==='/api/ai/settings'){await route.fulfill({json:providers.map(p=>({provider:p.code,enabled:true}))});return;}
  if(path==='/api/watchlist'){await route.fulfill({json:{providers,items:[{product,signals:[],lastAnalyzedAt:null}],isMock:true}});return;}
  if(path.endsWith('/position')){await route.fulfill({json:{position:null,quote:null}});return;}
  if(path.endsWith('/market')){await route.fulfill({status:404,json:{message:'No market'}});return;}
  if(path.endsWith('/analysis/history')){await route.fulfill({json:rows});return;}
  if(path.endsWith('/analysis')){await route.fulfill({json:[]});return;}
  await route.abort();
 });
 await page.addInitScript(()=>sessionStorage.setItem('demo-session','yes'));
 await page.goto('/products/00631L');
 await page.getByRole('button',{name:'分析歷史',exact:true}).click();
 const table=page.getByRole('table');
 await expect(table.getByText('actual-model-a')).toBeVisible();
 await expect(table.getByText('39.62')).toBeVisible();
 await expect(table.getByText('investment-analysis-v2')).toBeVisible();
 await expect(page.getByText('history-only-reason')).toHaveCount(0);
 await page.getByRole('button',{name:'展開分析 101',exact:true}).click();
 await expect.poll(() => page.locator('.history-scroll').evaluate(e => e.scrollLeft)).toBe(0);
 await expect(page.getByText('history-only-reason')).toBeVisible();
 await expect(page.getByText('history-only-invalidation')).toBeVisible();
 await expect(page.getByText(/UNDERLYING.*Taiwan50/)).toBeVisible();
 await expect(page.getByText(/TWSE.*TAI50I.*CLOSE_ONLY/)).toBeVisible();
 await expect(page.getByText('1,234',{exact:true})).toBeVisible();
 await expect(page.getByRole('button',{name:'收起分析 101'})).toHaveAttribute('aria-expanded','true');
 await page.getByRole('button',{name:'展開分析 100',exact:true}).click();
 await expect(page.getByText('此歷史紀錄未提供輸入摘要。')).toBeVisible();
 await page.setViewportSize({width:390,height:844});
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
 await expect(page.locator('.history-scroll')).toBeVisible();
 await page.getByRole('button',{name:'收起分析 101',exact:true}).click();
 await expect(page.getByRole('button',{name:'展開分析 101'})).toHaveAttribute('aria-expanded','false');
 await page.getByRole('button',{name:'展開分析 101',exact:true}).click();
 await expect.poll(() => page.locator('.history-scroll').evaluate(e => e.scrollLeft)).toBe(0);
 await page.getByRole('button',{name:'收起詳細分析 101',exact:true}).click();
 await expect(page.getByRole('button',{name:'展開分析 101'})).toHaveAttribute('aria-expanded','false');
 expect(force).toBe(0);
});
