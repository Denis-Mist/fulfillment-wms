import { test as base, expect, type BrowserContext, type Locator, type Page, type Request, type Response } from '@playwright/test'
import { DEMO_FLAG_KEY, DEMO_HEADER, DEMO_HEADER_VALUE, DEMO_TOKEN_PREFIX, MOCK_TOKEN_PREFIX, TESTID, TEXT } from './contract'
import { API_PATH, REAL_API_URL } from './env'
import { PERSONAS, type Persona } from './personas'
import type { RealAccount } from './real-api'

/** Ожидаемые в негативных сценариях 4xx (неверный пароль) браузер пишет в консоль как «Failed to load resource» */
const IGNORED_CONSOLE = [/Failed to load resource/, /\[MSW\]/]

export const isApiUrl = (url: string) => {
  const u = new URL(url)
  return u.pathname.startsWith(`${API_PATH}/`) || url.startsWith(`${REAL_API_URL}/`)
}

/** Признаки демо-запроса: заголовок X-WMS-Demo или мок-токен в Authorization */
export function demoMarkers(req: Request): string | null {
  const h = req.headers()
  if (h[DEMO_HEADER] !== undefined) return `${DEMO_HEADER}: ${h[DEMO_HEADER]}`
  if ((h.authorization ?? '').startsWith(`Bearer ${MOCK_TOKEN_PREFIX}`)) return 'Authorization: Bearer mock.…'
  return null
}

/**
 * Страж SPEC-v2 §2.4, §3.3, §3.4: ни один демо-запрос (X-WMS-Demo / мок-токен) не должен уйти на реальный API.
 * - запрос, который сделал сам service worker (MSW `passthrough()`), с демо-признаками — утечка;
 * - запрос страницы, на который ответил не SW (SW не контролирует вкладку), с демо-признаками — утечка.
 * В Chromium Playwright видит сетевые запросы service worker на уровне контекста.
 */
export function guardDemoLeaks(context: BrowserContext, leaks: string[]) {
  context.on('request', (req) => {
    if (!req.serviceWorker() || !isApiUrl(req.url())) return
    const marker = demoMarkers(req)
    if (marker) leaks.push(`SW passthrough → реальный API: ${req.method()} ${req.url()} (${marker})`)
  })
  context.on('response', (res) => {
    const req = res.request()
    if (req.serviceWorker() || res.fromServiceWorker() || !isApiUrl(req.url())) return
    const marker = demoMarkers(req)
    if (marker) leaks.push(`без MSW → реальный API: ${req.method()} ${req.url()} → ${res.status()} (${marker})`)
  })
}

type Fixtures = {
  pageErrors: string[]
  demoLeaks: string[]
}

export const test = base.extend<Fixtures>({
  pageErrors: [
    async ({ page }, use) => {
      const errors: string[] = []
      page.on('pageerror', (e) => errors.push(`pageerror: ${e.message}`))
      page.on('console', (m) => {
        if (m.type() === 'error' && !IGNORED_CONSOLE.some((re) => re.test(m.text()))) errors.push(`console: ${m.text()}`)
      })
      await use(errors)
      expect(errors, 'ошибки в консоли/странице').toEqual([])
    },
    { auto: true },
  ],
  demoLeaks: [
    async ({ context }, use) => {
      const leaks: string[] = []
      guardDemoLeaks(context, leaks)
      await use(leaks)
      expect(leaks, 'демо-запросы, дошедшие до реального API').toEqual([])
    },
    { auto: true },
  ],
})

export { expect }

export const pathname = (page: Page) => new URL(page.url()).pathname
export const toast = (page: Page) => page.locator('.p-toast-message')

/** Страница загрузилась: нет видимых скелетонов */
export async function waitReady(page: Page) {
  await expect(page.locator('.p-skeleton:visible')).toHaveCount(0)
}

const esc = (s: string) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')

/** Карточка персоны на /demo (testid фронта) */
export function personaCard(page: Page, persona: Persona): Locator {
  return page.getByTestId(TESTID.persona(persona))
}

/** Все карточки ролей (в v2 в main есть ещё кнопка «Сбросить демо-данные» — её не считаем) */
export const personaCards = (page: Page) => page.locator('[data-testid^="demo-persona-"]')

/** Заголовок карточки совпадает с ожидаемым (сверка текстов с PERSONAS) */
export async function expectCardTitle(page: Page, persona: Persona) {
  await expect(personaCard(page, persona), `заголовок карточки ${persona}`).toHaveText(new RegExp(`^\\s*${esc(PERSONAS[persona].title)}`))
}

export async function openDemo(page: Page) {
  await page.goto('/demo')
  await expect(page.getByText(TEXT.demoTitle)).toBeVisible()
}

const isDemoLogin = (r: Response) => new URL(r.url()).pathname.endsWith('/auth/demo') && r.request().method() === 'POST'

/**
 * Вход в демо кликом по карточке (§2.1): MSW стартует в рантайме, POST /api/auth/demo обслуживает мок
 * (ответ от service worker, с заголовком X-WMS-Demo), затем кабинет персоны.
 */
export async function enterDemo(page: Page, persona: Persona) {
  if (pathname(page) !== '/demo') await openDemo(page)
  const resPromise = page.waitForResponse(isDemoLogin)
  await personaCard(page, persona).click()
  const res = await resPromise
  expect(res.request().postDataJSON(), 'тело POST /auth/demo').toEqual({ persona })
  expect(res.request().headers()[DEMO_HEADER], `${DEMO_HEADER} на POST /auth/demo`).toBe(DEMO_HEADER_VALUE)
  expect(res.fromServiceWorker(), 'POST /auth/demo обслужен MSW (service worker)').toBe(true)
  expect(res.status(), `POST /auth/demo ${persona}`).toBe(200)
  const body = (await res.json()) as { accessToken: string }
  expect(body.accessToken.startsWith(DEMO_TOKEN_PREFIX), 'мок-токен mock.demo:…').toBe(true)
  await expect.poll(() => pathname(page), { message: `кабинет ${persona}` }).toMatch(PERSONAS[persona].home)
  await waitReady(page)
  return body.accessToken
}

export const banner = (page: Page) => page.getByTestId(TESTID.banner).filter({ visible: true })

/** Видимая кнопка/ссылка (баннер, /demo) по точному имени */
export const visibleAction = (page: Page, name: string) =>
  page.getByRole('link', { name, exact: true }).or(page.getByRole('button', { name, exact: true })).filter({ visible: true })

/** §2.2: «Вы в демо-режиме как {роль} · Сменить роль · Сбросить данные · Выйти» */
export async function expectBanner(page: Page, persona: Persona) {
  await expect(banner(page).first()).toBeVisible()
  await expect(banner(page).first()).toContainText(TEXT.bannerPrefix)
  await expect(banner(page).first()).toContainText(PERSONAS[persona].bannerRole)
  for (const name of [TEXT.switchRole, TEXT.resetData, TEXT.logout]) {
    await expect(visibleAction(page, name).first(), `в баннере «${name}»`).toBeVisible()
  }
}

/** Подтверждение в диалоге, если UI его спрашивает (сброс, выход) */
export async function confirmIfAsked(page: Page) {
  const dialog = page.locator('.p-confirmdialog, .p-dialog').filter({ visible: true }).last()
  if (await dialog.isVisible({ timeout: 1500 }).catch(() => false)) {
    const accept = dialog.locator('.p-confirmdialog-accept-button, button.p-button:not(.p-button-secondary):not(.p-button-text)').last()
    await accept.click()
  }
}

/** Признак демо этой вкладки: sessionStorage (SPEC-v2 §12, Q-1) */
export const demoFlag = (page: Page) => page.evaluate((k) => sessionStorage.getItem(k), DEMO_FLAG_KEY)

/** Весь localStorage вкладки */
export const storage = (page: Page) => page.evaluate(() => ({ ...localStorage }) as Record<string, string>)

/** Ключ и значение демо-токена (ищем по значению mock.demo:…, имя ключа в §3.3 дано как «например») */
export async function demoTokenEntry(page: Page): Promise<[string, string] | undefined> {
  return Object.entries(await storage(page)).find(([, v]) => v.startsWith(DEMO_TOKEN_PREFIX))
}

/** Запросы страницы к /api (для проверки заголовка X-WMS-Demo) */
export function recordApiRequests(page: Page) {
  const list: Request[] = []
  page.on('request', (r) => isApiUrl(r.url()) && list.push(r))
  return list
}

const isRealPost = (path: string) => (r: Response) => new URL(r.url()).pathname.endsWith(path) && r.request().method() === 'POST'

/** Обычный вход формой /login; возвращает ответ POST /auth/login */
export async function loginUI(page: Page, email: string, password: string) {
  if (pathname(page) !== '/login') await page.goto('/login')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Пароль').fill(password)
  const res = page.waitForResponse(isRealPost('/auth/login'))
  await page.getByRole('button', { name: 'Войти', exact: true }).click()
  return res
}

/** Регистрация селлера формой /register (3 шага); возвращает ответ POST /auth/register */
export async function registerUI(page: Page, account: RealAccount) {
  await page.goto('/register')
  await page.getByRole('button', { name: /Я селлер/ }).click()
  await page.locator('#org-name').fill(account.organizationName)
  await page.locator('#org-inn').fill('7700000000')
  await page.locator('#org-city').fill('Москва')
  await page.getByRole('button', { name: 'Далее', exact: true }).click()
  await page.locator('#u-name').fill(account.fullName)
  await page.locator('#u-email').fill(account.email)
  await page.locator('#u-phone').fill('+7 900 000-00-00')
  await page.locator('#u-password').fill(account.password)
  // Оверлей-подсказка силы пароля перекрывает чекбокс — убираем фокус с поля
  await page.keyboard.press('Escape')
  await page.getByRole('heading', { name: 'Регистрация' }).click()
  await page.getByText('Согласен с условиями').click()
  const res = page.waitForResponse(isRealPost('/auth/register'))
  await page.getByRole('button', { name: 'Отправить заявку', exact: true }).click()
  return res
}

/** Пункты бокового меню кабинета (десктоп) */
export const sidebarLink = (page: Page, title: string) => page.locator('aside nav').getByRole('link', { name: title, exact: true })
export const sidebarLinks = (page: Page) => page.locator('aside nav').getByRole('link')
