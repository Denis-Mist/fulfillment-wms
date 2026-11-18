/** Окружение демо-e2e: всё из env, значения по умолчанию — локальный стек на коробке */
export const PORT = Number(process.env.E2E_DEMO_PORT ?? 5173)
export const BASE_URL = (process.env.E2E_DEMO_BASE_URL ?? `http://127.0.0.1:${PORT}`).replace(/\/$/, '')
export const API_URL = (process.env.E2E_DEMO_API_URL ?? process.env.VITE_API_BASE_URL ?? 'http://127.0.0.1:5080/api').replace(/\/$/, '')
/** Срок демо-токена, мин (Demo:TokenLifetimeMinutes) */
export const DEMO_TTL_MIN = Number(process.env.E2E_DEMO_TTL_MIN ?? 120)
/** Срок обычного токена, мин (Jwt) */
export const LOGIN_TTL_MIN = Number(process.env.E2E_DEMO_LOGIN_TTL_MIN ?? 720)

const PROD_RE = /korobfulfillment\.ru/i
export const IS_PROD = PROD_RE.test(BASE_URL) || PROD_RE.test(API_URL)
if (IS_PROD && process.env.E2E_DEMO_ALLOW_PROD !== '1') {
  throw new Error('Демо-e2e против прода запрещены: полный набор делает мутации. Для дымового прогона @prod-smoke задайте E2E_DEMO_ALLOW_PROD=1')
}
