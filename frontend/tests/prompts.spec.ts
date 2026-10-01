import { test, expect } from '@playwright/test';
test('prompt settings creates immutable version, shows old content, activates history without analysis calls', async ({ page }) => {
  const seed = { id: 1, version: 'investment-analysis-v1', content: 'Original risk rules', createdAt: '2026-10-02T01:00:00Z' };
  const versions = [seed]; let activeVersionId = 1;
  let analysisCalls = 0;
  await page.addInitScript(() => sessionStorage.setItem('demo-session', 'yes'));
  await page.route('**/api/**', async route => {
    const req = route.request(); const path = new URL(req.url()).pathname;
    if (path.includes('/analysis/force')) { analysisCalls++; await route.abort(); return; }
    if (path === '/api/prompts/active') { activeVersionId = req.postDataJSON().versionId; await route.fulfill({ status: 204 }); return; }
    if (path === '/api/prompts') {
      if (req.method() === 'POST') { const added = { id: 2, version: 'investment-analysis-v2', content: req.postDataJSON().content, createdAt: '2026-10-02T02:00:00Z' }; versions.unshift(added); activeVersionId = added.id; await route.fulfill({ status: 201, json: added }); return; }
      await route.fulfill({ json: { activeVersionId, versions } }); return;
    }
    await route.fulfill({ status: 404, json: { message: 'Unexpected API request' } });
  });
  await page.goto('/settings/prompts');
  await expect(page.getByRole('heading', { name: 'Prompt 設定' })).toBeVisible();
  await expect(page.getByLabel('Prompt 內容')).toHaveValue(seed.content);
  await page.getByLabel('Prompt 內容').fill('New independent risk rules');
  await page.getByRole('button', { name: '儲存為新版本並啟用' }).click();
  await expect(page.getByRole('status')).toContainText('investment-analysis-v2');
  await page.getByLabel('版本紀錄').selectOption('1');
  await expect(page.getByLabel('Prompt 內容')).toHaveValue('Original risk rules');
  await page.getByRole('button', { name: '啟用選取的已儲存版本' }).click();
  await expect(page.getByRole('status')).toHaveText('已切換使用中的版本');
  expect(seed.content).toBe('Original risk rules'); expect(activeVersionId).toBe(1);
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.getByLabel('Prompt 內容')).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  expect(analysisCalls).toBe(0);
});

test('real prompt settings API preserves versions, restores active selection and never requests analysis', async ({ page, request }) => {
  test.skip(process.env.TEST_PROMPT_LIVE !== '1', 'Opt in against an isolated backend only');
  const backend = process.env.TEST_PROMPT_BACKEND ?? '';
  const initialResponse = await request.get(`${backend}/api/prompts`); expect(initialResponse.ok()).toBe(true);
  const initial = await initialResponse.json(); const original = initial.versions.find((v: { id: number }) => v.id === initial.activeVersionId);
  const visibleOriginal = original.content.replace(/\r\n/g, '\n');
  let analysisCalls = 0;
  await page.addInitScript(() => sessionStorage.setItem('demo-session', 'yes'));
  await page.route('**/api/**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.includes('/analysis/force')) { analysisCalls++; await route.abort(); return; }
    if (backend) { const response = await route.fetch({ url: backend + path }); await route.fulfill({ response }); }
    else await route.continue();
  });
  try {
    await page.goto('/settings/prompts');
    await expect(page.getByLabel('Prompt 內容')).toHaveValue(visibleOriginal);
    const updated = `${visibleOriginal}\nUI verification: distinguish unavailable evidence explicitly.`;
    await page.getByLabel('Prompt 內容').fill(updated);
    await page.getByRole('button', { name: '儲存為新版本並啟用' }).click();
    await expect(page.getByRole('status')).toContainText('已建立並啟用');
    const saved = await (await request.get(`${backend}/api/prompts`)).json();
    expect(saved.versions.length).toBe(initial.versions.length + 1);
    expect(saved.versions.find((v: { id: number }) => v.id === original.id).content).toBe(original.content);
    await page.reload(); await expect(page.getByLabel('Prompt 內容')).toHaveValue(updated);
    await page.getByLabel('版本紀錄').selectOption(String(original.id));
    await expect(page.getByLabel('Prompt 內容')).toHaveValue(visibleOriginal);
    await page.getByRole('button', { name: '啟用選取的已儲存版本' }).click();
    await expect(page.getByRole('status')).toHaveText('已切換使用中的版本');
    expect((await (await request.get(`${backend}/api/prompts`)).json()).activeVersionId).toBe(original.id);
    expect(analysisCalls).toBe(0);
  } finally {
    const restored = await request.put(`${backend}/api/prompts/active`, { headers: { 'X-Market-Signal': 'web' }, data: { versionId: original.id } });
    expect(restored.status()).toBe(204);
  }
});
