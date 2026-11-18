import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { DEMO_FLAG_KEY, REAL_TOKEN_KEY, TESTID, TEXT } from './contract'
import { banner, demoFlag, enterDemo, expect, expectCardTitle, isApiUrl, openDemo, pathname, personaCard, personaCards, storage, test, visibleAction } from './fixtures'
import { ALL_PERSONAS } from './personas'

/** Ресурсы слоя моков: чанк src/mocks (в сборке — browser-*.js), msw, mockServiceWorker.js */
const MOCK_ASSET = /\/src\/mocks\/|\/node_modules\/\.vite\/deps\/msw|\/assets\/browser-[\w-]+\.js$|mockServiceWorker\.js/

test.describe('Вход в демо: кнопки и /demo (SPEC-v2 §2.1, §3.1, §3.6, §7)', () => {
  test('/login: «Попробовать демо» + подпись → /demo, сетки паролей нет @prod-smoke', async ({ page }) => {
    await page.goto('/login')
    await expect(page.getByText(TEXT.loginHint)).toBeVisible()
    // §2.1: сетка быстрого входа с паролем `demo` — только при VITE_USE_MOCKS=true
    await expect(page.getByText(/войдите как демо-пользователь/i)).toHaveCount(0)
    await expect(page.getByText(/director@demo\.ru|парол[ья]\s*[:—-]?\s*demo\b/i)).toHaveCount(0)
    await page.getByTestId(TESTID.tryDemo).click()
    await expect.poll(() => pathname(page)).toBe('/demo')
  })

  test('/register: есть «Попробовать демо» → /demo (новое в v2) @prod-smoke', async ({ page }) => {
    await page.goto('/register')
    await expect(page.getByRole('heading', { name: 'Регистрация' })).toBeVisible()
    await expect(page.getByTestId(TESTID.tryDemo)).toHaveText(TEXT.tryDemo)
    await page.getByTestId(TESTID.tryDemo).click()
    await expect.poll(() => pathname(page)).toBe('/demo')
    // Вторая точка входа: ссылка «Попробовать демо» внизу формы («Уже зарегистрированы? Войти · Попробовать демо»)
    await page.goto('/register')
    await page.getByRole('link', { name: TEXT.tryDemo, exact: true }).click()
    await expect.poll(() => pathname(page)).toBe('/demo')
  })

  test('лендинг deploy/landing: обе кнопки «Попробовать демо» ведут на https://wms.korobfulfillment.ru/demo', async ({ page }) => {
    const file = fileURLToPath(new URL('../../deploy/landing/index.html', import.meta.url))
    // Без сети: только разметка лендинга из репозитория
    await page.route(/^https?:\/\//, (route) => route.abort())
    await page.setContent(readFileSync(file, 'utf8'), { waitUntil: 'domcontentloaded' })
    const links = page.getByRole('link', { name: TEXT.tryDemo, exact: true })
    expect(await links.count()).toBeGreaterThanOrEqual(2)
    for (const href of await links.evaluateAll((els) => els.map((e) => e.getAttribute('href')))) {
      expect(href).toBe('https://wms.korobfulfillment.ru/demo')
    }
  })

  test('/demo: 11 карточек, тексты «личная песочница», без ночного сброса, есть «Сбросить демо-данные» @prod-smoke', async ({ page }) => {
    await openDemo(page)
    await expect(page.getByText(/без пароля/).first()).toBeVisible()
    await expect(page.getByText(TEXT.demoPersonal).first()).toBeVisible()
    await expect(page.getByText(TEXT.demoNoNightly)).toHaveCount(0)
    await expect(personaCards(page)).toHaveCount(11)
    for (const persona of ALL_PERSONAS) {
      await expectCardTitle(page, persona)
    }
    await expect(visibleAction(page, TEXT.resetOnDemoPage).first()).toBeVisible()
    await expect(page.getByText(/парол[ья]\s*[:—-]?\s*demo/i)).toHaveCount(0)
  })

  test('/demo не спрашивает сервер: нет GET /api/auth/demo/status, карточки видны даже при недоступном API', async ({ page }) => {
    const apiCalls: string[] = []
    page.on('request', (r) => isApiUrl(r.url()) && apiCalls.push(`${r.method()} ${r.url()}`))
    // API «лежит» — /demo всё равно показывает выбор ролей, а не «Демо временно выключено» (§3.6)
    await page.route('**/api/**', (route) => route.abort('connectionrefused'))
    await page.goto('/demo')
    await expect(page.getByText(TEXT.demoTitle)).toBeVisible()
    await expect(personaCards(page)).toHaveCount(11)
    await expect(page.getByText(TEXT.v1Disabled)).toHaveCount(0)
    await expect(page.getByText('Не удалось проверить демо')).toHaveCount(0)
    expect(apiCalls.filter((c) => c.includes('/auth/demo/status')), 'запросы статуса демо').toEqual([])
    expect(apiCalls, 'гость на /demo до выбора роли в /api не ходит').toEqual([])
  })

  test('/demo адаптивен: на 390px все 11 карточек, без горизонтального скролла', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 })
    await openDemo(page)
    await expect(personaCards(page)).toHaveCount(11)
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
    expect(overflow).toBeLessThanOrEqual(1)
  })
})

test.describe('Ленивая загрузка моков (SPEC-v2 §3.1)', () => {
  test('боевые /login и /register не грузят чанк моков и не регистрируют service worker @prod-smoke', async ({ page }) => {
    const assets: string[] = []
    page.on('request', (r) => MOCK_ASSET.test(r.url()) && assets.push(r.url()))
    for (const path of ['/login', '/register']) {
      await page.goto(path)
      await page.waitForLoadState('networkidle')
    }
    expect(assets, 'ресурсы моков на боевых страницах').toEqual([])
    const regs = await page.evaluate(async () => ((await navigator.serviceWorker?.getRegistrations()) ?? []).length)
    expect(regs, 'регистрации service worker до входа в демо').toBe(0)
    const ls = await storage(page)
    expect(await demoFlag(page), `${DEMO_FLAG_KEY} без входа в демо`).toBeNull()
    expect(ls[DEMO_FLAG_KEY] ?? '').toBe('')
  })

  test('открыть /demo ещё не значит включить демо: флага и SW нет, пока роль не выбрана', async ({ page }) => {
    await openDemo(page)
    await page.waitForLoadState('networkidle')
    const ls = await storage(page)
    expect(await demoFlag(page)).toBeNull()
    expect(ls[DEMO_FLAG_KEY] ?? '').toBe('')
    expect(ls[REAL_TOKEN_KEY] ?? '').toBe('')
  })
})

test.describe('Service worker не поднялся (SPEC-v2 §2.4)', () => {
  for (const [name, script] of [
    [
      'register() отклонён (блокировщик/политика)',
      () => {
        if (navigator.serviceWorker) {
          Object.defineProperty(navigator.serviceWorker, 'register', {
            configurable: true,
            value: () => Promise.reject(new DOMException('blocked by e2e', 'SecurityError')),
          })
        }
      },
    ],
    [
      'serviceWorker недоступен в браузере',
      () => {
        // как в браузере без SW (небезопасный контекст, часть приватных режимов): свойства navigator.serviceWorker нет вовсе
        delete (Navigator.prototype as unknown as Record<string, unknown>).serviceWorker
      },
    ],
  ] as const) {
    test(`${name}: «Не удалось запустить демо в этом браузере», демо-запросы не уходят на сервер`, async ({ page }) => {
      const demoPosts: string[] = []
      page.on('request', (r) => isApiUrl(r.url()) && r.url().includes('/auth/demo') && demoPosts.push(`${r.method()} ${r.url()}`))
      await page.addInitScript(script)
      await openDemo(page)
      await personaCard(page, 'fc_owner').click()
      await expect(page.getByTestId(TESTID.startError)).toContainText(TEXT.swFailed)
      await expect(page.getByText(TEXT.v1Disabled)).toHaveCount(0)
      const toLogin = page.getByTestId(TESTID.startError).getByRole('button', { name: TEXT.toLogin })
      await expect(toLogin).toBeVisible()
      // Реальный API не должен получить демо-вход как боевой (§2.4); страж demoLeaks дополнительно ловит X-WMS-Demo/mock.
      expect(demoPosts, 'запросы /auth/demo при неподнятом SW').toEqual([])
      const ls = await storage(page)
      expect(ls[REAL_TOKEN_KEY] ?? '', 'боевой токен не появился').toBe('')
      expect(await demoFlag(page), `${DEMO_FLAG_KEY} не остаётся включённым после неудачи`).not.toBe('1')
      await toLogin.click()
      await expect.poll(() => pathname(page)).toBe('/login')
    })
  }
})

test.describe('Флаг демо — на вкладку, а не на браузер (SPEC-v2 §12 Q-1, §3.4)', () => {
  test('флаг wms.demo.active лежит в sessionStorage, в localStorage его нет', async ({ page }) => {
    await enterDemo(page, 'fc_owner')
    expect(await demoFlag(page), 'sessionStorage').toBe('1')
    expect((await storage(page))[DEMO_FLAG_KEY], 'localStorage не должен содержать флаг (§12)').toBeUndefined()
  })

  test('новая вкладка того же браузера открывается как боевая; перезагрузка демо-вкладки остаётся демо', async ({ page, context }) => {
    await enterDemo(page, 'seller_owner')
    const other = await context.newPage()
    await other.goto('/')
    await expect.poll(() => pathname(other)).toBe('/login')
    expect(await demoFlag(other), 'флаг в новой вкладке').toBeNull()
    await expect(banner(other)).toHaveCount(0)
    await other.close()
    await page.reload()
    await expect(banner(page).first()).toBeVisible()
    expect(await demoFlag(page)).toBe('1')
  })
})
