import { expect, test, type Page } from '@playwright/test'

const PERSONAS = [
  'platform_admin',
  'fc_owner',
  'fc_manager',
  'fc_supervisor',
  'fc_account_manager',
  'fc_receiver',
  'fc_picker',
  'fc_packer',
  'seller_owner',
  'seller_manager',
  'pending',
] as const

async function enterPersona(page: Page, persona: (typeof PERSONAS)[number]) {
  await page.goto('/demo')
  await page.getByTestId(`demo-persona-${persona}`).click()
  if (persona === 'pending') {
    await expect(page).toHaveURL(/\/pending/)
  } else {
    await expect(page.getByTestId('demo-banner')).toBeVisible()
  }
}

test.describe('рантайм-демо на сборке VITE_USE_MOCKS=false', () => {
  test('карточки /demo видны без запроса статуса к API, кнопки на login и register ведут на /demo', async ({
    page,
  }) => {
    const statusCalls: string[] = []
    page.on('request', (request) => {
      if (request.url().includes('/auth/demo/status')) statusCalls.push(request.url())
    })

    await page.goto('/demo')
    await expect(page.getByRole('heading', { name: 'Попробуйте систему в демо' })).toBeVisible()
    await expect(page.getByText('личная песочница')).toBeVisible()
    await expect(page.getByText('03:00')).toHaveCount(0)
    for (const persona of PERSONAS) {
      await expect(page.getByTestId(`demo-persona-${persona}`)).toBeVisible()
    }
    expect(statusCalls).toEqual([])

    await page.goto('/login')
    await page.getByTestId('try-demo').click()
    await expect(page).toHaveURL(/\/demo/)

    await page.goto('/register')
    await page.getByTestId('try-demo').click()
    await expect(page).toHaveURL(/\/demo/)
  })

  test('11 ролей входят, баннер на месте, у AM нет «Заданий» и «Комплектации»', async ({ page }) => {
    for (const persona of PERSONAS) {
      await enterPersona(page, persona)
      if (persona === 'pending') {
        await expect(page.getByText(/проверк|модерац/i).first()).toBeVisible()
        continue
      }
      await expect(page.getByTestId('demo-banner')).toContainText('Сменить роль')
      await expect(page.getByTestId('demo-banner')).toContainText('Сбросить данные')
      await expect(page.getByTestId('demo-banner')).toContainText('Выйти')
    }

    await enterPersona(page, 'fc_account_manager')
    const nav = page.locator('aside nav, nav')
    await expect(nav.getByRole('link', { name: 'Задания' })).toHaveCount(0)
    await expect(nav.getByRole('link', { name: 'Комплектация' })).toHaveCount(0)

    await enterPersona(page, 'fc_manager')
    await expect(page.getByRole('link', { name: 'Задания' }).first()).toBeVisible()
    await expect(page.getByRole('link', { name: 'Комплектация' }).first()).toBeVisible()
  })

  test('перезагрузка оставляет демо; смена роли сохраняет данные; сброс возвращает seed', async ({ page }) => {
    await enterPersona(page, 'fc_owner')
    const marker = 'e2e-runtime-marker'
    await page.evaluate((name) => {
      const key = 'wms.mockdb.v2'
      const overlay = JSON.parse(localStorage.getItem(key) || '{}') as Record<
        string,
        { upserts: Record<string, { id: string; name?: string }>; deleted: string[] }
      >
      overlay.organizations = overlay.organizations ?? { upserts: {}, deleted: [] }
      overlay.organizations.upserts.org_e2e = { id: 'org_e2e', name }
      localStorage.setItem(key, JSON.stringify(overlay))
    }, marker)

    await page.reload()
    await expect(page.getByTestId('demo-banner')).toBeVisible()
    await expect(page.locator('body')).not.toContainText('Демо временно выключено')

    await page.getByRole('button', { name: 'Сменить роль' }).click()
    await expect(page).toHaveURL(/\/demo/)
    await page.getByTestId('demo-persona-fc_picker').click()
    await expect(page.getByTestId('demo-banner')).toContainText('Комплектовщик')
    const kept = await page.evaluate((name) => localStorage.getItem('wms.mockdb.v2')?.includes(name) === true, marker)
    expect(kept).toBe(true)

    await page.getByRole('button', { name: 'Сбросить данные' }).click()
    await page.locator('.p-confirmdialog-accept-button').click()
    await expect(page.getByTestId('demo-banner')).toBeVisible()
    const cleared = await page.evaluate((name) => localStorage.getItem('wms.mockdb.v2')?.includes(name) === true, marker)
    expect(cleared).toBe(false)
  })

  test('боевые /api не перехватываются моками; боевой токен не затирается', async ({ context, page }) => {
    await page.goto('/login')
    await page.evaluate(() => localStorage.setItem('wms.token', 'real-jwt-placeholder'))
    // Не перезагружаем: иначе init() с фейковым JWT без бэка покажет экран ошибки поверх /demo.
    await page.getByTestId('try-demo').click()
    await page.getByTestId('demo-persona-fc_owner').click()
    await expect(page.getByTestId('demo-banner')).toBeVisible()
    const stored = await page.evaluate(() => ({
      real: localStorage.getItem('wms.token'),
      demo: localStorage.getItem('wms.demo.token'),
      flag: sessionStorage.getItem('wms.demo.active'),
    }))
    expect(stored.real).toBe('real-jwt-placeholder')
    expect(stored.demo?.startsWith('mock.demo:')).toBe(true)
    expect(stored.flag).toBe('1')

    const realTab = await context.newPage()
    await realTab.goto('/login')
    const probe = await realTab.evaluate(async () => {
      const res = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: 'director@demo.ru', password: 'demo' }),
      })
      return { status: res.status, text: await res.text() }
    })
    expect(probe.text).not.toMatch(/mock\.demo:/)
    expect(probe.text).not.toMatch(/"accessToken"\s*:\s*"mock\./)
    expect(probe.status).not.toBe(200)

    const realKeys = await realTab.evaluate(() => ({
      token: localStorage.getItem('wms.token'),
      demoTab: sessionStorage.getItem('wms.demo.active'),
    }))
    expect(realKeys.token).toBe('real-jwt-placeholder')
    expect(realKeys.demoTab).toBeNull()

    await page.getByRole('button', { name: 'Выйти' }).click()
    await expect(page).toHaveURL(/\/login|\//)
    const afterExit = await page.evaluate(() => ({
      real: localStorage.getItem('wms.token'),
      flag: sessionStorage.getItem('wms.demo.active'),
    }))
    expect(afterExit.real).toBe('real-jwt-placeholder')
    expect(afterExit.flag).toBeNull()
  })

  test('в dist есть mockServiceWorker.js', async ({ request }) => {
    const res = await request.get('/mockServiceWorker.js')
    expect(res.ok()).toBeTruthy()
    const body = await res.text()
    expect(body).toContain('Mock Service Worker')
  })
})
