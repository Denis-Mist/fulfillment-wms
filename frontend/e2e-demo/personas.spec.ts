import type { Response } from '@playwright/test'
import { DEMO_FLAG_KEY, DEMO_HEADER, DEMO_HEADER_VALUE, TEXT } from './contract'
import {
  banner,
  demoFlag,
  enterDemo,
  expect,
  expectBanner,
  isApiUrl,
  pathname,
  recordApiRequests,
  sidebarLink,
  sidebarLinks,
  storage,
  test,
  toast,
  waitReady,
} from './fixtures'
import { ALL_PERSONAS, PERSONAS, SECOND_PAGE } from './personas'

test.describe('11 ролей: вход, кабинет, баннер (SPEC-v2 §2.1, §2.2, §7)', () => {
  for (const persona of ALL_PERSONAS) {
    test(`${persona}: кабинет ${PERSONAS[persona].home.source}, баннер, все /api — моки с ${DEMO_HEADER}`, async ({ page }) => {
      const requests = recordApiRequests(page)
      const responses: Response[] = []
      page.on('response', (r) => isApiUrl(r.url()) && responses.push(r))
      await enterDemo(page, persona)
      await expectBanner(page, persona)
      const ls = await storage(page)
      expect(await demoFlag(page), `флаг ${DEMO_FLAG_KEY} в sessionStorage`).toBe('1')
      // Пароль нигде не сохраняется
      expect(JSON.stringify(ls)).not.toMatch(/"password"|"demo"/i)

      const second = SECOND_PAGE[persona]
      if (second) {
        await page.goto(second)
        await waitReady(page)
        expect(pathname(page)).toBe(second)
        await expectBanner(page, persona)
      }
      // Каждый запрос к /api из демо-вкладки помечен и обслужен MSW (§3.4)
      expect(requests.length).toBeGreaterThan(0)
      for (const r of requests) {
        expect(r.headers()[DEMO_HEADER], `${DEMO_HEADER}: ${r.method()} ${r.url()}`).toBe(DEMO_HEADER_VALUE)
      }
      // Ответы — только полученные: отменённые навигацией запросы response() не дождутся (зависание)
      expect(responses.length).toBeGreaterThan(0)
      for (const res of responses) {
        expect(res.fromServiceWorker(), `ответ от MSW: ${res.request().method()} ${res.url()}`).toBe(true)
      }
    })
  }

  test('fc_picker на мобильном (ТСД, 390px): баннер и все три действия в экране', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await enterDemo(page, 'fc_picker')
    await expectBanner(page, 'fc_picker')
    const box = await banner(page).first().boundingBox()
    expect(box && box.x >= 0 && box.x + box.width <= 391).toBeTruthy()
  })
})

test.describe('Менеджер по клиентам (SPEC-v2 §2.3, §7, §11.4)', () => {
  test('в меню нет «Задания»/«Комплектация», коммерческие разделы есть', async ({ page }) => {
    await enterDemo(page, 'fc_account_manager')
    for (const title of ['Задания', 'Комплектация']) {
      await expect(sidebarLink(page, title), `пункт «${title}» скрыт`).toHaveCount(0)
    }
    for (const title of ['Обзор', 'Клиенты и договоры', 'Товары клиентов', 'Биллинг']) {
      await expect(sidebarLink(page, title), `пункт «${title}» есть`).toBeVisible()
    }
  })

  test('ни один пункт меню не ведёт на пустую 403 / «Нет доступа»', async ({ page }) => {
    await enterDemo(page, 'fc_account_manager')
    const hrefs = await sidebarLinks(page).evaluateAll((els) => [...new Set(els.map((e) => e.getAttribute('href') ?? ''))])
    expect(hrefs.length).toBeGreaterThan(3)
    for (const href of hrefs.filter((h) => h.startsWith('/'))) {
      await page.goto(href)
      await waitReady(page)
      expect(pathname(page), `пункт меню ${href} открылся`).toBe(href)
      await expect(toast(page).filter({ hasText: TEXT.noAccess }), `«Нет доступа» на ${href}`).toHaveCount(0)
      await expect(page.getByText(/^\s*(403|Доступ запрещён|Недостаточно прав)\s*$/), `403 на ${href}`).toHaveCount(0)
      await expectBanner(page, 'fc_account_manager')
    }
  })

  for (const path of ['/fc/tasks', '/fc/picking']) {
    test(`прямой заход на ${path} → главная ФЦ и «Нет доступа»`, async ({ page }) => {
      await enterDemo(page, 'fc_account_manager')
      await page.goto(path)
      await expect(toast(page).filter({ hasText: TEXT.noAccess })).toBeVisible()
      await expect.poll(() => pathname(page)).toMatch(/^\/fc\/?$/)
    })
  }
})

test.describe('Ослабленный стоп-лист: учебные мутации разрешены (SPEC-v2 §2.3)', () => {
  test('демо-админ: «Сбросить пароль» и модерация не заблокированы, нет «В демо-режиме это действие недоступно»', async ({ page }) => {
    await enterDemo(page, 'platform_admin')
    await page.goto('/admin/users')
    await waitReady(page)
    const resets = page.getByRole('button', { name: 'Сбросить пароль', exact: true }).filter({ visible: true })
    await expect(resets.first()).toBeVisible()
    await expect(resets.first()).toBeEnabled()

    await page.goto('/admin/moderation')
    await waitReady(page)
    const approve = page.getByRole('button', { name: 'Одобрить', exact: true }).filter({ visible: true })
    await expect(approve.first()).toBeVisible()
    await expect(approve.first()).toBeEnabled()
    await expect(page.getByText(TEXT.v1Forbidden)).toHaveCount(0)
  })
})
