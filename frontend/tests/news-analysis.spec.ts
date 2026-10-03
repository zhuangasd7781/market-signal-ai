import { test, expect } from '@playwright/test';
test('force uses saved backend preference and news switch exists only in AI settings', async ({ page, request }) => {
  const rows = await (await request.get('/api/products/00631L/analysis')).json();
  const posts: unknown[] = []; let preference=false, saves=0;
  await page.addInitScript(() => sessionStorage.setItem('demo-session', 'yes'));
  await page.route('**/api/ai/analysis-settings', route => {
    if(route.request().method()==='PUT'){preference=route.request().postDataJSON().refreshNewsBeforeAnalysis;saves++;return route.fulfill({status:204});}
    return route.fulfill({json:{refreshNewsBeforeAnalysis:preference}});
  });
  const news = { newsContextId: 87, generatedAt: '2026-10-03T14:00:00Z', status: 'PARTIAL', freshness: 'STALE', newsRefreshFailed: true, events: [{ title: '政策資訊' }] };
  await page.route('**/api/products/00631L/analysis?*', route => route.fulfill({ json: rows.map((r:any) => ({ ...r, context: {...r.context,newsContext:news} })) }));
  await page.route('**/api/products/00631L/analysis/force', route => { posts.push(route.request().postDataJSON());return route.fulfill({json:{providers:[{provider:'deepseek',status:'COMPLETED',error:null}]}});});
  await page.goto('/settings/ai'); const checkbox=page.getByRole('checkbox',{name:'分析前更新市場情報'});
  await expect(checkbox).not.toBeChecked();await checkbox.check();await page.getByRole('button',{name:'儲存市場情報設定'}).click();
  await expect(page.getByText('市場情報設定已儲存',{exact:true})).toBeVisible();expect(posts).toEqual([]);expect(saves).toBe(1);
  await page.reload();await expect(checkbox).toBeChecked();await page.goto('/products/00631L');
  await expect(checkbox).toHaveCount(0);await expect(page.locator('.analysis-news-evidence')).toHaveCount(1);await expect(page.locator('.analysis-grid .analysis-news-evidence')).toHaveCount(0);
  await page.getByRole('button',{name:'強制分析',exact:true}).click();await expect.poll(()=>posts.length).toBe(1);expect(posts[0]).toEqual({});
  await expect(page.getByRole('button',{name:'強制分析',exact:true})).toBeEnabled();expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
});
test('unavailable news and force error work on dark mobile with no automatic analysis', async ({page, request}) => {
  const rows = await (await request.get('/api/products/00631L/analysis')).json(); let calls=0;
  await page.addInitScript(() => sessionStorage.setItem('demo-session', 'yes'));
  await page.route('**/api/products/00631L/analysis?*', route => route.fulfill({json:rows.map((r:any)=>({...r,context:{...r.context,newsContext:{newsContextId:null,status:'UNAVAILABLE',freshness:'UNAVAILABLE',events:[],newsRefreshFailed:false}}}))}));
  await page.route('**/api/products/00631L/analysis/force', route => {calls++; return route.fulfill({status:503,json:{message:'服務暫時無法使用，請稍後再試。'}});});
  await page.setViewportSize({width:390,height:844});await page.goto('/products/00631L');await page.evaluate(()=>document.documentElement.dataset.theme='dark');
  await expect(page.locator('.analysis-news-evidence').first()).toContainText('無資料');expect(calls).toBe(0);await page.getByRole('button',{name:'強制分析',exact:true}).click();await expect(page.getByRole('alert')).toBeVisible();
  expect(calls).toBe(1);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
});

test('shared news summary stays above reports and labels different historical contexts', async ({page, request}) => {
  const rows = await (await request.get('/api/products/00631L/analysis')).json();
  await page.addInitScript(() => sessionStorage.setItem('demo-session', 'yes'));
  await page.route('**/api/products/00631L/analysis?*', route => route.fulfill({json:rows.map((r:any,i:number)=>({...r,context:{...r.context,newsContext:{newsContextId:87+i,generatedAt:'2026-10-03T14:00:00Z',status:'PARTIAL',freshness:'FRESH',newsRefreshFailed:false,events:[]}}}))}));
  await page.goto('/products/00631L');
  const summary=page.getByRole('region',{name:'本次分析使用的市場情報'});
  await expect(summary).toBeVisible();await expect(page.locator('.analysis-news-evidence')).toHaveCount(1);
  await expect(summary.getByRole('link',{name:'NewsContext #87'})).toBeVisible();await expect(summary.getByRole('link',{name:'NewsContext #88'})).toBeVisible();
  await expect(summary).toContainText('DeepSeek');await expect(summary).toContainText('GPT');
  const above=await page.evaluate(()=>document.querySelector('.analysis-news-evidence')!.getBoundingClientRect().bottom<=document.querySelector('.analysis-grid')!.getBoundingClientRect().top);
  expect(above).toBe(true);await expect(page.locator('.analysis-card .analysis-news-evidence')).toHaveCount(0);
});
