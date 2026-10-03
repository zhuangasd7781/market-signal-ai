import { test, expect } from '@playwright/test';

test('login, dynamic board, search, detail, position, history, remove', async ({ page, request }) => {
  // Idempotent preparation touches only the demo fixture used by this test.
  await request.delete('/api/watchlist/4', { headers: { 'X-Market-Signal': 'web' } });
  await page.goto('/');
  await expect(page).toHaveURL(/login/);
  await page.getByRole('button', { name: '進入示範看板' }).click();
  await expect(page.getByRole('heading', { name: '我的追蹤' })).toBeVisible();
  const providers = await (await request.get('/api/ai/providers')).json();
  for (const provider of providers) await expect(page.getByRole('columnheader', { name: provider.displayName })).toBeVisible();
  await expect(page.locator('.changed:not(.changed-placeholder)').first()).toBeVisible();
  await expect(page.getByText('平均成本')).toHaveCount(0);
  await page.screenshot({ path: 'test-results/desktop.png', fullPage: true });
  await page.getByRole('textbox', { name: '搜尋追蹤商品' }).fill('2330');
  await expect(page.locator('tbody tr')).toHaveCount(1);
  await page.getByRole('textbox', { name: '搜尋追蹤商品' }).fill('');
  await page.getByRole('button', { name: '＋ 加入商品', exact: true }).click();
  await page.getByRole('textbox', { name: '搜尋商品代號或名稱' }).fill('0050');
  await page.getByRole('button', { name: '加入', exact: true }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await page.getByRole('link', { name: '0050 元大台灣50' }).click();
  await page.getByRole('button', { name: /設定持倉|編輯持倉/ }).click();
  await page.getByLabel('持有數量（張）').fill('2');
  await page.getByLabel('平均成本（每股／單位）').fill('35.5');
  await page.getByRole('button', { name: '儲存', exact: true }).click();
  await expect(page.getByText('35.5', { exact: true })).toBeVisible();
  await page.reload();
  await expect(page.getByText('35.5', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: '移除追蹤', exact: true }).click();
  await page.getByRole('button', { name: '確認移除' }).click();
  await expect(page.getByRole('link', { name: '0050 元大台灣50' })).toHaveCount(0);
  await page.getByRole('link', { name: '2330 台積電' }).click();
  await expect(page.getByText('失效條件')).toHaveCount(providers.length);
  await page.getByRole('button', { name: '分析歷史', exact: true }).click();
  await expect(page.locator('.history-table tbody > tr')).toHaveCount(6);
  await page.screenshot({ path: 'test-results/detail.png', fullPage: true });
});

test('mobile table stays scrollable without overflowing the page', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/login');
  await page.getByRole('button', { name: '進入示範看板' }).click();
  await expect(page.locator('tbody tr').first()).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  expect(await page.locator('.table-scroll').evaluate(e => e.scrollWidth > e.clientWidth)).toBe(true);
  await page.screenshot({ path: 'test-results/mobile.png', fullPage: true });
});

test('API validates input and preserves duplicate-free tracking with MySQL', async ({ request }) => {
  const headers = { 'X-Market-Signal': 'web' };
  expect((await request.post('/api/watchlist', { data: { productId: 3 }, headers })).status()).toBe(204);
  expect((await request.post('/api/watchlist', { data: { productId: 3 }, headers })).status()).toBe(204);
  const board = await (await request.get('/api/watchlist')).json();
  expect(board.items.filter((x: { product: { id: number } }) => x.product.id === 3)).toHaveLength(1);
  expect((await request.put('/api/products/2330/position', { data: { quantity: -1, averageCost: 3 }, headers })).status()).toBe(400);
  expect((await request.post('/api/watchlist', { data: { productId: 999 }, headers })).status()).toBe(404);
  expect((await request.get('/api/products/AAPL?market=US')).status()).toBe(200);
  expect((await request.get('/api/products/AAPL?market=TW')).status()).toBe(404);
  expect((await request.get('/health')).status()).toBe(200);
});
