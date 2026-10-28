import type { Page } from '@playwright/test'
import { expect, login, test, waitReady } from './fixtures'

/**
 * Ошибки и пустые данные подставляются подменой window.fetch поверх MSW (addInitScript),
 * режим включается ключом localStorage 'e2e.api' — код приложения не меняется.
 */
async function installApiOverride(page: Page) {
  await page.addInitScript(() => {
    const original = window.fetch.bind(window)
    window.fetch = async (input, init) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
      const method = (init?.method ?? (input instanceof Request ? input.method : 'GET')).toUpperCase()
      const mode = localStorage.getItem('e2e.api')
      if (!mode || method !== 'GET' || !/\/api\//.test(url) || /\/api\/auth\//.test(url)) return original(input, init)
      if (mode === '500')
        return new Response(JSON.stringify({ title: 'Internal Server Error', status: 500 }), {
          status: 500,
          headers: { 'Content-Type': 'application/problem+json' },
        })
      const res = await original(input, init)
      if (!res.ok) return res
      let data = await res.json()
      if (Array.isArray(data)) data = []
      else if (data && Array.isArray(data.items)) data = { ...data, items: [], total: 0, page: 1 }
      return new Response(JSON.stringify(data), { status: 200, headers: { 'Content-Type': 'application/json' } })
    }
  })
}
const setMode = (page: Page, mode: string | null) =>
  page.evaluate((m) => (m ? localStorage.setItem('e2e.api', m) : localStorage.removeItem('e2e.api')), mode)

const PAGES: [string, string[]][] = [
  ['director@demo.ru', ['/fc/orders', '/fc/inbound', '/fc/stock', '/fc/clients']],
  ['seller@demo.ru', ['/seller/products', '/seller/orders']],
  ['admin@demo.ru', ['/admin/organizations', '/admin/users']],
]

for (const [email, paths] of PAGES) {
  test(`500 → ErrorState и «Повторить» восстанавливает данные (${email})`, async ({ page }) => {
    await installApiOverride(page)
    await login(page, email)
    for (const path of paths) {
      await setMode(page, '500')
      await page.goto(path)
      await expect(page.getByText('Не удалось загрузить данные').first()).toBeVisible()
      await setMode(page, null)
      // На странице может быть несколько независимых блоков с ошибкой — повторяем каждый
      const retry = page.getByRole('button', { name: 'Повторить' })
      await expect(retry.first()).toBeVisible()
      await expect(async () => {
        if (await retry.count()) await retry.first().click({ timeout: 2_000 })
        await expect(page.getByText('Не удалось загрузить данные')).toHaveCount(0, { timeout: 2_000 })
      }).toPass({ timeout: 20_000 })
      await waitReady(page)
    }
  })

  test(`пустые данные → пустое состояние без ошибок (${email})`, async ({ page }) => {
    await installApiOverride(page)
    await login(page, email)
    await setMode(page, 'empty')
    for (const path of paths) {
      await page.goto(path)
      await waitReady(page)
      await expect(page.getByText('Не удалось загрузить данные')).toHaveCount(0)
      await expect(page.locator('main')).toContainText(/нет|не найден|пока|пусто/i)
    }
  })
}
