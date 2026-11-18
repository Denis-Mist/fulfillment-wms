import { DEMO_FLAG_KEY, DEMO_HEADER, DEMO_TOKEN_PREFIX, looksLikeJwt, MOCKDB_KEY, REAL_SESSION_KEYS, REAL_TOKEN_KEY, TEXT } from './contract'
import {
  banner,
  demoFlag,
  demoTokenEntry,
  enterDemo,
  expect,
  loginUI,
  openDemo,
  pathname,
  recordApiRequests,
  registerUI,
  storage,
  test,
  visibleAction,
  waitReady,
} from './fixtures'
import { loginReal, meReal, newAccount, realApi, registerReal } from './real-api'
import type { APIRequestContext, Page } from '@playwright/test'

/**
 * Реальная WMS и демо одновременно (SPEC-v2 §3.3, §3.4, §7). Учётки реального API создаются регистрацией на стенде
 * тестов (уникальный email, одноразовый пароль) — мок-БД о них не знает, поэтому ответ 200 + JWT возможен только от
 * настоящего бэкенда.
 */
let api: APIRequestContext
test.beforeEach(async () => {
  api = await realApi()
})
test.afterEach(async () => {
  await api.dispose()
})

const noDemoHeader = (page: Page) => {
  const requests = recordApiRequests(page)
  return () => requests.filter((r) => r.headers()[DEMO_HEADER] !== undefined).map((r) => `${r.method()} ${r.url()}`)
}

async function demoLogout(page: Page) {
  await visibleAction(page, TEXT.logout).first().click()
}

test.describe('Выход из демо → реальный API (SPEC-v2 §2.2, §3.3, §7)', () => {
  test('«Выйти»: /login, флаг и демо-сессия сняты, кабинет снова требует входа', async ({ page }) => {
    await enterDemo(page, 'fc_picker')
    await demoLogout(page)
    await expect.poll(() => pathname(page)).toBe('/login')
    await expect(banner(page)).toHaveCount(0)
    const ls = await storage(page)
    expect(await demoFlag(page), `${DEMO_FLAG_KEY} снят (sessionStorage)`).toBeNull()
    expect(ls[DEMO_FLAG_KEY], `${DEMO_FLAG_KEY} не появился в localStorage`).toBeUndefined()
    expect(Object.values(ls).filter((v) => v.startsWith(DEMO_TOKEN_PREFIX)), 'демо-токен удалён').toEqual([])
    expect(ls[REAL_TOKEN_KEY] ?? '', 'в боевой ключ мок-токен не попал').toBe('')
    await page.goto('/work')
    await expect(page).toHaveURL(/\/login/)
  })

  test('«Выйти» с экрана /pending', async ({ page }) => {
    await enterDemo(page, 'pending')
    await demoLogout(page)
    await expect.poll(() => pathname(page)).toBe('/login')
    expect(Object.values(await storage(page)).filter((v) => v.startsWith(DEMO_TOKEN_PREFIX))).toEqual([])
  })

  test('после выхода неверный пароль → настоящий 401, верный → JWT реального API', async ({ page }) => {
    const { account } = await registerReal(api, newAccount('login'))
    await enterDemo(page, 'fc_owner')
    await demoLogout(page)
    await expect.poll(() => pathname(page)).toBe('/login')
    const demoHeaders = noDemoHeader(page)

    const bad = await loginUI(page, account.email, `${account.password}-wrong`)
    expect(bad.status(), 'неверный пароль').toBe(401)
    expect(pathname(page)).toBe('/login')

    const ok = await loginUI(page, account.email, account.password)
    expect(ok.status(), 'верный пароль: мок о пользователе не знает — 200 только от реального API').toBe(200)
    const body = (await ok.json()) as { accessToken: string; user: { email: string; isDemo?: boolean } }
    expect(looksLikeJwt(body.accessToken), 'JWT, а не mock.…').toBe(true)
    expect(body.user.isDemo ?? false).toBe(false)
    await expect.poll(() => pathname(page)).not.toBe('/login')
    await expect(banner(page)).toHaveCount(0)
    expect(demoHeaders(), `запросы с ${DEMO_HEADER} после выхода`).toEqual([])
    expect((await meReal(api, body.accessToken)).body?.email).toBe(account.email)
  })

  test('после выхода регистрация идёт в реальный API (usr_r_* в БД стенда), не в localStorage моков', async ({ page }) => {
    await enterDemo(page, 'seller_owner')
    await demoLogout(page)
    await expect.poll(() => pathname(page)).toBe('/login')
    const demoHeaders = noDemoHeader(page)
    const account = newAccount('register')

    const res = await registerUI(page, account)
    expect([200, 201]).toContain(res.status())
    const body = (await res.json()) as { accessToken: string; user: { id: string } }
    expect(looksLikeJwt(body.accessToken), 'JWT реального API').toBe(true)
    expect(body.user.id).toMatch(/^usr_r_/)
    await expect.poll(() => pathname(page)).toBe('/pending')
    await expect(banner(page)).toHaveCount(0)
    expect(demoHeaders()).toEqual([])

    // Пользователь есть на сервере: реальный логин теми же данными
    const login = await loginReal(api, account.email, account.password)
    expect(login.status, 'логин зарегистрированного пользователя в реальном API').toBe(200)
    expect(login.body?.user.id).toBe(body.user.id)
    // …и его нет в мок-БД браузера
    const ls = await storage(page)
    expect(ls[MOCKDB_KEY] ?? '', 'email не попал в overlay моков').not.toContain(account.email)
    expect(JSON.stringify(ls)).not.toContain(account.password)
  })
})

test.describe('Боевая сессия не затирается демо (SPEC-v2 §3.3, §7)', () => {
  test('вход в демо не трогает wms.token; «Выйти» восстанавливает реальную сессию через живой /auth/me', async ({ page }) => {
    const { account } = await registerReal(api, newAccount('session'))
    const login = await loginUI(page, account.email, account.password)
    expect(login.status()).toBe(200)
    await expect.poll(() => pathname(page)).toBe('/pending')
    const before = await storage(page)
    expect(looksLikeJwt(before[REAL_TOKEN_KEY]), 'боевой JWT').toBe(true)

    await openDemo(page)
    await enterDemo(page, 'fc_owner')
    const during = await storage(page)
    for (const key of REAL_SESSION_KEYS) expect(during[key] ?? '', `${key} во время демо`).toBe(before[key] ?? '')
    const demo = await demoTokenEntry(page)
    expect(demo?.[0], 'демо-токен лежит в отдельном ключе').not.toBe(REAL_TOKEN_KEY)

    await demoLogout(page)
    // §12 Q-2: «Выйти» ведёт на /login, а при живой боевой сессии /login сам перекидывает в её кабинет (/pending у новой организации)
    await expect.poll(() => pathname(page)).toBe('/pending')
    expect(await demoFlag(page)).toBeNull()
    const after = await storage(page)
    expect(after[REAL_TOKEN_KEY], 'боевой токен восстановлен').toBe(before[REAL_TOKEN_KEY])

    // Реальная сессия жива: кабинет открывается без входа, /auth/me — от реального API
    const demoHeaders = noDemoHeader(page)
    const me = page.waitForResponse((r) => new URL(r.url()).pathname.endsWith('/auth/me'))
    await page.goto('/login')
    const meRes = await me
    expect(meRes.status()).toBe(200)
    expect(((await meRes.json()) as { email: string }).email).toBe(account.email)
    // /login при живой боевой сессии перекидывает в кабинет (Q-2)
    await expect.poll(() => pathname(page)).toBe('/pending')
    await expect(banner(page)).toHaveCount(0)
    expect(demoHeaders()).toEqual([])
  })
})

test.describe('Две вкладки одного origin (SPEC-v2 §3.4, §7)', () => {
  test('вкладка с реальной сессией продолжает ходить в живой API, пока в соседней демо', async ({ page, context }) => {
    const { account } = await registerReal(api, newAccount('tabs'))
    // B — боевая вкладка, вошла до того, как соседняя открыла демо
    const real = page
    expect((await loginUI(real, account.email, account.password)).status()).toBe(200)
    await expect.poll(() => pathname(real)).toBe('/pending')
    const realDemoHeaders = noDemoHeader(real)

    const demo = await context.newPage()
    await enterDemo(demo, 'fc_owner')
    await expect(banner(demo).first()).toBeVisible()

    // Действие в боевой вкладке без перезагрузки: «Проверить статус» → /auth/me
    const me = real.waitForResponse((r) => new URL(r.url()).pathname.endsWith('/auth/me'))
    await real.getByRole('button', { name: 'Проверить статус', exact: true }).click()
    const meRes = await me
    expect(meRes.status(), 'боевой /auth/me').toBe(200)
    expect(((await meRes.json()) as { email: string }).email, 'ответ про реального пользователя, не из моков').toBe(account.email)
    // Прямой запрос боевым токеном из боевой вкладки — тоже в реальный API
    const token = (await storage(real))[REAL_TOKEN_KEY]
    const direct = await real.evaluate(async (t) => {
      const r = await fetch('/api/auth/me', { headers: { Authorization: `Bearer ${t}`, Accept: 'application/json' } })
      return { status: r.status, body: await r.text() }
    }, token)
    expect(direct.status).toBe(200)
    expect(direct.body).toContain(account.email)
    await expect(banner(real)).toHaveCount(0)
    expect(realDemoHeaders(), `запросы боевой вкладки с ${DEMO_HEADER}`).toEqual([])

    // Демо-вкладка при этом живёт своей жизнью
    await demo.reload()
    await waitReady(demo)
    await expect(banner(demo).first()).toBeVisible()
    await demo.close()
  })

  test('пока в соседней вкладке демо, логин во второй вкладке идёт в реальный API', async ({ page, context }) => {
    const { account } = await registerReal(api, newAccount('tabs-login'))
    await enterDemo(page, 'seller_owner')

    const second = await context.newPage()
    const demoHeaders = noDemoHeader(second)
    await second.goto('/login')
    // Флаг демо — в sessionStorage (§12 Q-1): новая вкладка боевая, баннера нет
    expect(await demoFlag(second), 'флаг в соседней вкладке').toBeNull()
    await expect(banner(second)).toHaveCount(0)
    const res = await loginUI(second, account.email, account.password)
    expect(res.status(), 'логин реального пользователя (мок его не знает)').toBe(200)
    expect(looksLikeJwt(((await res.json()) as { accessToken: string }).accessToken)).toBe(true)
    expect(demoHeaders(), `запросы с ${DEMO_HEADER} из боевой вкладки`).toEqual([])
    await second.close()
  })
})
