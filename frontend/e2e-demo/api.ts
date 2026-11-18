import { request, type APIRequestContext, type APIResponse } from '@playwright/test'
import { API_URL, IS_PROD } from './env'
import { type Persona } from './personas'

/**
 * Лимит POST /api/auth/demo — 10/мин и 60/час на IP (SPEC §2.5). Набор входит в демо больше 10 раз,
 * поэтому каждому сценарию даём свой «клиентский» IP через X-Forwarded-For: API доверяет ему только
 * от известного прокси 127.0.0.1, т.е. ровно при локальном прогоне. Отключить: E2E_DEMO_XFF=0.
 */
export const USE_XFF = process.env.E2E_DEMO_XFF !== '0' && !IS_PROD
let ipSeq = Math.floor(Math.random() * 200)
export function nextClientIp() {
  ipSeq++
  return `10.77.${Math.floor(ipSeq / 250) % 250}.${(ipSeq % 250) + 1}`
}

export interface AuthResponse {
  accessToken: string
  user: {
    id: string
    email: string
    isPlatformAdmin: boolean
    isDemo?: boolean
    memberships: { organizationId: string; role: string; organizationStatus?: string; status?: string }[]
  }
}

export interface Session {
  token: string
  organizationId?: string
  auth: AuthResponse
}

export async function apiContext(ip = nextClientIp()): Promise<APIRequestContext> {
  return request.newContext({
    extraHTTPHeaders: { Accept: 'application/json', ...(USE_XFF ? { 'X-Forwarded-For': ip } : {}) },
  })
}

export const url = (path: string) => `${API_URL}${path}`

export async function demoLogin(api: APIRequestContext, persona: Persona | string): Promise<APIResponse> {
  return api.post(url('/auth/demo'), { data: { persona } })
}

export async function demoSession(api: APIRequestContext, persona: Persona): Promise<Session> {
  const res = await demoLogin(api, persona)
  if (res.status() !== 200) throw new Error(`POST /auth/demo ${persona} → ${res.status()} ${await res.text()}`)
  const auth = (await res.json()) as AuthResponse
  return { token: auth.accessToken, organizationId: auth.user.memberships[0]?.organizationId, auth }
}

export async function passwordSession(api: APIRequestContext, email: string, password = 'demo'): Promise<Session> {
  const res = await api.post(url('/auth/login'), { data: { email, password } })
  if (res.status() !== 200) throw new Error(`POST /auth/login ${email} → ${res.status()} ${await res.text()}`)
  const auth = (await res.json()) as AuthResponse
  return { token: auth.accessToken, organizationId: auth.user.memberships[0]?.organizationId, auth }
}

export function authHeaders(s: Session, organizationId = s.organizationId): Record<string, string> {
  return { Authorization: `Bearer ${s.token}`, ...(organizationId ? { 'X-Organization-Id': organizationId } : {}) }
}

/** Вызов API от имени сессии */
export async function call(api: APIRequestContext, s: Session, method: string, path: string, data?: unknown) {
  return api.fetch(url(path), { method, headers: authHeaders(s), data: data === undefined && method !== 'GET' ? {} : data })
}

/** Полезная нагрузка JWT (без проверки подписи) */
export function jwtPayload(token: string): Record<string, unknown> {
  const part = token.split('.')[1] ?? ''
  return JSON.parse(Buffer.from(part.replace(/-/g, '+').replace(/_/g, '/'), 'base64').toString('utf8')) as Record<string, unknown>
}
