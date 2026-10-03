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
  await page.getByRole('button', { name: 'Demo 投資人，開啟帳戶選單' }).click();
  await page.getByRole('navigation', { name: '帳戶與設定' }).getByRole('link', { name: 'Reference Instruments' }).click();
  const manager = page.getByRole('region', { name: 'Reference Instruments 管理' });
  for (const [symbol, name] of [['^TSE50', 'Taiwan50'], ['^TWII', 'TAIEX']]) {
    await manager.getByRole('button', { name: '新增 Instrument', exact: true }).click();
    await manager.getByRole('textbox', { name: 'Yahoo ticker' }).fill(symbol);
    await manager.getByRole('textbox', { name: 'Reference 名稱' }).fill(name);
    await manager.getByRole('button', { name: '儲存 Instrument', exact: true }).click();
    await expect(manager.getByRole('status')).toHaveText('Reference Instrument 已儲存');
  }
  const instruments = await (await request.get(base + '/api/market-reference-instruments')).json();
  await page.goto('/products/00631L?market=TW');
  const panel = page.getByRole('region', { name: '市場參考標的' });
  for (const [symbol, type] of [['^TSE50', 'UNDERLYING'], ['^TWII', 'BROAD_MARKET']]) {
    await panel.getByRole('button', { name: '新增此商品參考標的', exact: true }).click();
    await panel.getByLabel('Reference Instrument', { exact: true }).selectOption(String(instruments.find((i: {symbol:string}) => i.symbol === symbol).id));
    await panel.getByLabel('Reference Type', { exact: true }).selectOption(type);
    await panel.getByRole('button', { name: '儲存 Mapping', exact: true }).click();
    await expect(panel.getByRole('status')).toHaveText('Reference Mapping 已儲存');
  }
  await page.reload();
  await expect(panel.getByText('UNDERLYING', { exact: true })).toBeVisible();
  await expect(panel.getByText('BROAD_MARKET', { exact: true })).toBeVisible();
  await panel.getByRole('button', { name: '修改 ^TWII Mapping', exact: true }).click();
  await panel.getByLabel('Reference Type', { exact: true }).selectOption('SECTOR');
  await panel.getByRole('button', { name: '儲存 Mapping', exact: true }).click();
  await expect(panel.getByText('SECTOR', { exact: true })).toBeVisible();
  await page.goto('/settings/reference-instruments');
  await manager.getByRole('button', { name: '修改 ^TWII Instrument', exact: true }).click();
  await manager.getByRole('textbox', { name: 'Reference 名稱' }).fill('TAIEX renamed');
  await manager.getByRole('button', { name: '儲存 Instrument', exact: true }).click();
  await expect(manager.getByText('TAIEX renamed · TW', { exact: true })).toBeVisible();
  await manager.getByRole('button', { name: '移除 ^TWII Instrument', exact: true }).click();
  await manager.getByRole('button', { name: '確認移除 Reference' }).click();
  await expect(manager.locator('.error')).toContainText('Remove product mappings');
  await manager.getByRole('button', { name: '取消移除' }).click();
  await page.goto('/products/00631L?market=TW');
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  for (const symbol of ['^TWII', '^TSE50']) {
    await panel.getByRole('button', { name: `移除 ${symbol} Mapping`, exact: true }).click();
    await panel.getByRole('button', { name: '確認移除 Reference' }).click();
    await expect(panel.getByRole('status')).toHaveText('Reference Mapping 已移除');
  }
  await expect(panel.getByText('尚未設定 Reference，分析將使用商品本身資料。')).toBeVisible();
  await page.goto('/settings/reference-instruments');
  for (const symbol of ['^TWII', '^TSE50']) {
    await manager.getByRole('button', { name: `移除 ${symbol} Instrument`, exact: true }).click();
    await manager.getByRole('button', { name: '確認移除 Reference' }).click();
    await expect(manager.getByRole('status')).toHaveText('Reference Instrument 已移除');
  }
  expect(calls.some(c => c.endsWith('/analysis/force'))).toBeFalsy();
  expect(errors).toEqual([]);
});
