import { DEMO_HEADER } from './contract'
import { banner, enterDemo, expect, expectBanner, recordApiRequests, test } from './fixtures'
import { BASE_URL } from './env'

/**
 * Выборочная проверка после выкладки (SPEC-v2 §7 последний пункт, §11.4). Только чтение: вход в демо обслуживает MSW
 * в браузере (нагрузки на Postgres нет), боевые проверки — /health и /auth/me без токена. Прод-данные не меняются,
 * сидовые пароли не используются.
 *
 * Запуск только явно: E2E_DEMO_ALLOW_PROD=1 E2E_DEMO_BASE_URL=https://wms.korobfulfillment.ru npm run test:e2e:demo
 * Тесты с @prod-smoke-only на локальном стенде не запускаются. TgSearch проверяется вручную (контейнер на сервере).
 */
test.describe('Прод-смок демо v2 (SPEC-v2 §7, §11.4)', () => {
  test('вход в роль работает целиком в браузере, параллельно боевой /api отвечает живым сервером @prod-smoke', async ({ page, context }) => {
    await enterDemo(page, 'fc_owner')
    await expectBanner(page, 'fc_owner')

    const real = await context.newPage()
    const requests = recordApiRequests(real)
    await real.goto('/login')
    const res = await real.evaluate(async () => {
      const r = await fetch('/api/auth/me', { headers: { Accept: 'application/json' } })
      return r.status
    })
    expect(res, 'боевой /api/auth/me без токена — 401 от сервера').toBe(401)
    const health = await real.request.get(`${BASE_URL}/health`)
    expect(health.status()).toBe(200)
    expect(requests.filter((r) => r.headers()[DEMO_HEADER] !== undefined)).toEqual([])
    await expect(banner(real)).toHaveCount(0)
    await real.close()
  })

  test('mockServiceWorker.js отдаётся и не кэшируется надолго (§6) @prod-smoke @prod-smoke-only', async ({ request }) => {
    const res = await request.get(`${BASE_URL}/mockServiceWorker.js`)
    expect(res.status()).toBe(200)
    expect(res.headers()['content-type'] ?? '').toMatch(/javascript/)
    expect(res.headers()['cache-control'] ?? '').toMatch(/no-cache|no-store|max-age=0/)
  })

  test('лендинг korobfulfillment.ru жив и ведёт на /demo @prod-smoke @prod-smoke-only', async ({ page }) => {
    const res = await page.goto('https://korobfulfillment.ru')
    expect(res?.status()).toBe(200)
    const hrefs = await page.getByRole('link', { name: 'Попробовать демо', exact: true }).evaluateAll((els) => els.map((e) => e.getAttribute('href')))
    expect(hrefs.length).toBeGreaterThanOrEqual(1)
    for (const href of hrefs) expect(href).toBe('https://wms.korobfulfillment.ru/demo')
  })
})
