/**
 * Окружение e2e демо-режима v2 (SPEC-v2 §7, §11.4). Всё из env, по умолчанию — локальный стенд на коробке.
 *
 * Фронт — production-like сборка (`vite build` с `VITE_USE_MOCKS=false`, `VITE_API_BASE_URL=/api`) за `vite preview`,
 * `/api` проксируется на реальный API (`E2E_DEMO_API_TARGET`). Демо при этом живёт только в браузере (MSW + localStorage).
 */
export const PORT = Number(process.env.E2E_DEMO_PORT ?? 4180)
export const BASE_URL = (process.env.E2E_DEMO_BASE_URL ?? `http://127.0.0.1:${PORT}`).replace(/\/$/, '')
/** Куда vite preview проксирует `/api` (реальный бэкенд стенда тестов) */
export const API_TARGET = (process.env.E2E_DEMO_API_TARGET ?? 'http://127.0.0.1:5080').replace(/\/$/, '')
/**
 * Базовый URL реального API для прямых проверок из Node (регистрация тестового пользователя, проверка, что запрос
 * действительно дошёл до сервера). Для внешнего фронта (`E2E_DEMO_BASE_URL`) по умолчанию — тот же origin.
 */
export const REAL_API_URL = (
  process.env.E2E_DEMO_API_URL ?? (process.env.E2E_DEMO_BASE_URL ? `${BASE_URL}/api` : `${API_TARGET}/api`)
).replace(/\/$/, '')
/** Путь API, каким его видит фронт (`VITE_API_BASE_URL` в сборке) */
export const API_PATH = '/api'

const PROD_RE = /korobfulfillment\.ru/i
export const IS_PROD = PROD_RE.test(BASE_URL) || PROD_RE.test(REAL_API_URL)
if (IS_PROD && process.env.E2E_DEMO_ALLOW_PROD !== '1') {
  throw new Error(
    'Демо-e2e против прода запрещены: полный набор регистрирует пользователей в БД. ' +
      'Выборочная проверка после выкладки — только тесты @prod-smoke и только с E2E_DEMO_ALLOW_PROD=1',
  )
}

/** Подставлять ли X-Forwarded-For в прямые запросы к API (rate limit login/register — 20/мин на IP) */
export const USE_XFF = process.env.E2E_DEMO_XFF !== '0'
