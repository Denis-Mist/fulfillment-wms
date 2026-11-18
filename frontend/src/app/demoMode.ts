/** Флаг вкладки и persisted-ключ из SPEC-v2 §3.2–3.4 */
export const DEMO_FLAG = 'wms.demo.active'
export const DEMO_PERSONA_KEY = 'wms.demo.persona'
export const DEMO_HEADER = 'X-WMS-Demo'

export const DEMO_SESSION_KEYS = {
  token: 'wms.demo.token',
  organizationId: 'wms.demo.organizationId',
  warehouseId: 'wms.demo.warehouseId',
} as const

export const REAL_SESSION_KEYS = {
  token: 'wms.token',
  organizationId: 'wms.organizationId',
  warehouseId: 'wms.warehouseId',
} as const

export function isAlwaysMocks(): boolean {
  return import.meta.env.VITE_USE_MOCKS === 'true'
}

/** Источник правды «эта вкладка в демо». sessionStorage, чтобы соседняя боевая вкладка не переключилась. */
export function isDemoTab(): boolean {
  try {
    return sessionStorage.getItem(DEMO_FLAG) === '1'
  } catch {
    return false
  }
}

export function enterDemoTab(): void {
  sessionStorage.setItem(DEMO_FLAG, '1')
  // Защита от остатка флага из релиза 411df4f, который писал его и в localStorage (§12: только sessionStorage).
  try {
    localStorage.removeItem(DEMO_FLAG)
  } catch {
    /* private mode */
  }
}

export function leaveDemoTab(): void {
  sessionStorage.removeItem(DEMO_FLAG)
  try {
    localStorage.removeItem(DEMO_FLAG)
  } catch {
    /* private mode */
  }
}

export function shouldStartMocksOnBoot(): boolean {
  return isAlwaysMocks() || isDemoTab()
}

/** Повторять запрос, пока не ответит MSW (и в dev, и в рантайм-демо). */
export function expectMockResponse(): boolean {
  return isAlwaysMocks() || isDemoTab()
}

/** При VITE_USE_MOCKS=true — всегда (§12 Q-3); иначе только демо-вкладка. Боевая вкладка заголовок не ставит — handlers делают passthrough. */
export function shouldSendDemoHeader(): boolean {
  return isAlwaysMocks() || isDemoTab()
}

export function isMockIntercept(request: Request): boolean {
  if (isAlwaysMocks()) return true
  return request.headers.get(DEMO_HEADER) === '1'
}

export function readDemoPersona(): string | null {
  try {
    return localStorage.getItem(DEMO_PERSONA_KEY)
  } catch {
    return null
  }
}

export function writeDemoPersona(persona: string | null): void {
  if (persona) localStorage.setItem(DEMO_PERSONA_KEY, persona)
  else localStorage.removeItem(DEMO_PERSONA_KEY)
}
