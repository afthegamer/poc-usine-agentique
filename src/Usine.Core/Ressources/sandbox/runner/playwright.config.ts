import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  timeout: 30_000,
  expect: { timeout: 5_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  reporter: [['json', { outputFile: '/tmp/usine-rapport-tests.json' }], ['line']],
  use: {
    baseURL: process.env.BASE_URL ?? 'http://localhost:8080',
    headless: true,
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
});
