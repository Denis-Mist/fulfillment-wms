import type { RequestHandler } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll } from 'vitest'

const modules = import.meta.glob<Record<string, unknown>>('../../src/mocks/handlers/*.ts', { eager: true })
export const handlers = Object.values(modules)
  .flatMap((module) => Object.values(module))
  .filter((value): value is RequestHandler[] => Array.isArray(value))
  .flat()

export const server = setupServer(...handlers)

/**
 * MSW в Node поверх тех же обработчиков, что и в браузере. БД моков — синглтон модуля:
 * vitest изолирует модули по файлам, внутри файла тесты используют уникальные данные.
 */
export function useMockServer() {
  beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))
  afterEach(() => {
    server.resetHandlers()
    localStorage.clear()
  })
  afterAll(() => server.close())
}

/** fetch к мок-API с токеном демо-пользователя */
export async function api(path: string, init: RequestInit & { token?: string; json?: unknown } = {}) {
  const headers = new Headers(init.headers)
  if (init.token) headers.set('Authorization', `Bearer ${init.token}`)
  if (init.json !== undefined) headers.set('Content-Type', 'application/json')
  const res = await fetch(`http://localhost/api${path}`, {
    ...init,
    headers,
    body: init.json !== undefined ? JSON.stringify(init.json) : init.body,
  })
  const text = await res.text()
  return { status: res.status, body: text ? JSON.parse(text) : null }
}

export async function login(email: string, password = 'demo') {
  const res = await api('/auth/login', { method: 'POST', json: { email, password } })
  if (res.status !== 200) throw new Error(`login ${email}: ${res.status}`)
  return res.body as { accessToken: string; user: { memberships: { organizationId: string }[] } }
}
