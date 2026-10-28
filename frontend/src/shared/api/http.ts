import type { ProblemDetails } from './types'

type QueryValue = string | number | boolean | null | undefined
export type QueryParams = Record<string, QueryValue>

interface HttpConfig {
  getToken: () => string | null
  getOrganizationId: () => string | null
  onUnauthorized: () => void
}

let config: HttpConfig = {
  getToken: () => null,
  getOrganizationId: () => null,
  onUnauthorized: () => {},
}

export function configureHttp(next: HttpConfig) {
  config = next
}

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails

  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail ?? problem.title)
    this.status = status
    this.problem = problem
  }
}

const baseUrl = import.meta.env.VITE_API_BASE_URL ?? '/api'

function buildUrl(path: string, query?: QueryParams) {
  const url = new URL(baseUrl + path, window.location.origin)
  if (query) {
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== '') url.searchParams.set(key, String(value))
    }
  }
  return url.toString()
}

const MOCKS = import.meta.env.VITE_USE_MOCKS === 'true'

/** Ответ обработчика MSW всегда JSON (или 204); всё остальное — проход мимо моков */
function fromMock(response: Response) {
  if (response.status === 204) return true
  return (response.headers.get('content-type') ?? '').includes('json')
}

async function waitForMockWorker(attempt: number) {
  if ('serviceWorker' in navigator) {
    await Promise.race([navigator.serviceWorker.ready, new Promise((r) => setTimeout(r, 1000))])
  }
  await new Promise((r) => setTimeout(r, 250 * (attempt + 1)))
}

interface RequestOptions {
  query?: QueryParams
  body?: unknown
  signal?: AbortSignal
}

async function request<T>(method: string, path: string, options: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  const token = config.getToken()
  const organizationId = config.getOrganizationId()
  if (token) headers.Authorization = `Bearer ${token}`
  if (organizationId) headers['X-Organization-Id'] = organizationId
  if (options.body !== undefined) headers['Content-Type'] = 'application/json'

  const send = () =>
    fetch(buildUrl(path, options.query), {
      method,
      headers,
      body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
      signal: options.signal,
    })

  let response: Response
  try {
    response = await send()
    // Моки: если ответ пришёл не от MSW (dev-сервер отдал не-JSON 404 — service worker ещё/снова
    // не контролирует вкладку), ждём готовности worker и повторяем запрос (BUG-08)
    for (let attempt = 0; MOCKS && attempt < 3 && !fromMock(response); attempt++) {
      await waitForMockWorker(attempt)
      response = await send()
    }
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error
    throw new ApiError(0, { title: 'Нет соединения с сервером', status: 0 })
  }

  if (response.status === 401 && token) config.onUnauthorized()

  if (!response.ok) {
    let problem: ProblemDetails
    try {
      problem = (await response.json()) as ProblemDetails
    } catch {
      problem = { title: response.statusText || 'Ошибка запроса', status: response.status }
    }
    throw new ApiError(response.status, problem)
  }

  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

export const http = {
  get: <T>(path: string, query?: QueryParams, signal?: AbortSignal) =>
    request<T>('GET', path, { query, signal }),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, { body }),
  put: <T>(path: string, body?: unknown) => request<T>('PUT', path, { body }),
  patch: <T>(path: string, body?: unknown) => request<T>('PATCH', path, { body }),
  delete: <T>(path: string) => request<T>('DELETE', path),
}

export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) return error.problem.detail ?? error.problem.title
  if (error instanceof Error) return error.message
  return 'Неизвестная ошибка'
}
