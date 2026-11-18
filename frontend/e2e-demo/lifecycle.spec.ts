import { randomBytes } from 'node:crypto'
import type { Page } from '@playwright/test'
import { DEMO_FLAG_KEY, DEMO_HEADER, DEMO_HEADER_VALUE, DEMO_TOKEN_PREFIX, MOCK_COLLECTION_PREFIX, MOCKDB_KEY, TEXT } from './contract'
import {
  confirmIfAsked,
  demoTokenEntry,
  enterDemo,
  expect,
  expectBanner,
  openDemo,
  pathname,
  personaCards,
  storage,
  test,
  visibleAction,
  waitReady,
} from './fixtures'
import { PERSONAS, type Persona } from './personas'

/**
 * Учебная мутация в песочнице: товар селлера через мок POST /api/catalog/products (тот же запрос, что делает UI,
 * с демо-токеном и X-WMS-Demo). Проверка — через интерфейс другой роли.
 */
async function createDemoProduct(page: Page, persona: Persona) {
  const id = randomBytes(4).toString('hex')
  const product = {
    sku: `E2E-V2-${id}`,
    name: `Товар e2e демо v2 ${id}`,
    category: 'E2E',
    barcode: String(Date.now()).slice(-9) + String(Math.floor(Math.random() * 9000) + 1000),
    price: 990,
    weightG: 150,
    volumeLiters: 0.5,
    tracksExpiry: false,
    requiresMarking: false,
  }
  const entry = await demoTokenEntry(page)
  expect(entry, 'демо-токен mock.demo:… в localStorage').toBeTruthy()
  const res = await page.evaluate(
    async ({ product, token, org, header, value }) => {
      const r = await fetch('/api/catalog/products', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Accept: 'application/json',
          Authorization: `Bearer ${token}`,
          'X-Organization-Id': org,
          [header]: value,
        },
        body: JSON.stringify(product),
      })
      return { status: r.status, body: await r.text() }
    },
    { product, token: entry![1], org: PERSONAS[persona].organizationId ?? '', header: DEMO_HEADER, value: DEMO_HEADER_VALUE },
  )
  expect(res.status, `мок POST /catalog/products: ${res.body}`).toBe(201)
  return product
}

/** Найти товар на /seller/products через поиск UI */
async function productRow(page: Page, sku: string) {
  await page.goto('/seller/products')
  await waitReady(page)
  await page.getByPlaceholder('Артикул, название, штрихкод…').fill(sku)
  await page.waitForTimeout(600) // debounce поиска
  await waitReady(page)
  return page.getByText(sku, { exact: true })
}

async function switchRole(page: Page, to: Persona) {
  await visibleAction(page, TEXT.switchRole).first().click()
  await expect.poll(() => pathname(page)).toBe('/demo')
  await expect(personaCards(page)).toHaveCount(11)
  return enterDemo(page, to)
}

test.describe('Перезагрузка остаётся в демо (SPEC-v2 §3.2, §3.5, §7)', () => {
  for (const [persona, path] of [
    ['fc_owner', '/fc/orders'],
    ['fc_picker', '/work'],
    ['pending', '/pending'],
  ] as const) {
    test(`${persona}: reload на ${path} — тот же кабинет, баннер, /auth/me обслужен MSW`, async ({ page }) => {
      await enterDemo(page, persona)
      if (pathname(page) !== path) {
        await page.goto(path)
        await waitReady(page)
      }
      const me = page.waitForResponse((r) => new URL(r.url()).pathname.endsWith('/auth/me'))
      await page.reload()
      const res = await me
      // MSW стартовал до session.init(): иначе /auth/me с демо-токеном ушёл бы на реальный API и дал 401
      expect(res.fromServiceWorker(), '/auth/me после reload — от MSW').toBe(true)
      expect(res.request().headers()[DEMO_HEADER]).toBe(DEMO_HEADER_VALUE)
      expect(res.status()).toBe(200)
      await waitReady(page)
      expect(pathname(page)).toBe(path)
      await expectBanner(page, persona)
      expect((await storage(page))[DEMO_FLAG_KEY]).toBe('1')
    })
  }

  test('учебные изменения переживают перезагрузку (overlay wms.mockdb.v2)', async ({ page }) => {
    await enterDemo(page, 'seller_owner')
    const product = await createDemoProduct(page, 'seller_owner')
    expect(Object.keys(await storage(page)).some((k) => k === MOCKDB_KEY || k.startsWith(MOCK_COLLECTION_PREFIX))).toBe(true)
    await page.reload()
    await waitReady(page)
    await expect(await productRow(page, product.sku)).toBeVisible()
  })
})

test.describe('Сменить роль / Сбросить данные (SPEC-v2 §2.2, §3.5, §7)', () => {
  test('«Сменить роль» сохраняет учебную базу: товар владельца виден менеджеру селлера', async ({ page }) => {
    await enterDemo(page, 'seller_owner')
    const product = await createDemoProduct(page, 'seller_owner')
    const ownerToken = (await demoTokenEntry(page))?.[1]

    await visibleAction(page, TEXT.switchRole).first().click()
    await expect.poll(() => pathname(page)).toBe('/demo')
    // На /demo остаёмся в демо-режиме, база не сброшена
    expect((await storage(page))[DEMO_FLAG_KEY]).toBe('1')
    await enterDemo(page, 'seller_manager')
    await expectBanner(page, 'seller_manager')
    const managerToken = (await demoTokenEntry(page))?.[1]
    expect(managerToken).toBeTruthy()
    expect(managerToken).not.toBe(ownerToken)

    await expect(await productRow(page, product.sku)).toBeVisible()
  })

  test('«Сменить роль» на роль ФЦ: кабинет селлера больше не доступен', async ({ page }) => {
    await enterDemo(page, 'seller_owner')
    await switchRole(page, 'fc_owner')
    await expectBanner(page, 'fc_owner')
    await page.goto('/seller/products')
    await expect.poll(() => pathname(page)).toMatch(/^\/fc/)
  })

  test('«Сбросить данные» в баннере возвращает seed и оставляет в демо', async ({ page }) => {
    await enterDemo(page, 'seller_owner')
    const product = await createDemoProduct(page, 'seller_owner')
    await expect(await productRow(page, product.sku)).toBeVisible()

    await visibleAction(page, TEXT.resetData).first().click()
    await confirmIfAsked(page)
    // §2.2: перезаход текущей ролью (или /demo, если сессии нет)
    await expect.poll(() => pathname(page)).toMatch(new RegExp(`${PERSONAS.seller_owner.home.source}|^/demo$`))
    await waitReady(page)
    const ls = await storage(page)
    expect(ls[DEMO_FLAG_KEY], 'после сброса остаёмся в демо').toBe('1')
    expect(JSON.stringify(ls), 'overlay очищен от учебных изменений').not.toContain(product.sku)
    if (pathname(page) === '/demo') await enterDemo(page, 'seller_owner')
    await expectBanner(page, 'seller_owner')
    // Seed восстановлен и в памяти, а не только в storage (§3.5: db.reset() раньше этого не делал)
    await expect(await productRow(page, product.sku)).toHaveCount(0)
  })

  test('«Сбросить демо-данные» на /demo тоже возвращает seed', async ({ page }) => {
    await enterDemo(page, 'seller_owner')
    const product = await createDemoProduct(page, 'seller_owner')
    await openDemo(page)
    await visibleAction(page, TEXT.resetOnDemoPage).first().click()
    await confirmIfAsked(page)
    await page.waitForLoadState('networkidle')
    expect(JSON.stringify(await storage(page))).not.toContain(product.sku)
    if (pathname(page) === '/demo') await enterDemo(page, 'seller_owner')
    await expect(await productRow(page, product.sku)).toHaveCount(0)
  })

  test('битая демо-сессия → /demo с понятным сообщением, а не /login (§2.2)', async ({ page }) => {
    await enterDemo(page, 'fc_owner')
    const entry = await demoTokenEntry(page)
    expect(entry).toBeTruthy()
    await page.evaluate(([key, value]) => localStorage.setItem(key, value), [entry![0], `${DEMO_TOKEN_PREFIX}usr_e2e_missing`])
    await page.goto('/fc/orders')
    await expect.poll(() => pathname(page)).toBe('/demo')
    await expect(page.locator('.p-message').filter({ visible: true }).first()).toBeVisible()
    await expect(personaCards(page)).toHaveCount(11)
  })
})
