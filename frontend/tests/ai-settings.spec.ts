import { test, expect } from '@playwright/test';

test('provider settings load and save without analysis; models come from backend', async ({page})=>{
 const rows=[
  {provider:'openai',displayName:'GPT',enabled:true,configuredModel:'backend-gpt-model',actualModel:'reported-gpt-model',isMock:false},
  {provider:'deepseek',displayName:'DeepSeek',enabled:true,configuredModel:'backend-deep-model',actualModel:'reported-deep-model',isMock:false},
  {provider:'claude',displayName:'Claude',enabled:false,configuredModel:'mock-v1',actualModel:'mock-v1',isMock:true},
 ];
 let force=0,saves=0;
 await page.route('**/api/**',async route=>{
  const req=route.request();const path=new URL(req.url()).pathname;
  if(path.endsWith('/analysis/force')){force++;await route.abort();return;}
  if(path==='/api/ai/settings'&&req.method()==='GET'){await route.fulfill({json:rows});return;}
  if(path.startsWith('/api/ai/settings/')&&req.method()==='PUT'){
   const row=rows.find(x=>x.provider===path.split('/').pop())!;Object.assign(row,req.postDataJSON());saves++;
   await route.fulfill({status:204});return;
  }
  await route.abort();
 });
 await page.addInitScript(()=>sessionStorage.setItem('demo-session','yes'));
 await page.goto('/settings/ai');
 await expect(page.getByLabel('DeepSeek 設定模型')).toHaveValue('backend-deep-model');
 await expect(page.getByText(/reported-deep-model/)).toBeVisible();
 await page.getByLabel('啟用 GPT',{exact:true}).uncheck();
 await page.getByRole('button',{name:'儲存 GPT',exact:true}).click();
 await expect(page.getByRole('status')).toHaveText('GPT 設定已儲存');
 await page.getByLabel('DeepSeek 設定模型').fill('user-selected-model');
 await page.getByRole('button',{name:'儲存 DeepSeek',exact:true}).click();
 await expect(page.getByRole('status')).toHaveText('DeepSeek 設定已儲存');
 await page.reload();
 await expect(page.getByLabel('啟用 GPT',{exact:true})).not.toBeChecked();
 await expect(page.getByLabel('DeepSeek 設定模型')).toHaveValue('user-selected-model');
 await expect(page.getByLabel('Claude 設定模型')).toHaveAttribute('readonly','');
 expect(saves).toBe(2);expect(force).toBe(0);
 await page.setViewportSize({width:390,height:844});
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
});
