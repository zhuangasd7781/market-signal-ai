import { expect, test } from '@playwright/test';

test('account dropdown navigates existing settings routes and works by mouse and keyboard', async ({ page }) => {
  const errors: string[] = [];
  let forceCalls = 0;
  page.on('pageerror', error => errors.push(error.message));
  await page.route('**/api/products/*/analysis/force', async route => { forceCalls++; await route.abort(); });
  await page.addInitScript(() => sessionStorage.setItem('demo-session', 'yes'));
  await page.goto('/');
  await expect(page.getByRole('heading', { name: '我的追蹤' })).toBeVisible();
  const trigger = page.getByRole('button', { name: 'Demo 投資人，開啟帳戶選單' });
  await trigger.click();
  const menu = page.getByRole('navigation', { name: '帳戶與設定' });
  await expect(menu).toBeVisible();
  await expect(trigger).toHaveAttribute('aria-expanded', 'true');
  for (const label of ['AI 設定', '分析排程', 'Prompt 設定', 'Reference Instruments'])
    await expect(menu.getByRole('link', { name: label })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(menu).toHaveCount(0);
  await expect(trigger).toBeFocused();
  await trigger.click();
  await page.mouse.click(5, 140);
  await expect(menu).toHaveCount(0);

  for (const [label, path, heading] of [
    ['AI 設定', '/settings/ai', 'AI 設定'],
    ['分析排程', '/settings/schedule', '分析排程'],
    ['Prompt 設定', '/settings/prompts', 'Prompt 設定'],
    ['Reference Instruments', '/settings/reference-instruments', 'Reference Instruments'],
  ]) {
    await trigger.click();
    await menu.getByRole('link', { name: label }).click();
    await expect(page).toHaveURL(new RegExp(path.replaceAll('/', '\/')));
    await expect(page.getByRole('heading', { name: heading, exact: true })).toBeVisible();
    await expect(menu).toHaveCount(0);
    await page.reload();
    await expect(page.getByRole('heading', { name: heading, exact: true })).toBeVisible();
  }
  await page.goto('/products/00631L?market=TW');
  await expect(page.getByRole('heading', { name: '市場參考標的' })).toBeVisible();
  await expect(page.getByRole('button', { name: '新增此商品參考標的' })).toBeVisible();
  await expect(page.getByRole('button', { name: '管理 Reference Instruments' })).toHaveCount(0);
  await page.setViewportSize({ width: 390, height: 844 });
  await trigger.click();
  await expect(menu).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await menu.getByRole('link', { name: '分析排程' }).click();
  await expect(page.getByRole('heading', { name: '分析排程' })).toBeVisible();
  expect(errors).toEqual([]);
  expect(forceCalls).toBe(0);
});

test('product provider headings follow backend enabled state', async ({ page, request }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  const providers = await (await request.get('/api/ai/providers')).json() as { code: string; displayName: string }[];
  const enabled = new Map(providers.map(row => [row.code, row.code !== 'deepseek']));
  let settingsReads = 0;
  await page.route('**/api/ai/settings', async route => {
    settingsReads++;
    await route.fulfill({ json: providers.map(row => ({ provider: row.code, enabled: enabled.get(row.code) })) });
  });
  await page.addInitScript(() => sessionStorage.setItem('demo-session', 'yes'));
  await page.goto('/products/00631L?market=TW');
  const heading = (code: string) => page.locator('.analysis-grid .provider-heading').filter({ has: page.locator(`.provider-logo--${code}`) });
  await expect(heading('deepseek')).toContainText('未啟用');
  await expect(heading('deepseek')).toHaveClass(/provider-heading--disabled/);
  await expect(heading('openai')).not.toHaveClass(/provider-heading--disabled/);
  enabled.set('deepseek', true);
  enabled.set('openai', false);
  await page.reload();
  await expect(heading('deepseek')).not.toHaveClass(/provider-heading--disabled/);
  await expect(heading('openai')).toContainText('未啟用');
  await expect(heading('openai')).toHaveClass(/provider-heading--disabled/);
  expect(settingsReads).toBeGreaterThanOrEqual(2);
  expect(errors).toEqual([]);
});
