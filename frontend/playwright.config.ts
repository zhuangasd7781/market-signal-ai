import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './tests', fullyParallel: false, workers: 1,
  use: { baseURL: process.env.TEST_BASE_URL ?? 'http://localhost:8080', browserName: 'chromium', channel: 'msedge', screenshot: 'only-on-failure' },
});
