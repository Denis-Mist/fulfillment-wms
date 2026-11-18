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

  // Холодный Vite дооптимизирует зависимости и перезагружает страницу посреди сценария — обходим кабинеты заранее
  const browser = await chromium.launch()
  for (let attempt = 0; attempt < 2; attempt++) {
    let reloaded = false
    for (const [email, paths] of WARMUP) {
      const page = await browser.newPage()
      await page.route(`${API_URL}/**`, (route) =>
        route.continue({ headers: { ...route.request().headers(), 'x-forwarded-for': '10.76.0.1' } }),
      )
      for (const path of ['/demo', '/login']) {
        await page.goto(`${BASE_URL}${path}`)
        await page.waitForLoadState('networkidle').catch(() => {})
      }
      await page.locator('#email').fill(email)
      await page.locator('#password').fill('demo')
      await page.keyboard.press('Enter')
      await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 30_000 }).catch(() => (reloaded = true))
      for (const path of paths) {
        await page.goto(`${BASE_URL}${path}`)
        await page.waitForLoadState('networkidle').catch(() => {})
      }
      await page.close()
    }
    if (!reloaded) break
  }
  await browser.close()
}

const WARMUP: [string, string[]][] = [
  ['director@demo.ru', ['/fc', '/fc/orders', '/fc/clients', '/fc/billing', '/fc/products', '/fc/tasks', '/fc/picking', '/fc/analytics', '/fc/settings']],
  ['seller@demo.ru', ['/seller', '/seller/products', '/seller/integrations']],
  ['admin@demo.ru', ['/admin', '/admin/moderation', '/admin/users', '/admin/references']],
  ['picker@demo.ru', ['/work', '/work/scan']],
  ['owner1@pending.demo.ru', ['/pending']],
]

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
