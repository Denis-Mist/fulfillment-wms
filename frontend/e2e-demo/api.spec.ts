import { expect, test, type APIRequestContext } from '@playwright/test'
import { apiContext, call, demoLogin, demoSession, jwtPayload, passwordSession, url, type Session } from './api'
import { DEMO_TTL_MIN, LOGIN_TTL_MIN } from './env'
import { ALL_PERSONAS, DEMO_FORBIDDEN_TITLE, PERSONAS } from './personas'

/**
 * Контракт API демо-режима (SPEC §2.2–§2.3) на реальном бэкенде. Каждый describe — свой «IP» клиента
 * (X-Forwarded-For), чтобы не упереться в лимит 10/мин на POST /auth/demo.
 */

let api: APIRequestContext
test.beforeEach(async () => {
  api = await apiContext()
})
test.afterEach(async () => {
  await api.dispose()
})

async function expectDemoForbidden(res: Awaited<ReturnType<APIRequestContext['fetch']>>, what: string) {
  expect(res.status(), `${what}: статус`).toBe(403)
  const body = (await res.json().catch(() => ({}))) as { title?: string; detail?: string }
  expect(`${body.title ?? ''} ${body.detail ?? ''}`, `${what}: ProblemDetails`).toContain(DEMO_FORBIDDEN_TITLE)
}

test.describe('API: вход в демо', () => {
  test('GET /auth/demo/status — публичный, enabled=true @prod-smoke', async () => {
    const res = await api.get(url('/auth/demo/status'))
    expect(res.status()).toBe(200)
    expect(await res.json()).toEqual(expect.objectContaining({ enabled: true }))
  })

  test('неизвестная персона → 422', async () => {
    for (const persona of ['ghost', 'admin', '']) {
      const res = await demoLogin(api, persona)
      expect(res.status(), `persona=${JSON.stringify(persona)}`).toBe(422)
    }
  })

  test('ответ без пароля, claim demo, TTL демо-токена ≈ Demo:TokenLifetimeMinutes @prod-smoke', async () => {
    const res = await demoLogin(api, 'fc_owner')
    expect(res.status()).toBe(200)
    const text = await res.text()
    expect(text.toLowerCase()).not.toContain('password')
    const s = (await JSON.parse(text)) as { accessToken: string; user: { isDemo?: boolean } }
    expect(s.user.isDemo).toBe(true)
    const p = jwtPayload(s.accessToken)
    expect(String(p.demo).toLowerCase(), 'claim demo').toBe('true')
    const issued = Number(p.iat ?? p.nbf)
    expect(Number(p.exp) - issued, 'срок жизни демо-токена, с').toBeGreaterThan(DEMO_TTL_MIN * 60 - 120)
    expect(Number(p.exp) - issued, 'срок жизни демо-токена, с').toBeLessThan(DEMO_TTL_MIN * 60 + 120)
  })

  // 11 персон — отдельные запросы; на один «IP» это ровно в пределах 10/мин не укладывается, поэтому делим на две группы
  for (const [i, group] of [ALL_PERSONAS.slice(0, 6), ALL_PERSONAS.slice(6)].entries()) {
    test(`все персоны логинятся в канонического сида (группа ${i + 1})`, async () => {
      for (const persona of group) {
        const def = PERSONAS[persona]
        const s = await demoSession(api, persona)
        expect(s.auth.user.email, persona).toBe(def.email)
        expect(s.auth.user.isDemo, `${persona}: isDemo`).toBe(true)
        if (def.role === null) {
          expect(s.auth.user.isPlatformAdmin, persona).toBe(true)
        } else {
          expect(s.auth.user.memberships, persona).toEqual(
            expect.arrayContaining([expect.objectContaining({ organizationId: def.organizationId, role: def.role })]),
          )
        }
        const me = await call(api, s, 'GET', '/auth/me')
        expect(me.status(), `${persona}: /auth/me`).toBe(200)
        const meBody = (await me.json()) as { email: string; isDemo?: boolean }
        expect(meBody.email).toBe(def.email)
        expect(meBody.isDemo, `${persona}: /auth/me isDemo`).toBe(true)
      }
    })
  }

  test('обычный логин по паролю: isDemo=false, токен без demo и на 720 мин', async () => {
    const s = await passwordSession(api, 'director@demo.ru')
    expect(s.auth.user.isDemo ?? false).toBe(false)
    const me = (await (await call(api, s, 'GET', '/auth/me')).json()) as { isDemo?: boolean }
    expect(me.isDemo ?? false).toBe(false)
    const p = jwtPayload(s.token)
    expect(p.demo === undefined || String(p.demo).toLowerCase() === 'false').toBe(true)
    expect(Number(p.exp) - Number(p.iat ?? p.nbf)).toBeGreaterThan(LOGIN_TTL_MIN * 60 - 120)
  })
})

test.describe('API: стоп-лист демо-токена (§2.3)', () => {
  test('демо-админ: сброс пароля, блокировка, модерация, справочники, сброс демо → 403; данные не изменились', async () => {
    const demo = await demoSession(api, 'platform_admin')
    // Цели — реальные сид-записи, не используемые другими сценариями: если стоп-лист пропустит запрос, это будет видно
    const cases: [string, string, unknown?][] = [
      ['POST', '/admin/users/usr_p3/reset-password'],
      ['POST', '/admin/users/usr_p3/block'],
      ['POST', '/admin/users/usr_p3/unblock'],
      ['POST', '/admin/organizations/org_p3/approve'],
      ['POST', '/admin/organizations/org_p3/reject', { reason: 'e2e demo stop-list' }],
      ['POST', '/admin/organizations/org_p3/block', { reason: 'e2e demo stop-list' }],
      ['POST', '/admin/organizations/org_fc3/unblock'],
      ['POST', '/admin/references', { kind: 'category', name: 'e2e-demo' }],
      ['POST', '/admin/references/ref_e2e_demo/toggle'],
      ['POST', '/admin/demo/reset'],
    ]
    for (const [method, path, body] of cases) {
      await expectDemoForbidden(await call(api, demo, method, path, body), `${method} ${path}`)
    }

    // Состояние не изменилось: org_p3 на модерации, owner3 входит с прежним паролем и не заблокирован
    const admin = await passwordSession(api, 'admin@demo.ru')
    const org = await call(api, admin, 'GET', '/admin/organizations/org_p3')
    expect(org.status()).toBe(200)
    expect(JSON.stringify(await org.json())).toMatch(/"status"\s*:\s*"pending"/)
    await passwordSession(api, 'owner3@pending.demo.ru')
  })

  test('демо-токены ФЦ и селлера: запрещённые мутации → 403', async () => {
    const fc = await demoSession(api, 'fc_owner')
    const seller = await demoSession(api, 'seller_owner')
    const cases: [Session, string, string, unknown?][] = [
      [fc, 'PUT', '/settings/profile', {}],
      [fc, 'POST', '/settings/team/invite', { email: 'e2e-demo@example.com', role: 'fc_manager' }],
      [fc, 'POST', '/settings/team/usr_fc1_2/revoke'],
      [fc, 'POST', '/fc/client-requests/req_e2e/approve'],
      [fc, 'PUT', '/billing/tariffs/tar_e2e', {}],
      [fc, 'POST', '/warehouses/wh_e2e/zones', {}],
      [fc, 'POST', '/warehouses/wh_e2e/locations', {}],
      [fc, 'PUT', '/staff/usr_fc1_3', {}],
      [fc, 'POST', '/staff/usr_fc1_3/shifts', {}],
      [fc, 'POST', '/catalog/products/prd_e2e/archive'],
      [fc, 'POST', '/analytics/slotting/apply', {}],
      [fc, 'POST', '/stock/writeoff', {}],
      [fc, 'POST', '/tasks/tsk_e2e/cancel'],
      [seller, 'POST', '/integrations/wb/connect', { apiKey: 'e2e-not-a-key' }],
      [seller, 'POST', '/integrations/wb/disconnect'],
      [seller, 'PUT', '/integrations/ozon', {}],
      [seller, 'POST', '/integrations/wb/sync'],
      [seller, 'POST', '/seller/fulfillments/org_fc2/connect'],
    ]
    for (const [s, method, path, body] of cases) {
      await expectDemoForbidden(await call(api, s, method, path, body), `${s.auth.user.email} ${method} ${path}`)
    }
  })

  test('стоп-лист работает по claim: обычный вход director@demo.ru сохраняет профиль (не 403)', async () => {
    const owner = await passwordSession(api, 'director@demo.ru')
    const settings = await call(api, owner, 'GET', '/settings')
    expect(settings.status()).toBe(200)
    const profile = ((await settings.json()) as { profile?: unknown }).profile
    expect(profile, 'GET /settings → profile').toBeTruthy()
    const put = await call(api, owner, 'PUT', '/settings/profile', profile)
    expect(put.status(), await put.text()).toBeLessThan(300)
  })

  test('разрешённые запросы демо-токена проходят: чтение и не-стоп-лист мутация', async () => {
    const fc = await demoSession(api, 'fc_owner')
    for (const path of ['/settings', '/tasks', '/stock']) {
      const res = await call(api, fc, 'GET', path)
      expect(res.status(), `GET ${path}`).toBe(200)
    }
    // Настройки уведомлений — не в стоп-листе (§2.3: «уведомления и т.д. разрешены»)
    const settings = (await (await call(api, fc, 'GET', '/settings')).json()) as { notifications?: unknown }
    const put = await call(api, fc, 'PUT', '/settings/notifications', settings.notifications ?? {})
    expect(put.status(), await put.text()).toBeLessThan(300)
  })
})

test.describe('API: аккаунт-менеджер (§3)', () => {
  test('fc_account_manager: задания и комплектация закрыты API (403), клиенты и биллинг открыты', async () => {
    const am = await demoSession(api, 'fc_account_manager')
    for (const path of ['/tasks', '/picking/candidates']) {
      const res = await call(api, am, 'GET', path)
      expect(res.status(), `GET ${path}`).toBe(403)
    }
    for (const path of ['/fc/clients', '/billing/summary']) {
      const res = await call(api, am, 'GET', path)
      expect(res.status(), `GET ${path}`).toBeLessThan(400)
    }
  })
})
