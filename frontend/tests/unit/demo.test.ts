import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { navSectionsForRole } from '@/app/sections'
import { useMockServer } from './setup-msw'
import { configureHttp } from '@/shared/api/http'
import { errorMessage, ApiError } from '@/shared/api/http'
import { authApi } from '@/modules/auth/api'
import { useSessionStore } from '@/stores/session'
import { demoFlags } from '@/mocks/handlers/auth'
import { tokenLooksDemo } from '@/modules/auth/token'
import { tokenFor } from '@/mocks/lib/http'

useMockServer()

async function setup() {
  setActivePinia(createPinia())
  const session = useSessionStore()
  configureHttp({
    getToken: () => session.token || null,
    getOrganizationId: () => session.organizationId || null,
    onUnauthorized: () => {},
  })
  return session
}

describe('меню аккаунт-менеджера', () => {
  it('не показывает «Задания» и «Комплектацию», в отличие от офисных ролей', () => {
    const am = navSectionsForRole('fc', 'fc_account_manager').map((s) => s.name)
    expect(am).not.toContain('tasks')
    expect(am).not.toContain('picking')
    expect(am).toEqual(expect.arrayContaining(['dashboard', 'inbound', 'returns', 'stock', 'orders', 'fbo', 'clients', 'products', 'billing']))
    for (const name of ['tasks', 'picking', 'warehouses', 'analytics', 'staff', 'settings']) {
      expect(am).not.toContain(name)
    }
    for (const role of ['fc_owner', 'fc_manager', 'fc_supervisor'] as const) {
      const names = navSectionsForRole('fc', role).map((s) => s.name)
      expect(names, role).toEqual(expect.arrayContaining(['tasks', 'picking']))
    }
  })
})

describe('isDemo', () => {
  beforeEach(() => {
    localStorage.clear()
    sessionStorage.clear()
    demoFlags.enabled = true
  })

  it('обычный логин по паролю не ставит isDemo, демо-вход ставит', async () => {
    const session = await setup()
    await session.login({ email: 'director@demo.ru', password: 'demo' })
    expect(session.user?.isDemo).toBe(false)
    expect(session.isDemo).toBe(false)
    const me = await authApi.me()
    expect(me.isDemo).toBe(false)

    await session.loginDemo('fc_owner')
    expect(session.user?.email).toBe('director@demo.ru')
    expect(session.isDemo).toBe(true)
    expect((await authApi.me()).isDemo).toBe(true)
  })

  it('все 11 персон входят в нужную роль и организацию', async () => {
    const session = await setup()
    const expected: Record<string, { email: string; role: string; org?: string }> = {
      platform_admin: { email: 'admin@demo.ru', role: 'platform_admin' },
      fc_owner: { email: 'director@demo.ru', role: 'fc_owner', org: 'СкладПро Фулфилмент' },
      fc_manager: { email: 'manager@demo.ru', role: 'fc_manager', org: 'СкладПро Фулфилмент' },
      fc_supervisor: { email: 'staff8@fc1.demo.ru', role: 'fc_supervisor', org: 'СкладПро Фулфилмент' },
      fc_account_manager: { email: 'am@fc1.demo.ru', role: 'fc_account_manager', org: 'СкладПро Фулфилмент' },
      fc_receiver: { email: 'staff1@fc1.demo.ru', role: 'fc_receiver', org: 'СкладПро Фулфилмент' },
      fc_picker: { email: 'picker@demo.ru', role: 'fc_picker', org: 'СкладПро Фулфилмент' },
      fc_packer: { email: 'staff6@fc1.demo.ru', role: 'fc_packer', org: 'СкладПро Фулфилмент' },
      seller_owner: { email: 'seller@demo.ru', role: 'seller_owner', org: 'Уютный дом' },
      seller_manager: { email: 'manager@ud.demo.ru', role: 'seller_manager', org: 'Уютный дом' },
      pending: { email: 'owner1@pending.demo.ru', role: 'fc_owner', org: 'Сибирь Фулфилмент' },
    }
    for (const [persona, exp] of Object.entries(expected)) {
      await session.loginDemo(persona as never)
      expect(session.user?.email, persona).toBe(exp.email)
      expect(session.role, persona).toBe(exp.role)
      expect(session.isDemo, persona).toBe(true)
      if (exp.org) expect(session.activeMembership?.organizationName, persona).toBe(exp.org)
      if (persona === 'pending') {
      expect(session.isRestricted).toBe(true)
      expect(session.organizationId).toBe('org_p1')
      expect(session.role).toBe('fc_owner')
    }
    }
  })

  it('персона сопоставляется без учёта регистра', async () => {
    const session = await setup()
    await session.loginDemo('FC_Owner' as never)
    expect(session.user?.email).toBe('director@demo.ru')
    expect(session.isDemo).toBe(true)
    expect(session.user?.isDemo).toBe(true)
  })

  it('неизвестная персона → 422 «Неизвестная персона», выключенное демо → 200 status и 404 POST', async () => {
    const session = await setup()
    const unknown = session.loginDemo('nope' as never)
    await expect(unknown).rejects.toBeInstanceOf(ApiError)
    await unknown.catch((e: ApiError) => {
      expect(e.status).toBe(422)
      expect(e.problem.title).toBe('Неизвестная персона')
      expect(errorMessage(e)).toBe('Неизвестная персона')
    })
    demoFlags.enabled = false
    await expect(authApi.demoStatus()).resolves.toEqual({ enabled: false })
    const disabled = session.loginDemo('fc_owner')
    await expect(disabled).rejects.toBeInstanceOf(ApiError)
    await disabled.catch((e: ApiError) => {
      expect(e.status).toBe(404)
      expect(e.problem.title).toBe('Не найдено')
    })
    demoFlags.enabled = true
  })
})

describe('тексты ошибок демо', () => {
  it('подставляет понятные формулировки для 403/429/503 без тела', () => {
    expect(errorMessage(new ApiError(403, { title: '', status: 403 }))).toBe(
      'В демо-режиме это действие недоступно',
    )
    expect(errorMessage(new ApiError(429, { title: '', status: 429 }))).toBe(
      'Слишком много попыток, попробуйте позже',
    )
    expect(errorMessage(new ApiError(503, { title: '', status: 503 }))).toBe(
      'Демо-данные обновляются, подождите минуту',
    )
    expect(
      errorMessage(new ApiError(403, { title: 'В демо-режиме это действие недоступно', status: 403 })),
    ).toBe('В демо-режиме это действие недоступно')
    expect(errorMessage(new ApiError(422, { title: 'Неизвестная персона', status: 422 }))).toBe(
      'Неизвестная персона',
    )
  })
})

describe('истечение демо-токена', () => {
  it('tokenLooksDemo распознаёт mock- и JWT-токены', () => {
    expect(tokenLooksDemo(tokenFor('usr_1', true))).toBe(true)
    expect(tokenLooksDemo(tokenFor('usr_1', false))).toBe(false)
    const payload = btoa(JSON.stringify({ demo: 'true' })).replaceAll('=', '')
    expect(tokenLooksDemo(`eyJhbGciOiJub25lIn0.${payload}.sig`)).toBe(true)
    expect(tokenLooksDemo(`eyJhbGciOiJub25lIn0.${btoa(JSON.stringify({ sub: 'x' })).replaceAll('=', '')}.sig`)).toBe(
      false,
    )
  })

  it('401 по демо-токену помечает сессию как истёкшую', async () => {
    const session = await setup()
    await session.loginDemo('fc_owner')
    expect(session.demoExpired).toBe(false)
    session.handleUnauthorized()
    expect(session.demoExpired).toBe(true)
    expect(session.isAuthenticated).toBe(false)
  })
})

describe('изоляция сессии и сброс мок-БД', () => {
  beforeEach(() => {
    localStorage.clear()
    sessionStorage.clear()
    demoFlags.enabled = true
  })

  it('вход в демо не затирает боевой токен', async () => {
    const session = await setup()
    await session.login({ email: 'director@demo.ru', password: 'demo' })
    const real = session.realToken
    expect(real).toBeTruthy()
    expect(localStorage.getItem('wms.token')).toBe(real)

    await session.loginDemo('fc_manager')
    expect(session.isDemo).toBe(true)
    expect(sessionStorage.getItem('wms.demo.active')).toBe('1')
    expect(localStorage.getItem('wms.demo.active')).toBe('1')
    expect(localStorage.getItem('wms.token')).toBe(real)
    expect(localStorage.getItem('wms.demo.token')?.startsWith('mock.demo:')).toBe(true)
    expect(session.token).toBe(localStorage.getItem('wms.demo.token'))
  })

  it('сброс восстанавливает seed в памяти, смена роли данные сохраняет', async () => {
    const { db } = await import('@/mocks/db/store')
    const session = await setup()
    await session.loginDemo('fc_owner')
    const org = db.data.organizations.find((o) => o.name === 'СкладПро Фулфилмент')
    expect(org).toBeTruthy()
    const original = org!.name
    db.upsert('organizations', { ...org!, name: 'Изменённый ФЦ' })
    expect(db.data.organizations.find((o) => o.id === org!.id)?.name).toBe('Изменённый ФЦ')

    await session.loginDemo('fc_picker')
    expect(session.role).toBe('fc_picker')
    expect(db.data.organizations.find((o) => o.id === org!.id)?.name).toBe('Изменённый ФЦ')

    const dest = await session.resetDemo()
    expect(dest).toBe('home')
    expect(db.data.organizations.find((o) => o.id === org!.id)?.name).toBe(original)
    expect(session.role).toBe('fc_picker')
    expect(session.isDemo).toBe(true)
  })

  it('выход из демо восстанавливает боевую сессию', async () => {
    const session = await setup()
    await session.login({ email: 'seller@demo.ru', password: 'demo' })
    const real = session.realToken
    await session.loginDemo('seller_owner')
    expect(session.isDemo).toBe(true)
    const dest = await session.logout()
    expect(dest).toBe('home')
    expect(session.isDemo).toBe(false)
    expect(sessionStorage.getItem('wms.demo.active')).toBeNull()
    expect(session.token).toBe(real)
    expect(session.user?.email).toBe('seller@demo.ru')
    expect(session.user?.isDemo).toBe(false)
  })
})
