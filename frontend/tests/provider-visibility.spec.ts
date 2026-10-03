import { expect, test } from '@playwright/test';

test('saved visibility hides overview columns and product cards, preserves history, and restores on reload', async ({ page, request }) => {
  let rows = await (await request.get('/api/ai/settings')).json();
  rows = rows.map((row: { visible?: boolean }) => ({ ...row, visible: true }));
  let forceCalls = 0;
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('request', req => { if (req.url().includes('/analysis/force')) forceCalls++; });
  await page.addInitScript(() => sessionStorage.setItem('demo-session', 'yes'));
  await page.route('**/api/ai/settings**', async route => {
    const req = route.request();
    if (req.method() === 'PUT') {
      const provider = new URL(req.url()).pathname.split('/').at(-1);
      rows = rows.map((row: { provider: string }) => row.provider === provider ? { ...row, ...req.postDataJSON() } : row);
      await route.fulfill({ status: 204 });
    } else await route.fulfill({ json: rows });
  });
  await page.goto('/settings/ai');
  const enabled = await page.getByRole('checkbox', { name: '啟用 Claude', exact: true }).isChecked();
  await page.getByRole('checkbox', { name: '顯示 Claude', exact: true }).uncheck();
  await page.getByRole('button', { name: '儲存 Claude', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('Claude 設定已儲存');
  await page.reload();
  await expect(page.getByRole('checkbox', { name: '顯示 Claude', exact: true })).not.toBeChecked();
  expect(await page.getByRole('checkbox', { name: '啟用 Claude', exact: true }).isChecked()).toBe(enabled);
  await page.goto('/');
  await expect(page.getByRole('columnheader', { name: 'DeepSeek', exact: true })).toBeVisible();
  await expect(page.getByRole('columnheader', { name: 'Claude', exact: true })).toHaveCount(0);
  await page.goto('/products/00631L');
  await expect(page.locator('.analysis-grid .provider-logo--deepseek')).toBeVisible();
  await expect(page.locator('.analysis-grid .provider-logo--claude')).toHaveCount(0);
  await page.getByRole('button', { name: '分析歷史', exact: true }).click();
  await expect(page.locator('.history-table .provider-logo--claude').first()).toBeVisible();
  await page.goto('/settings/ai');
  await page.getByRole('checkbox', { name: '顯示 Claude', exact: true }).check();
  await page.getByRole('button', { name: '儲存 Claude', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('Claude 設定已儲存');
  await page.goto('/products/00631L');
  await expect(page.locator('.analysis-grid .provider-logo--claude')).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/settings/ai');
  await expect(page.getByRole('checkbox', { name: '顯示 Claude', exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  expect(errors).toEqual([]); expect(forceCalls).toBe(0);
});
