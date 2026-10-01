import { test, expect } from '@playwright/test';
test('schedule can add edit remove disable and reload without analysis',async({page})=>{
 let row={enabled:true,times:['09:05'],timezone:'Asia/Taipei',tradingDayCheck:'08:30'};let calls=0;
 await page.route('**/api/**',async route=>{const req=route.request();if(new URL(req.url()).pathname!='/api/settings/analysis-schedule'){calls++;await route.abort();return;}if(req.method()==='PUT'){row={...row,...req.postDataJSON()};await route.fulfill({status:204});}else await route.fulfill({json:row});});
 await page.addInitScript(()=>sessionStorage.setItem('demo-session','yes'));await page.goto('/settings/schedule');
 await expect(page.getByRole('heading',{name:'\u5206\u6790\u6392\u7a0b',exact:true})).toBeVisible();
 const inputs=page.locator('input[type=time]');await expect(inputs).toHaveCount(1);
 await page.locator('button[type=button]').filter({hasText:/./}).first().click();await expect(inputs).toHaveCount(0);
 await page.locator('button[type=button]').last().click();await expect(inputs).toHaveCount(1);await inputs.first().fill('11:15');
 await page.locator('input[type=checkbox]').uncheck();await page.locator('button.primary').click();await expect(page.getByRole('status')).toHaveText('\u5206\u6790\u6392\u7a0b\u5df2\u5132\u5b58');
 await page.reload();await expect(inputs.first()).toHaveValue('11:15');await expect(page.locator('input[type=checkbox]')).not.toBeChecked();expect(calls).toBe(0);
 await page.setViewportSize({width:390,height:844});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
});

test('real schedule API saves edits without triggering providers',async({page})=>{
 test.skip(!process.env.SCHEDULE_REAL_API_URL,'Set isolated backend URL for real API check');let calls=0;
 const reset=await page.request.put(process.env.SCHEDULE_REAL_API_URL+'/api/settings/analysis-schedule',{headers:{'X-Market-Signal':'web'},data:{enabled:true,times:['09:05','10:05','12:05','13:05']}});expect(reset.ok()).toBe(true);
 await page.route('**/api/**',async route=>{const path=new URL(route.request().url()).pathname;if(path!='/api/settings/analysis-schedule'){calls++;await route.abort();return;}const response=await route.fetch({url:process.env.SCHEDULE_REAL_API_URL+path});await route.fulfill({response});});
 await page.addInitScript(()=>sessionStorage.setItem('demo-session','yes'));await page.goto('/settings/schedule');
 await page.getByLabel('\u555f\u7528\u81ea\u52d5\u5206\u6790',{exact:true}).uncheck();await page.getByLabel('\u5206\u6790\u6642\u9593 1',{exact:true}).fill('11:17');await page.getByRole('button',{name:'\u5132\u5b58\u6392\u7a0b',exact:true}).click();await expect(page.getByRole('status')).toHaveText('\u5206\u6790\u6392\u7a0b\u5df2\u5132\u5b58');
 await page.reload();await expect(page.getByLabel('\u555f\u7528\u81ea\u52d5\u5206\u6790',{exact:true})).not.toBeChecked();await expect(page.getByLabel('\u5206\u6790\u6642\u9593 2',{exact:true})).toHaveValue('11:17');expect(calls).toBe(0);
});
