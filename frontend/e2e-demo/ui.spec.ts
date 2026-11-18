import { apiContext, call, passwordSession } from './api'
import { API_URL } from './env'
import {
  banner,
  demoLoginUI,
  expect,
  expectBanner,
  openDemo,
  passwordLoginUI,
  pathname,
  personaCard,
  personaCards,
  sidebarLink,
  test,
  toast,
  visibleAction,
  waitReady,
} from './fixtures'
import { ALL_PERSONAS, PERSONAS, UI_PERSONAS, type Persona } from './personas'

/** Ещё один раздел кабинета: баннер нескрываемый и виден не только на главной */
const SECOND_PAGE: Partial<Record<Persona, string>> = {
  platform_admin: '/admin/users',
  fc_owner: '/fc/orders',
  fc_account_manager: '/fc/clients',
  fc_picker: '/work/scan',
  seller_owner: '/seller/products',
}

test.describe('Вход в демо: /login и /demo (§1.1–1.3)', () => {
  test('на /login есть «Попробовать демо» → /demo, сетки с паролем нет @prod-smoke', async ({ page }) => {
    await page.goto('/login')
    await expect(page.getByText('Аккаунт не нужен — выберите роль и посмотрите систему')).toBeVisible()
    // Сетка быстрого входа с паролем `demo` — только при VITE_USE_MOCKS=true
    await expect(page.getByText(/войдите как демо-пользователь/i)).toHaveCount(0)
    await expect(page.getByText(/director@demo\.ru|парол[ья]\s*[:—-]?\s*demo\b/i)).toHaveCount(0)
    await page
      .getByRole('button', { name: 'Попробовать демо', exact: true })
      .or(page.getByRole('link', { name: 'Попробовать демо', exact: true }))
      .first()
      .click()
    await expect.poll(() => pathname(page)).toBe('/demo')
  })

  test('/demo: заголовок, подзаголовок и ровно 11 карточек ролей, без паролей @prod-smoke', async ({ page }) => {
    await openDemo(page)
    await expect(page.getByText(/Выберите роль — войдёте сразу, без пароля/)).toBeVisible()
    await expect(personaCards(page)).toHaveCount(11)
    for (const persona of ALL_PERSONAS) {
      await expect(personaCard(page, persona), `карточка ${persona}`).toHaveCount(1)
      await expect(personaCard(page, persona)).toBeVisible()
    }
    await expect(page.getByText(/парол[ья]\s*[:—-]?\s*demo/i)).toHaveCount(0)
  })

  test('/demo адаптивен: на 390px все 11 карточек доступны, горизонтального скролла нет', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await openDemo(page)
    await expect(personaCards(page)).toHaveCount(11)
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
    expect(overflow).toBeLessThanOrEqual(1)
  })

  test('демо выключено (status enabled=false) — заглушка, без запроса логина', async ({ page }) => {
    const posts: string[] = []
    page.on('request', (r) => r.url().endsWith('/auth/demo') && r.method() === 'POST' && posts.push(r.url()))
    await page.route(`${API_URL}/auth/demo/status`, (route) => route.fulfill({ json: { enabled: false } }))
    await page.goto('/demo')
    await expect(page.getByText('Демо временно выключено')).toBeVisible()
    await expect(personaCards(page).filter({ hasText: PERSONAS.fc_owner.title })).toHaveCount(0)
    expect(posts).toEqual([])
  })

  for (const [status, text] of [
    [429, 'Слишком много попыток, попробуйте позже'],
    [503, 'Демо-данные обновляются, подождите минуту'],
  ] as const) {
    test(`ответ ${status} на POST /auth/demo — понятное сообщение, остаёмся на /demo`, async ({ page }) => {
      await page.route(`${API_URL}/auth/demo`, (route) =>
        route.request().method() === 'POST'
          ? route.fulfill({
              status,
              contentType: 'application/problem+json',
              headers: status === 503 ? { 'Retry-After': '60' } : {},
              json: { type: 'about:blank', title: text, status },
            })
          : route.fallback(),
      )
      await openDemo(page)
      await personaCard(page, 'fc_owner').click()
      await expect(page.getByText(text).first()).toBeVisible()
      expect(pathname(page)).toBe('/demo')
      await expect(page.evaluate(() => localStorage.getItem('wms.token') ?? '')).resolves.toBe('')
    })
  }
})

test.describe('Вход по ролям: верный кабинет и баннер (§1.4, §4.3)', () => {
  for (const persona of UI_PERSONAS) {
    test(`${persona}: кабинет ${PERSONAS[persona].home} и баннер «${PERSONAS[persona].bannerRole.source}»`, async ({ page }) => {
      await demoLoginUI(page, persona)
      await expectBanner(page, persona)
      // Пароль нигде не сохраняется
      const storage = await page.evaluate(() => JSON.stringify(localStorage))
      expect(storage).not.toMatch(/"demo"|password/i)

      const second = SECOND_PAGE[persona]
      if (second) {
        await page.goto(second)
        await waitReady(page)
        expect(pathname(page)).toBe(second)
        await expectBanner(page, persona)
      }
      // После перезагрузки признак демо берётся из /auth/me
      await page.reload()
      await waitReady(page)
      await expectBanner(page, persona)
    })
  }

  test('fc_picker на мобильном (ТСД, 390px): баннер и его действия видны', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await demoLoginUI(page, 'fc_picker')
    await expectBanner(page, 'fc_picker')
    const box = await banner(page).first().boundingBox()
    expect(box && box.x >= 0 && box.x + box.width <= 391).toBeTruthy()
  })
})

test.describe('Сменить роль и Выйти (§1.4)', () => {
  test('«Сменить роль» → /demo без разлогина, новая роль заменяет сессию', async ({ page }) => {
    await demoLoginUI(page, 'fc_owner')
    const before = await page.evaluate(() => localStorage.getItem('wms.token'))
    await visibleAction(page, 'Сменить роль').first().click()
    await expect.poll(() => pathname(page)).toBe('/demo')
    await expect(personaCards(page)).toHaveCount(11)
    await demoLoginUI(page, 'seller_owner')
    await expectBanner(page, 'seller_owner')
    const after = await page.evaluate(() => localStorage.getItem('wms.token'))
    expect(after).toBeTruthy()
    expect(after).not.toBe(before)
    // Старый кабинет ФЦ больше не доступен
    await page.goto('/fc/orders')
    await expect.poll(() => pathname(page)).toMatch(/^\/seller/)
  })

  test('«Выйти» → /login, токен удалён, кабинет снова требует входа', async ({ page }) => {
    await demoLoginUI(page, 'fc_picker')
    await visibleAction(page, 'Выйти').first().click()
    await expect.poll(() => pathname(page)).toBe('/login')
    expect(await page.evaluate(() => localStorage.getItem('wms.token') ?? '')).toBe('')
    await expect(banner(page)).toHaveCount(0)
    await page.goto('/work')
    await expect(page).toHaveURL(/\/login/)
  })

  test('«Выйти» с экрана /pending', async ({ page }) => {
    await demoLoginUI(page, 'pending')
    await visibleAction(page, 'Выйти').first().click()
    await expect.poll(() => pathname(page)).toBe('/login')
    expect(await page.evaluate(() => localStorage.getItem('wms.token') ?? '')).toBe('')
  })
})

test.describe('Менеджер по клиентам (§3)', () => {
  test('в меню нет «Задания»/«Комплектация» и закрытых разделов, есть коммерческие', async ({ page }) => {
    await demoLoginUI(page, 'fc_account_manager')
    for (const title of ['Задания', 'Комплектация', 'Склады и ячейки', 'Аналитика и прогнозы', 'Сотрудники и смены', 'Настройки']) {
      await expect(sidebarLink(page, title), `пункт «${title}» скрыт`).toHaveCount(0)
    }
    for (const title of ['Обзор', 'Поставки', 'Возвраты', 'Остатки', 'Заказы', 'Клиенты и договоры', 'Товары клиентов', 'Биллинг']) {
      await expect(sidebarLink(page, title), `пункт «${title}» есть`).toBeVisible()
    }
  })

  for (const path of ['/fc/tasks', '/fc/picking']) {
    test(`прямой заход на ${path} → главная ФЦ и тост «Нет доступа»`, async ({ page }) => {
      await demoLoginUI(page, 'fc_account_manager')
      await page.goto(path)
      await expect(toast(page).filter({ hasText: 'Нет доступа' })).toBeVisible()
      await expect.poll(() => pathname(page)).toMatch(/^\/fc\/?$/)
    })
  }
})

test.describe('Стоп-лист в UI (§1.4, §2.3)', () => {
  test('демо-админ: «Одобрить»/«Отклонить» и «Сбросить пароль» недоступны; организации остаются на модерации', async ({ page, clientIp }) => {
    await demoLoginUI(page, 'platform_admin')

    await page.goto('/admin/moderation')
    await waitReady(page)
    for (const name of ['Одобрить', 'Отклонить']) {
      const buttons = page.getByRole('button', { name, exact: true }).filter({ visible: true })
      const n = await buttons.count()
      for (let i = 0; i < n; i++) {
        const b = buttons.nth(i)
        if (await b.isEnabled()) {
          // Кнопка активна — источник правды 403 API: клик не должен ничего промодерировать
          await b.click()
          const confirm = page.locator('.p-confirmdialog, .p-dialog').last().getByRole('button', { name, exact: true })
          if (await confirm.isVisible().catch(() => false)) await confirm.click()
          await expect(toast(page).filter({ hasText: /демо/i }).first()).toBeVisible()
        }
      }
    }

    await page.goto('/admin/users')
    await waitReady(page)
    const resets = page.getByRole('button', { name: 'Сбросить пароль', exact: true }).filter({ visible: true })
    await expect(resets.first()).toBeVisible()
    const enabled = await resets.evaluateAll((els) => els.filter((e) => !(e as HTMLButtonElement).disabled).length)
    expect(enabled, 'активных «Сбросить пароль» в демо').toBe(0)

    // Проверка по API обычным админом: заявки p1…p3 не одобрены/не отклонены
    const api = await apiContext(clientIp)
    const admin = await passwordSession(api, 'admin@demo.ru')
    for (const id of ['org_p1', 'org_p2', 'org_p3']) {
      const res = await call(api, admin, 'GET', `/admin/organizations/${id}`)
      expect(JSON.stringify(await res.json()), id).toMatch(/"status"\s*:\s*"pending"/)
    }
    await api.dispose()
  })

  test('демо-токен в браузере: прямой POST reset-password → 403 «В демо-режиме…»', async ({ page }) => {
    await demoLoginUI(page, 'platform_admin')
    const res = await page.evaluate(async (api) => {
      const token = localStorage.getItem('wms.token')
      const r = await fetch(`${api}/admin/users/usr_p3/reset-password`, { method: 'POST', headers: { Authorization: `Bearer ${token}` } })
      return { status: r.status, body: await r.text() }
    }, API_URL)
    expect(res.status).toBe(403)
    expect(res.body).toContain('В демо-режиме это действие недоступно')
  })
})

test.describe('Обычный вход без демо', () => {
  test('director@demo.ru по паролю — без баннера на всех экранах, офисные разделы на месте', async ({ page }) => {
    await passwordLoginUI(page, 'director@demo.ru')
    expect(pathname(page)).toMatch(/^\/fc\/?$/)
    await expect(banner(page)).toHaveCount(0)
    await page.goto('/fc/orders')
    await waitReady(page)
    await expect(banner(page)).toHaveCount(0)
    // Пункты офиса на месте у полноправного директора
    await expect(sidebarLink(page, 'Задания')).toBeVisible()
    await expect(sidebarLink(page, 'Комплектация')).toBeVisible()
  })

  test('admin@demo.ru по паролю — без баннера, «Сбросить пароль» активна', async ({ page }) => {
    await passwordLoginUI(page, 'admin@demo.ru')
    await expect(banner(page)).toHaveCount(0)
    await page.goto('/admin/users')
    await waitReady(page)
    await expect(page.getByRole('button', { name: 'Сбросить пароль', exact: true }).first()).toBeEnabled()
  })

  test('вошедший по паролю может открыть /demo (не guestOnly) и перейти в демо', async ({ page }) => {
    await passwordLoginUI(page, 'director@demo.ru')
    await page.goto('/demo')
    await expect.poll(() => pathname(page)).toBe('/demo')
    await demoLoginUI(page, 'fc_picker')
    await expectBanner(page, 'fc_picker')
  })
})
