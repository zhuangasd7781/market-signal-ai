import { test, expect } from '@playwright/test';
// Opt-in isolated backend: never forward mutations to the normal live development API.
test('reference UI manages real API instruments and multiple mappings without analysis', async ({ page, request }) => {
  const backend = process.env.REFERENCE_TEST_API;
  test.skip(!backend, 'Set REFERENCE_TEST_API to an isolated Memory/DB backend with providers and worker OFF.');
  const base = backend!;
  expect((await (await request.get(base + '/health')).json()).storage).toBe('Memory');
  const headers = { 'X-Market-Signal': 'web' };
  // Memory storage has no production seed; clean only this test's prior interrupted run.
  for (const mapping of await (await request.get(base + '/api/products/00631L/references?market=TW')).json()) {
    await request.delete(base + `/api/products/00631L/references/${mapping.id}?market=TW`, { headers });
  }
  for (const item of await (await request.get(base + '/api/market-reference-instruments')).json()) {
    await request.delete(base + `/api/market-reference-instruments/${item.id}`, { headers });
  }
  const calls: string[] = [];
  const errors: string[] = [];
  page.on('pageerror', e => errors.push(e.message));
  await page.addInitScript(() => sessionStorage.setItem('demo-session', 'yes'));
  await page.route('**/api/**', async route => {
    const req = route.request(); const url = new URL(req.url()); calls.push(`${req.method()} ${url.pathname}`);
    if (url.pathname.endsWith('/analysis/force')) throw new Error('Unexpected paid analysis request');
    const response = await request.fetch(base + url.pathname + url.search, { method: req.method(), headers: req.headers(), data: req.postData() ?? undefined });
    await route.fulfill({ response });
  });
  await page.goto('/products/00631L?market=TW');
  await page.getByRole('button', { name: '示範投資人，開啟帳戶選單' }).click();
  await page.getByRole('navigation', { name: '帳戶與設定' }).getByRole('link', { name: '共用市場標的' }).click();
  const manager = page.getByRole('region', { name: '共用市場標的管理' });
  for (const [symbol, name] of [['^TSE50', 'Taiwan50'], ['^TWII', 'TAIEX']]) {
    await manager.getByRole('button', { name: '新增標的', exact: true }).click();
    await manager.getByRole('textbox', { name: 'Yahoo Symbol' }).fill(symbol);
    await manager.getByRole('textbox', { name: '標的名稱' }).fill(name);
    await manager.getByRole('button', { name: '儲存標的', exact: true }).click();
    await expect(manager.getByRole('status')).toHaveText('共用市場標的已儲存');
  }
  const instruments = await (await request.get(base + '/api/market-reference-instruments')).json();
  await page.goto('/products/00631L?market=TW');
  const panel = page.getByRole('region', { name: '市場參考標的' });
  await panel.getByRole('button', { name: '展開市場參考標的' }).click();
  for (const [symbol, type] of [['^TSE50', 'UNDERLYING'], ['^TWII', 'BROAD_MARKET']]) {
    await panel.getByRole('button', { name: '新增此商品參考標的', exact: true }).click();
    await panel.getByLabel('共用市場標的', { exact: true }).selectOption(String(instruments.find((i: {symbol:string}) => i.symbol === symbol).id));
    await panel.getByLabel('參考類型', { exact: true }).selectOption(type);
    await panel.getByRole('button', { name: '儲存參考設定', exact: true }).click();
    await expect(panel.getByRole('status')).toHaveText('商品參考設定已儲存');
  }
  await page.reload();
  await panel.getByRole('button', { name: '展開市場參考標的' }).click();
  await expect(panel.getByText('追蹤標的', { exact: true })).toBeVisible();
  await expect(panel.getByText('大盤', { exact: true })).toBeVisible();
  await panel.getByRole('button', { name: '修改 ^TWII 參考設定', exact: true }).click();
  await panel.getByLabel('參考類型', { exact: true }).selectOption('SECTOR');
  await panel.getByRole('button', { name: '儲存參考設定', exact: true }).click();
  await expect(panel.getByText('產業', { exact: true })).toBeVisible();
  await page.goto('/settings/reference-instruments');
  await manager.getByRole('button', { name: '修改 ^TWII 標的', exact: true }).click();
  await manager.getByRole('textbox', { name: '標的名稱' }).fill('TAIEX renamed');
  await manager.getByRole('button', { name: '儲存標的', exact: true }).click();
  await expect(manager.getByText('TAIEX renamed · 台灣', { exact: true })).toBeVisible();
  await manager.getByRole('button', { name: '移除 ^TWII 標的', exact: true }).click();
  await manager.getByRole('button', { name: '確認移除標的' }).click();
  await expect(manager.locator('.error')).toContainText('此標的仍有商品使用');
  await manager.getByRole('button', { name: '取消移除' }).click();
  await page.goto('/products/00631L?market=TW');
  await panel.getByRole('button', { name: '展開市場參考標的' }).click();
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  for (const symbol of ['^TWII', '^TSE50']) {
    await panel.getByRole('button', { name: `移除 ${symbol} 參考設定`, exact: true }).click();
    await panel.getByRole('button', { name: '確認移除參考標的' }).click();
    await expect(panel.getByRole('status')).toHaveText('商品參考設定已移除');
  }
  await expect(panel.getByText('尚未設定參考標的，分析將使用商品本身資料。')).toBeVisible();
  await page.goto('/settings/reference-instruments');
  for (const symbol of ['^TWII', '^TSE50']) {
    await manager.getByRole('button', { name: `移除 ${symbol} 標的`, exact: true }).click();
    await manager.getByRole('button', { name: '確認移除標的' }).click();
    await expect(manager.getByRole('status')).toHaveText('共用市場標的已移除');
  }
  expect(calls.some(c => c.endsWith('/analysis/force'))).toBeFalsy();
  expect(errors).toEqual([]);
});
