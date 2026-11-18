import { defineConfig, devices } from '@playwright/test'
import { API_TARGET, BASE_URL, IS_PROD, PORT, REAL_API_URL } from './e2e-demo/env'

/**
 * E2E демо-режима v2 (SPEC-v2 §7, §11.4): демо целиком в браузере (MSW + localStorage), реальная WMS — рядом.
 * Отдельно от `playwright.config.ts` (моки при VITE_USE_MOCKS=true, CI `frontend-tests` — не трогаем).
 *
 * Фронт — production-like: `vite build` с `VITE_USE_MOCKS=false`, `VITE_API_BASE_URL=/api` (как deploy/server-deploy.sh)
 * и `vite preview` с прокси `/api` → реальный API стенда (`E2E_DEMO_API_TARGET`, по умолчанию http://127.0.0.1:5080).
 * Готовый фронт — через `E2E_DEMO_BASE_URL` (тогда сборка не запускается).
 *
 * Прод (korobfulfillment.ru): только тесты @prod-smoke и только при E2E_DEMO_ALLOW_PROD=1 (иначе env.ts бросает ошибку).
 */
const external = !!process.env.E2E_DEMO_BASE_URL
const OUT_DIR = 'node_modules/.cache/e2e-demo/dist'

export default defineConfig({
  testDir: './e2e-demo',
  testMatch: /.*\.spec\.ts$/,
  globalSetup: './e2e-demo/global-setup.ts',
  // Сценарии с реальным API регистрируют пользователей; гоняем последовательно, чтобы не упираться в rate limit
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  timeout: 60_000,
  expect: { timeout: 15_000 },
  grep: IS_PROD ? /@prod-smoke/ : undefined,
  grepInvert: IS_PROD ? undefined : /@prod-smoke-only/,
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report-demo' }]],
  outputDir: 'node_modules/.cache/e2e-demo/test-results',
  metadata: { apiTarget: API_TARGET, realApiUrl: REAL_API_URL },
  use: {
    baseURL: BASE_URL,
    locale: 'ru-RU',
    timezoneId: 'Europe/Moscow',
    viewport: { width: 1440, height: 900 },
    // Демо держится на service worker — не блокируем
    serviceWorkers: 'allow',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'demo-v2-chromium', use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } } }],
  webServer: external
    ? undefined
    : {
        command:
          `npx vite build --config vite.e2e-demo.config.ts --outDir ${OUT_DIR} --emptyOutDir && ` +
          `npx vite preview --config vite.e2e-demo.config.ts --outDir ${OUT_DIR} --host 127.0.0.1 --port ${PORT} --strictPort`,
        url: `${BASE_URL}/login`,
        reuseExistingServer: process.env.E2E_DEMO_REUSE_SERVER === '1',
        timeout: 240_000,
        env: { VITE_USE_MOCKS: 'false', VITE_API_BASE_URL: '/api', E2E_DEMO_API_TARGET: API_TARGET },
      },
})
