import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useMockServer } from './setup-msw'
import { configureHttp } from '@/shared/api/http'
import { useSessionStore } from '@/stores/session'

useMockServer()

async function setup(email?: string) {
  setActivePinia(createPinia())
  const session = useSessionStore()
  configureHttp({ getToken: () => session.token || null, getOrganizationId: () => session.organizationId || null, onUnauthorized: () => {} })
  if (email) await session.login({ email, password: 'demo' })
  const { router } = await import('@/app/router')
  return { router, session }
}

describe('router guard', () => {
  beforeEach(() => localStorage.clear())

  it('гость → /login с redirect', async () => {
    const { router } = await setup()
    await router.push('/fc/orders')
    expect(router.currentRoute.value.name).toBe('login')
    expect(router.currentRoute.value.query.redirect).toBe('/fc/orders')
  })

  it.each([
    ['director@demo.ru', 'fc-dashboard'],
    ['seller@demo.ru', 'seller-dashboard'],
    ['admin@demo.ru', 'admin-dashboard'],
    ['picker@demo.ru', 'worker-tasks'],
    ['owner1@pending.demo.ru', 'pending'],
  ])('%s: / → %s', async (email, name) => {
    const { router } = await setup(email)
    await router.push('/')
    expect(router.currentRoute.value.name).toBe(name)
  })

  it('чужой кабинет → главная своего с ?denied', async () => {
    const { router } = await setup('seller@demo.ru')
    await router.push('/fc/staff')
    expect(router.currentRoute.value.name).toBe('seller-dashboard')
    expect(router.currentRoute.value.query.denied).toBe('/fc/staff')
  })

  it('раздел не по роли → ?denied (старший смены → биллинг)', async () => {
    const { router } = await setup('staff8@fc1.demo.ru')
    await router.push('/fc/billing')
    expect(router.currentRoute.value.name).toBe('fc-dashboard')
    expect(router.currentRoute.value.query.denied).toBe('/fc/billing')
  })

  it('авторизованный на /login → главная', async () => {
    const { router } = await setup('director@demo.ru')
    await router.push('/login')
    expect(router.currentRoute.value.name).toBe('fc-dashboard')
  })

  it('неизвестный путь → not-found', async () => {
    const { router } = await setup('director@demo.ru')
    await router.push('/nope/xyz')
    expect(router.currentRoute.value.name).toBe('not-found')
  })
})
