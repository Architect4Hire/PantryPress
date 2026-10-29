import { defineConfig, devices } from '@playwright/test';

/**
 * Browser runs for the admin app (PP3.6). It serves its own development build on a port of its own, so it never meets
 * the Aspire-hosted dev server, and the showcase route exists only in that build (dev-routes.ts). `npm run e2e`.
 */
const port = 4310;

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.e2e.ts',
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 1 : 0,
  reporter: process.env['CI'] ? 'github' : 'list',
  use: {
    baseURL: `http://localhost:${port}`,
    trace: 'retain-on-failure',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
    // WebKit's engine, not Safari itself: Safari's own controls and screen reader are still a manual check.
    { name: 'webkit', use: { ...devices['Desktop Safari'] } },
  ],
  webServer: {
    command: `npx ng serve pantrypress-admin --port ${port}`,
    url: `http://localhost:${port}/showcase`,
    reuseExistingServer: !process.env['CI'],
    timeout: 180_000,
  },
});
