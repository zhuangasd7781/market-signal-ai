import { expect, test } from '@playwright/test';

test('analysis display is Chinese, ineffective root events are hidden, and valid events remain visible', async ({ page, request }) => {
  const original = await (await request.get('/api/products/00631L/analysis')).json();
  let status = 'UNVERIFIED';
  let direction = 'BULLISH';
  let summary = '市場公告新資訊。';
  await page.addInitScript(() => sessionStorage.setItem('demo-session', 'yes'));
  await page.route('**/api/ai/settings', route => route.fulfill({ json: original.map((a: { provider: string }) => ({ provider: a.provider, enabled: true, visible: true })) }));
  await page.route('**/api/products/00631L/analysis?*', route => route.fulfill({ json: original.map((a: any) => ({ ...a, result: { ...a.result, rootEvent: { direction, status, summary }, marketRegime: 'BULL', trend: 'BULLISH', momentum: 'POSITIVE', volume: 'SUPPORTIVE', riskReward: 'FAVORABLE', reasons: ['HOLD，等待資料確認，資料為 CLOSE_ONLY。'], nextActions: [{ condition: '條件一', action: 'ADD', quantity: 1 }, { condition: '條件二', action: 'HOLD', quantity: null }, { condition: '條件三', action: 'REDUCE', quantity: 1 }, { condition: '條件四', action: 'EXIT', quantity: null }] } })) }));
  await page.goto('/products/00631L');
  const cards = page.locator('.analysis-grid');
  await expect(cards.locator('.analysis-card').first()).toBeVisible();
  await expect(cards.getByRole('heading', { name: /關鍵事件/ })).toHaveCount(0);
  for (const label of ['多頭', '偏多', '正向', '支持', '偏有利', '失效條件', '後續觸發條件', '持有', '出場']) await expect(cards.getByText(label, { exact: true }).first()).toBeVisible();
  await expect(cards.getByText(/分析信心值/).first()).toBeVisible();
  await expect(cards.getByText(/^加碼 1$/).first()).toBeVisible();
  await expect(cards.getByText(/^減碼 1$/).first()).toBeVisible();
  await expect(cards.getByText('持有，等待資料確認，資料為 僅收盤價。').first()).toBeVisible();
  for (const hidden of ['UNKNOWN', 'INVALIDATED']) {
    status = hidden; await page.reload();
    await expect(cards.getByRole('heading', { name: /關鍵事件/ })).toHaveCount(0);
  }
  for (const active of ['ACTIVE', 'EXPECTED']) {
    status = active; await page.reload();
    await expect(cards.getByRole('heading', { name: /關鍵事件/ }).first()).toBeVisible();
    await expect(cards.getByText(active === 'ACTIVE' ? '已確認' : '預期中', { exact: true }).first()).toBeVisible();
  }
  direction = 'UNKNOWN'; await page.reload();
  await expect(cards.getByRole('heading', { name: /關鍵事件/ })).toHaveCount(0);
  direction = 'BULLISH'; summary = ' '; await page.reload();
  await expect(cards.getByRole('heading', { name: /關鍵事件/ })).toHaveCount(0);
});

test('reference display translates types while mutations keep the original API values', async ({ page }) => {
  const instruments = [{ id: 99, symbol: '^TWII', name: '臺灣加權指數', market: 'TW' }];
  await page.route('**/api/market-reference-instruments', route => route.fulfill({ json: instruments }));
  let payload: { referenceType: string; instrumentId: number } | undefined;
  await page.addInitScript(() => sessionStorage.setItem('demo-session', 'yes'));
  await page.route('**/api/products/00631L/references?*', async route => {
    if (route.request().method() === 'POST') { payload = route.request().postDataJSON(); await route.fulfill({ status: 200, json: {} }); }
    else await route.fulfill({ json: ['UNDERLYING', 'BROAD_MARKET', 'SECTOR'].map((referenceType, index) => ({ id: index + 1, referenceType, instrument: instruments[0] })) });
  });
  await page.goto('/products/00631L');
  const references = page.getByRole('region', { name: '市場參考標的' });
  await references.getByRole('button', { name: '展開市場參考標的' }).click();
  for (const label of ['追蹤標的', '大盤', '產業']) await expect(references.getByText(label, { exact: true })).toBeVisible();
  await references.getByRole('button', { name: '新增此商品參考標的' }).click();
  await references.getByLabel('參考類型', { exact: true }).selectOption({ label: '產業' });
  await references.getByRole('button', { name: '儲存參考設定' }).click();
  await expect(references.getByRole('status')).toContainText('已儲存');
  expect(payload?.referenceType).toBe('SECTOR');
  expect(payload?.instrumentId).toBe(instruments[0].id);
});

test('all direct routes use zh-TW labels and fit desktop/mobile without errors or analysis calls', async ({ page }) => {
  const errors: string[] = []; let force = 0;
  page.on('pageerror', error => errors.push(error.message));
  page.on('request', req => { if (req.url().includes('/analysis/force')) force++; });
  await page.addInitScript(() => sessionStorage.setItem('demo-session', 'yes'));
  const routes = [['/', '我的追蹤'], ['/products/00631L', '00631L'], ['/settings/ai', 'AI 設定'], ['/settings/schedule', '分析排程'], ['/settings/prompts', 'Prompt 設定'], ['/settings/reference-instruments', '共用市場標的']];
  for (const width of [1280, 390]) {
    await page.setViewportSize({ width, height: 900 });
    for (const [path, heading] of routes) {
      await page.goto(path);
      await expect(page.getByRole('heading', { level: 1 })).toContainText(heading);
      await expect(page.locator('.loading')).toHaveCount(0);
      expect(await page.locator('html').getAttribute('lang')).toBe('zh-TW');
      const text = await page.locator('main').evaluate(main => { const copy = main.cloneNode(true) as HTMLElement; copy.querySelectorAll('textarea').forEach(node => node.remove()); return copy.innerText || copy.textContent || ''; });
      expect(text).not.toMatch(/Root Event|Bull Case|Bear Case|Reference Instruments?|Reference Type|Configured Model|YOUR SIGNAL BOARD|ANALYSIS SCHEDULE|MARKET REFERENCE SETTINGS|\b(?:BULLISH|BEARISH|NEUTRAL|UNKNOWN|AVAILABLE|UNAVAILABLE|UNDERLYING|BROAD_MARKET|SECTOR)\b/);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: `test-results/zh-tw-${width}-${path.replaceAll('/', '-') || 'home'}.png`, fullPage: true });
    }
  }
  expect(errors).toEqual([]); expect(force).toBe(0);
});

test('server validation and unavailable settings errors are presented in Chinese', async ({ page }) => {
  await page.addInitScript(() => sessionStorage.setItem('demo-session', 'yes'));
  await page.route('**/api/ai/settings', route => route.fulfill({ status: 503, json: { message: 'Failed to fetch' } }));
  await page.goto('/settings/ai');
  await expect(page.getByRole('alert')).toContainText('無法連線至服務，請確認網路後再試。');
  await page.unroute('**/api/ai/settings');
  await page.route('**/api/ai/settings/*', route => route.fulfill({ status: 400, json: { message: 'Model must be a valid model ID of at most 100 characters.' } }));
  await page.reload();
  await page.getByRole('button', { name: '儲存 GPT', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('請輸入有效的 Model ID，最多 100 個字元。');
});
