import { defineConfig, devices } from '@playwright/test'

const PORT = Number(process.env.E2E_RUNTIME_PORT ?? 4177)
const CI = !!process.env.CI

/**
 * Production-like контур: сборка VITE_USE_MOCKS=false, MSW стартует только после входа в /demo.
 * Запуск: npm run test:e2e:runtime
 */
export default defineConfig({
  testDir: './e2e-runtime',
  fullyParallel: false,
  forbidOnly: CI,
  retries: 0,
  workers: 1,
  timeout: 90_000,
  expect: { timeout: 20_000 },
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report-runtime' }]],
  outputDir: 'node_modules/.cache/e2e-runtime/test-results',
  use: {
    baseURL: `http://127.0.0.1:${PORT}`,
    locale: 'ru-RU',
    timezoneId: 'Europe/Moscow',
    viewport: { width: 1440, height: 900 },
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } } }],
  webServer: {
    command: `VITE_USE_MOCKS=false VITE_API_BASE_URL=/api npx vite build --outDir dist-runtime && npx vite preview --host 127.0.0.1 --port ${PORT} --strictPort --outDir dist-runtime`,
    url: `http://127.0.0.1:${PORT}/login`,
    reuseExistingServer: !CI,
    timeout: 180_000,
  },
})
