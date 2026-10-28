import { test as base, expect, type Page } from '@playwright/test'

export const ACCOUNTS = {
  fc_owner: 'director@demo.ru',
  fc_manager: 'manager@demo.ru',
  fc_supervisor: 'staff8@fc1.demo.ru',
  fc_picker: 'picker@demo.ru',
  fc_receiver: 'staff1@fc1.demo.ru',
  fc_packer: 'staff6@fc1.demo.ru',
  seller_owner: 'seller@demo.ru',
  seller_manager: 'manager@ud.demo.ru',
  platform_admin: 'admin@demo.ru',
  pending: 'owner1@pending.demo.ru',
} as const
export type Role = keyof typeof ACCOUNTS

/** Ответы 4xx/5xx от моков браузер пишет в консоль как «Failed to load resource» — это ожидаемо в негативных сценариях */
const IGNORED_CONSOLE = [/Failed to load resource/]

type Fixtures = {
  /** Ошибки страницы (pageerror + console.error) — проверяются автоматически после каждого теста */
  pageErrors: string[]
  loginAs: (role: Role) => Promise<void>
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
  loginAs: async ({ page }, use) => {
    await use(async (role) => {
      await login(page, ACCOUNTS[role])
    })
  },
})

/**
 * Каждый тест — новый контекст браузера, поэтому localStorage (сессия и оверлей мок-БД wms.mockdb.v2) пуст.
 * Дополнительно очищаем его явно при первом заходе.
 */
export async function login(page: Page, email: string, password = 'demo') {
  await page.goto('/login')
  await page.evaluate(() => localStorage.clear())
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Пароль').fill(password)
  await page.getByRole('button', { name: 'Войти', exact: true }).click()
  await expect(page).not.toHaveURL(/\/login/)
  await waitReady(page)
}

/** Ждём, пока страница загрузится: основная область видна и нет видимых скелетонов загрузки */
export async function waitReady(page: Page) {
  await expect(page.getByRole('main').first()).toBeVisible()
  await expect(page.locator('main .p-skeleton:visible')).toHaveCount(0)
}

export const dialog = (page: Page) => page.locator('.p-dialog').last()
export const toast = (page: Page) => page.locator('.p-toast-message')

export { expect }
