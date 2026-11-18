import { defineConfig, devices } from '@playwright/test'
import { API_URL, BASE_URL, IS_PROD, PORT } from './e2e-demo/env'

/**
 * E2E демо-режима на РЕАЛЬНОМ бэкенде (SPEC демо-режима §4.3). Отдельно от `playwright.config.ts`
 * (моки, CI `frontend-tests`): свой testDir `e2e-demo`, запуск — `npm run test:e2e:demo`.
 *
 * Нужен поднятый API с `Demo__Enabled=true` (по умолчанию http://127.0.0.1:5080/api, env `E2E_DEMO_API_URL`).
 * Фронт: Vite с `VITE_USE_MOCKS=false` поднимается сам на `E2E_DEMO_PORT` (по умолчанию 5173 — разрешён в CORS API)
 * либо берётся готовый по `E2E_DEMO_BASE_URL`.
 * Против прода (korobfulfillment.ru) запускаются только тесты с тегом @prod-smoke и только при E2E_DEMO_ALLOW_PROD=1.
 */
const external = !!process.env.E2E_DEMO_BASE_URL

export default defineConfig({
  testDir: './e2e-demo',
  testMatch: /.*\.spec\.ts$/,
  globalSetup: './e2e-demo/global-setup.ts',
  // Общая БД и лимит POST /auth/demo по IP — гоняем последовательно
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  timeout: 60_000,
  expect: { timeout: 15_000 },
  grep: IS_PROD ? /@prod-smoke/ : undefined,
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report-demo' }]],
  // Артефакты (трейсы пишутся по ходу теста) — вне отслеживаемых Vite файлов, иначе dev-сервер
  // перезагружает страницу посреди сценария. node_modules Vite не наблюдает.
  outputDir: 'node_modules/.cache/e2e-demo/test-results',
  metadata: { apiUrl: API_URL },
  use: {
    baseURL: BASE_URL,
    locale: 'ru-RU',
    timezoneId: 'Europe/Moscow',
    viewport: { width: 1440, height: 900 },
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'demo-chromium', use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } } }],
  webServer: external
    ? undefined
    : {
        command: `npx vite --host 127.0.0.1 --port ${PORT} --strictPort`,
        url: `${BASE_URL}/login`,
        // Не переиспользуем чужой dev-сервер: на том же порту может крутиться Vite с моками
        reuseExistingServer: process.env.E2E_DEMO_REUSE_SERVER === '1',
        timeout: 120_000,
        env: { VITE_USE_MOCKS: 'false', VITE_API_BASE_URL: API_URL },
      },
})
