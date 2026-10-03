import { expect, test } from '@playwright/test';
const context = (id = 7) => ({ id, windowStart: '2026-09-30T13:55:00Z', windowEnd: '2026-10-03T13:55:00Z', generatedAt: '2026-10-03T13:56:00Z', status: 'PARTIAL', searchProvider: 'FIXED_RSS', configuredModel: 'deepseek-flash', collectorModel: 'deepseek-v4-flash', eventCount: 1, searchResultCount: 10, filteredResultCount: 6, unknownTimeCount: 1, collectorInputCount: 6, promptVersion: 'news-intelligence-v1', usage: { inputTokens: 123, outputTokens: 45, cachedTokens: 20 }, coverage: [{ publisher: '官方來源', topic: 'MACRO', status: 'AVAILABLE', resultCount: 10, limitation: null }], events: [{ id: 'event1', category: 'MACRO', title: `央行政策資訊 ${id}`, summary: '政策方向待確認，僅供市場背景參考。', eventTime: null, publishedAt: '2026-10-03T12:00:00Z', direction: 'NEUTRAL', importance: 'HIGH', relevance: 90, confidence: 70, timeQuality: 'UNKNOWN', sources: [{ resultId: 'source1', publisher: '官方來源', url: 'https://example.com/news', publishedAt: '2026-10-03T12:00:00Z', sourceType: 'OFFICIAL' }] }] });
test.beforeEach(async ({page}) => { await page.addInitScript(() => sessionStorage.setItem('demo-session', 'yes')); });

test('refresh appends displayed context, prevents double submit and preserves time/source metadata', async ({page}) => {
  let refreshes = 0; let release!: () => void; const waiting = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/api/news-context/**', async route => { if (route.request().method() === 'POST') { refreshes++; await waiting; await route.fulfill({status: 201, json: context(8)}); } else await route.fulfill({json: context()}); });
  await page.goto('/news'); await expect(page.getByRole('heading', {name:'市場情報',exact:true})).toBeVisible();
  await expect(page.getByRole('heading', {name:'央行政策資訊 7'})).toBeVisible(); await expect(page.getByText('無法確認',{exact:true})).toBeVisible();
  await expect(page.getByRole('link',{name:'官方來源 ↗'})).toHaveAttribute('href','https://example.com/news');
  const button = page.getByRole('button',{name:'立即更新市場情報'}); await button.click(); await expect(page.getByRole('button',{name:'正在搜尋與整理…'})).toBeDisabled();
  expect(refreshes).toBe(1); release(); await expect(page.getByRole('heading',{name:'央行政策資訊 8'})).toBeVisible();
  await expect(page.getByText(/情報 #8/)).toBeVisible(); await expect(page.getByRole('button',{name:'立即更新市場情報'})).toBeEnabled();
});

test('refresh failure retains latest and empty latest remains actionable', async ({page}) => {
  let empty = false;
  await page.route('**/api/news-context/**', route => route.request().method() === 'POST' ? route.fulfill({status:503,json:{message:'DeepSeek Flash 回應 HTTP 429，未建立市場情報。'}}) : empty ? route.fulfill({status:404,json:{message:'尚未建立'}}) : route.fulfill({json:context()}));
  await page.goto('/news');await page.getByRole('button',{name:'立即更新市場情報'}).click();await expect(page.getByRole('alert')).toContainText('429');await expect(page.getByRole('heading',{name:'央行政策資訊 7'})).toBeVisible();
  empty = true;await page.reload();await expect(page.getByRole('heading',{name:'尚未建立市場情報'})).toBeVisible();await expect(page.getByRole('button',{name:'立即更新市場情報'})).toBeEnabled();
});

test('light/dark desktop/mobile and history direct routes have no overflow or automatic paid calls', async ({page}) => {
  let calls = 0; const errors:string[] = [];page.on('pageerror',e=>errors.push(e.message));
  await page.route('**/api/news-context/**', route => { if(route.request().method() === 'POST') calls++;return route.fulfill({json:context()}); });
  for (const theme of ['light','dark']) for (const width of [1280,390]) {
    await page.setViewportSize({width,height:900});await page.goto('/news/7');await expect(page.getByRole('heading',{name:'央行政策資訊 7'})).toBeVisible();
    await page.evaluate(t=>document.documentElement.dataset.theme=t,theme);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
    await page.reload();await expect(page.getByRole('heading',{name:'市場情報',exact:true})).toBeVisible();
  }
  expect(errors).toEqual([]);expect(calls).toBe(0);
});


test('news renders 10 at a time and loads another 10 at bottom without refresh calls', async ({page}) => {
  let refreshes = 0;
  const data = context(); data.events = Array.from({length: 25}, (_, i) => ({...data.events[0], id: `event-${i}`, title: `市場事件 ${i + 1}`})); data.eventCount = data.events.length;
  await page.route('**/api/news-context/**', route => { if(route.request().method() === 'POST') refreshes++;return route.fulfill({json:data}); });
  await page.goto('/news');await expect(page.locator('.news-event')).toHaveCount(10);
  await page.getByRole('status').filter({hasText:'捲到底部載入更多'}).scrollIntoViewIfNeeded();await expect(page.locator('.news-event')).toHaveCount(20);
  await page.getByRole('status').filter({hasText:'捲到底部載入更多'}).scrollIntoViewIfNeeded();await expect(page.locator('.news-event')).toHaveCount(25);await expect(page.getByText('已顯示全部 25 筆事件')).toBeVisible();
  expect(refreshes).toBe(0);
  const links = page.locator('.topbar nav a'); const first = await links.nth(0).boundingBox(); const second = await links.nth(1).boundingBox();
  expect(second!.x - first!.x - first!.width).toBeGreaterThanOrEqual(12);
});
