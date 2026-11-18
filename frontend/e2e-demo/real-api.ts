import { request, type APIRequestContext } from '@playwright/test'
import { randomBytes } from 'node:crypto'
import { REAL_API_URL, USE_XFF } from './env'

/**
 * Прямые запросы к РЕАЛЬНОМУ API стенда тестов (не через браузер и не через MSW). Нужны, чтобы проверить, что после
 * выхода из демо логин/регистрация действительно дошли до сервера (SPEC-v2 §7: «регистрация создаёт usr_r_* в БД
 * стенда тестов, не в localStorage»).
 *
 * Учётки создаются на лету с одноразовым паролем — сидовые пароли не используются и нигде не хранятся.
 */

let ipSeq = Math.floor(Math.random() * 200)
/** Свой «IP» на контекст: rate limit login/register по IP (API доверяет X-Forwarded-For только от loopback) */
export const nextClientIp = () => `10.77.${Math.floor(++ipSeq / 250) % 250}.${(ipSeq % 250) + 1}`

export async function realApi(): Promise<APIRequestContext> {
  return request.newContext({
    extraHTTPHeaders: USE_XFF ? { 'X-Forwarded-For': nextClientIp(), Accept: 'application/json' } : { Accept: 'application/json' },
  })
}

export interface RealAccount {
  email: string
  password: string
  fullName: string
  organizationName: string
}

/** Новые уникальные данные для регистрации (организация уходит на модерацию → кабинет /pending) */
export function newAccount(tag = 'reg'): RealAccount {
  const id = `${Date.now().toString(36)}${randomBytes(3).toString('hex')}`
  return {
    email: `e2e-demo-v2-${tag}-${id}@example.com`,
    password: `E2e-${randomBytes(9).toString('base64url')}`,
    fullName: 'Тест Демоизоляции',
    organizationName: `E2E демо v2 ${id}`,
  }
}

export interface AuthBody {
  accessToken: string
  user: { id: string; email: string; isDemo?: boolean }
}

/** Регистрация через реальный API: POST /auth/register → 201 */
export async function registerReal(api: APIRequestContext, account = newAccount()): Promise<{ account: RealAccount; auth: AuthBody }> {
  const res = await api.post(`${REAL_API_URL}/auth/register`, {
    data: {
      organizationType: 'seller',
      organization: { name: account.organizationName, inn: '7700000000', city: 'Москва', marketplaces: ['wb'] },
      user: { fullName: account.fullName, email: account.email, phone: '+7 900 000-00-00', password: account.password },
    },
  })
  if (res.status() !== 201 && res.status() !== 200) {
    throw new Error(`POST ${REAL_API_URL}/auth/register → ${res.status()}: ${await res.text()}`)
  }
  return { account, auth: (await res.json()) as AuthBody }
}

/** Логин через реальный API; возвращает статус и тело (для 401 — problem+json) */
export async function loginReal(api: APIRequestContext, email: string, password: string) {
  const res = await api.post(`${REAL_API_URL}/auth/login`, { data: { email, password } })
  return { status: res.status(), body: (await res.json().catch(() => null)) as AuthBody | null }
}

/** /auth/me реального API по токену */
export async function meReal(api: APIRequestContext, token: string) {
  const res = await api.get(`${REAL_API_URL}/auth/me`, { headers: { Authorization: `Bearer ${token}` } })
  return { status: res.status(), body: (await res.json().catch(() => null)) as { id: string; email: string; isDemo?: boolean } | null }
}

export async function healthReal(api: APIRequestContext) {
  const res = await api.get(REAL_API_URL.replace(/\/api$/, '') + '/health').catch((e: Error) => e)
  return res instanceof Error ? 0 : res.status()
}
