import { expect, test } from '@playwright/test';

test('all providers use Taiwanese semantic badges with readable text and safe future-value fallbacks', async ({ page, request }) => {
  const rows = await (await request.get('/api/products/00631L/analysis')).json();
  const providerOrder = await (await request.get('/api/ai/providers')).json();
  rows.sort((a: {provider:string}, b: {provider:string}) => providerOrder.findIndex((p: {code:string}) => p.code === a.provider) - providerOrder.findIndex((p: {code:string}) => p.code === b.provider));
  const fields = ['marketRegime', 'trend', 'momentum', 'volume', 'riskReward'];
  const examples = [
    ['BULL', 'BULLISH', 'POSITIVE', 'SUPPORTIVE', 'FAVORABLE'],
    ['BEAR', 'BEARISH', 'NEGATIVE', 'WARNING', 'UNFAVORABLE'],
    ['SIDEWAYS', 'NEUTRAL', 'NEUTRAL', 'NEUTRAL', 'NEUTRAL'],
  ];
  let values = examples;
  await page.addInitScript(() => sessionStorage.setItem('demo-session', 'yes'));
  await page.route('**/api/ai/settings', route => route.fulfill({ json: rows.map((row: { provider: string }) => ({ provider: row.provider, visible: true, enabled: true })) }));
  await page.route('**/api/products/00631L/analysis?*', route => route.fulfill({ json: rows.map((row: any, index: number) => ({ ...row, result: { ...row.result, ...Object.fromEntries(fields.map((key, i) => [key, values[index % values.length][i]])) } })) }));
  await page.goto('/products/00631L');
  const cards = page.locator('.analysis-grid .analysis-card');
  const assertBadge = async (index: number, field: string, label: string, tone: string) => {
    const badge = cards.nth(index).locator(`[data-status-field="${field}"]`);
    await expect(badge).toHaveText(label); await expect(badge).toHaveClass(new RegExp(`analysis-status--${tone}`));
    expect(await badge.evaluate(el => el.tagName)).toBe('SPAN');
    expect(await badge.getAttribute('role')).not.toBe('button');
    expect(await badge.evaluate(el => getComputedStyle(el).cursor)).toBe('default');
  };
  await assertBadge(0, 'marketRegime', '多頭', 'positive');
  await assertBadge(0, 'trend', '偏多', 'positive');
  await assertBadge(0, 'momentum', '正向', 'positive');
  await assertBadge(0, 'volume', '支持', 'positive');
  await assertBadge(0, 'riskReward', '偏有利', 'positive');
  await assertBadge(1, 'marketRegime', '空頭', 'negative');
  await assertBadge(1, 'trend', '偏空', 'negative');
  await assertBadge(1, 'momentum', '負向', 'negative');
  await assertBadge(1, 'volume', '警示', 'warning');
  await assertBadge(1, 'riskReward', '偏不利', 'negative');
  await assertBadge(2, 'marketRegime', '盤整', 'neutral');
  for (const field of fields.slice(1)) await assertBadge(2, field, '中性', 'neutral');
  for (const theme of ['light', 'dark']) {
    await page.evaluate(theme => { document.documentElement.dataset.theme = theme; localStorage.setItem('market-signal-theme', theme); }, theme);
    for (const [tone, redDominant] of [['positive', true], ['negative', false]] as const) {
      const rgb = await page.locator(`.analysis-grid .analysis-status--${tone}`).first().evaluate(el => getComputedStyle(el).color.match(/\d+/g)!.map(Number));
      expect(redDominant ? rgb[0] > rgb[1] : rgb[1] > rgb[0]).toBe(true);
    }
    const contrast = await page.locator('.analysis-status').evaluateAll(nodes => {
      const luminance = (css: string) => { const rgb = css.match(/\d+/g)!.slice(0, 3).map(Number).map(n => n / 255).map(n => n <= .04045 ? n / 12.92 : ((n + .055) / 1.055) ** 2.4); return .2126 * rgb[0] + .7152 * rgb[1] + .0722 * rgb[2]; };
      return nodes.map(node => { const css = getComputedStyle(node), a = luminance(css.color), b = luminance(css.backgroundColor); return (Math.max(a, b) + .05) / (Math.min(a, b) + .05); });
    });
    for (const ratio of contrast) expect(ratio).toBeGreaterThanOrEqual(4.5);
    await page.screenshot({ path: `test-results/status-badges-${theme}.png`, fullPage: true });
  }
  for (const [value, label, tone] of [['UNKNOWN', '資料不足', 'unknown'], ['PARTIAL', '資料部分不足', 'warning'], ['UNAVAILABLE', '無法判定', 'unknown'], ['MISSING', '未提供', 'unknown'], ['', '未提供', 'unknown'], ['FUTURE_STATE', '無法判定', 'unknown']]) {
    values = [fields.map(() => value)]; await page.reload();
    for (const field of fields) await assertBadge(0, field, value === 'UNKNOWN' && field === 'riskReward' ? '無法判定' : label, tone);
  }
  await page.setViewportSize({ width: 390, height: 900 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});
