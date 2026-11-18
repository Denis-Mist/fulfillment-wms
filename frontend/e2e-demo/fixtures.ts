import { test as base, expect, type Locator, type Page } from '@playwright/test'
import { nextClientIp, USE_XFF } from './api'
import { API_URL } from './env'
import { PERSONAS, type Persona } from './personas'

/** Ожидаемые в негативных сценариях ответы 4xx браузер пишет в консоль как «Failed to load resource» */
const IGNORED_CONSOLE = [/Failed to load resource/]

type Fixtures = {
  pageErrors: string[]
  /** IP клиента для партиции rate limit (X-Forwarded-For на запросы к API) */
  clientIp: string
}

export const test = base.extend<Fixtures>({
  clientIp: async ({}, use) => {
    await use(nextClientIp())
  },
  page: async ({ page, clientIp }, use) => {
    if (USE_XFF) {
      await page.route(`${API_URL}/**`, (route) =>
        route.continue({ headers: { ...route.request().headers(), 'x-forwarded-for': clientIp } }),
      )
    }
    await use(page)
  },
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
})

export { expect }

export const pathname = (page: Page) => new URL(page.url()).pathname
export const toast = (page: Page) => page.locator('.p-toast-message')

/** Ждём, пока страница загрузится: нет видимых скелетонов */
export async function waitReady(page: Page) {
  await expect(page.locator('.p-skeleton:visible')).toHaveCount(0)
}

/** Карточка персоны на /demo: кликабельный элемент с заголовком роли */
export function personaCard(page: Page, persona: Persona): Locator {
  const title = PERSONAS[persona].title.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  return page.getByRole('main').getByRole('button', { name: new RegExp(`^\\s*${title}(\\s|$)`) })
}

/** Все карточки ролей на /demo */
export const personaCards = (page: Page) => page.getByRole('main').getByRole('button')

export async function openDemo(page: Page) {
  await page.goto('/demo')
  await expect(page.getByText('Попробуйте систему в демо')).toBeVisible()
}

/** Вход через /demo кликом по карточке; ждёт ответ POST /auth/demo и уход с /demo */
export async function demoLoginUI(page: Page, persona: Persona) {
  if (pathname(page) !== '/demo') await openDemo(page)
  const res = page.waitForResponse((r) => r.url().endsWith('/auth/demo') && r.request().method() === 'POST')
  await personaCard(page, persona).click()
  const response = await res
  expect(response.request().postDataJSON(), 'тело POST /auth/demo').toEqual({ persona })
  expect(response.status(), `POST /auth/demo ${persona}`).toBe(200)
  await expect.poll(() => pathname(page), { message: `кабинет ${persona}` }).toMatch(PERSONAS[persona].home)
  await waitReady(page)
}

export const banner = (page: Page) => page.getByText(/Вы в демо-режиме как/).filter({ visible: true })

/** Видимые кнопка/ссылка (в баннере): «Сменить роль», «Выйти» */
export const visibleAction = (page: Page, name: string) =>
  page.getByRole('link', { name, exact: true }).or(page.getByRole('button', { name, exact: true })).filter({ visible: true })

export async function expectBanner(page: Page, persona: Persona) {
  await expect(banner(page).first()).toBeVisible()
  await expect(banner(page).first()).toContainText(PERSONAS[persona].bannerRole)
  await expect(visibleAction(page, 'Сменить роль').first()).toBeVisible()
  await expect(visibleAction(page, 'Выйти').first()).toBeVisible()
}

/** Обычный вход по паролю через форму /login */
export async function passwordLoginUI(page: Page, email: string, password = 'demo') {
  await page.goto('/login')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Пароль').fill(password)
  await page.getByRole('button', { name: 'Войти', exact: true }).click()
  await expect(page).not.toHaveURL(/\/login/)
  await waitReady(page)
}

/** Пункты бокового меню кабинета (десктоп) */
export const sidebarLink = (page: Page, title: string) => page.locator('aside nav').getByRole('link', { name: title, exact: true })
