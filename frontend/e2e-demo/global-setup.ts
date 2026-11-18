import { chromium, request } from '@playwright/test'
import { API_URL, BASE_URL, IS_PROD } from './env'

/**
 * Проверка стенда до тестов: API жив, демо включено, фронт отдаёт /login. Прогрев Vite (первая
 * компиляция модулей), чтобы первые сценарии не ловили перезагрузку страницы.
 */
export default async function globalSetup() {
  // E2E_DEMO_SKIP_PRECHECK=1 — только для отладки селекторов на фронте с моками: без проверки API
  if (process.env.E2E_DEMO_SKIP_PRECHECK !== '1') await precheckApi()
  if (IS_PROD) return

  const browser = await chromium.launch()
  const page = await browser.newPage()
  for (const path of ['/login', '/demo', '/login', '/demo']) {
    await page.goto(`${BASE_URL}${path}`)
    await page.waitForLoadState('networkidle').catch(() => {})
  }
  await browser.close()
}

async function precheckApi() {
  const api = await request.newContext()
  const health = await api.get(API_URL.replace(/\/api$/, '') + '/health').catch((e: Error) => e)
  if (health instanceof Error || !health.ok()) {
    throw new Error(`API недоступен: ${API_URL.replace(/\/api$/, '')}/health. Поднимите API с Demo__Enabled=true (см. e2e-demo/README.md)`)
  }
  const status = await api.get(`${API_URL}/auth/demo/status`)
  const body = status.ok() ? ((await status.json()) as { enabled?: boolean }) : null
  if (!body?.enabled) {
    throw new Error(`GET ${API_URL}/auth/demo/status → ${status.status()} ${JSON.stringify(body)}: нужен Demo__Enabled=true`)
  }
  await api.dispose()
}
