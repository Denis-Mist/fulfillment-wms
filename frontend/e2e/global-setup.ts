import { chromium, type FullConfig } from '@playwright/test'

/**
 * Прогрев dev-сервера: при холодном старте Vite дооптимизирует зависимости и перезагружает
 * страницу, из-за чего первые тесты ловят «Failed to fetch dynamically imported module».
 * Один раз обходим все кабинеты последовательно, пока модули не перестанут пересобираться.
 */
const WARMUP: [string, string[]][] = [
  ['director@demo.ru', ['/fc', '/fc/orders', '/fc/warehouses', '/fc/analytics', '/fc/billing', '/fc/fbo', '/fc/clients', '/fc/stock', '/fc/inbound', '/fc/tasks', '/fc/returns', '/fc/picking', '/fc/operations', '/fc/staff', '/fc/products', '/fc/settings']],
  ['seller@demo.ru', ['/seller', '/seller/products', '/seller/fbo', '/seller/integrations', '/seller/fulfillments', '/seller/billing']],
  ['admin@demo.ru', ['/admin', '/admin/moderation', '/admin/organizations', '/admin/users', '/admin/references', '/admin/audit']],
  ['picker@demo.ru', ['/work', '/work/scan']],
]

export default async function globalSetup(config: FullConfig) {
  const baseURL = config.projects[0]!.use.baseURL!
  const browser = await chromium.launch()
  for (let attempt = 0; attempt < 3; attempt++) {
    let failed = false
    for (const [email, paths] of WARMUP) {
      const page = await browser.newPage()
      page.on('pageerror', () => (failed = true))
      await page.goto(`${baseURL}/register`)
      await page.goto(`${baseURL}/login`)
      await page.locator('#email').fill(email)
      await page.locator('#password').fill('demo')
      await page.keyboard.press('Enter')
      await page.waitForURL((u) => !u.pathname.startsWith('/login'), { timeout: 30_000 }).catch(() => (failed = true))
      for (const path of paths) {
        await page.goto(`${baseURL}${path}`)
        await page.waitForLoadState('networkidle').catch(() => {})
      }
      await page.close()
    }
    if (!failed) break
  }
  await browser.close()
}
