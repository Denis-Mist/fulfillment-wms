/**
 * Контракт демо-режима v2, на который опираются тесты. Источник — /workspace/demo-mode/SPEC-v2.md.
 * Если фронт v2 выберет другие имена (в §3.3/§3.4 часть из них дана как «например»), правится только этот файл.
 */

/**
 * §3.2, §11.2, §12 (Q-1): признак «эта вкладка в демо» — ТОЛЬКО в sessionStorage (на вкладку), чтобы соседняя боевая
 * вкладка того же origin не переключилась в демо. В localStorage флага быть не должно. Читать — `demoFlag()` из fixtures.
 */
export const DEMO_FLAG_KEY = 'wms.demo.active'
/** Последняя выбранная персона (localStorage) — по ней «Сбросить данные» перезаходит текущей ролью */
export const DEMO_PERSONA_KEY = 'wms.demo.persona'
/** §3.3: ключи боевой сессии — вход в демо не должен их трогать */
export const REAL_TOKEN_KEY = 'wms.token'
export const REAL_SESSION_KEYS = ['wms.token', 'wms.organizationId', 'wms.warehouseId'] as const
/** §3.3: ключи демо-сессии (localStorage), фронт v2: src/app/demoMode.ts. Демо-токен ищем по значению mock.demo:… */
export const DEMO_TOKEN_KEY = 'wms.demo.token'
/** Мок-токены: `mock.<userId>` / `mock.demo:<userId>` (src/mocks/lib/http.ts) */
export const MOCK_TOKEN_PREFIX = 'mock.'
export const DEMO_TOKEN_PREFIX = 'mock.demo:'

/** §3.4, §11.2: заголовок каждого демо-запроса к /api; handlers без него делают passthrough */
export const DEMO_HEADER = 'x-wms-demo'
export const DEMO_HEADER_VALUE = '1'

/** §3.5: overlay мок-БД */
export const MOCKDB_KEY = 'wms.mockdb.v2'
export const MOCK_COLLECTION_PREFIX = 'wms.mock.'

/** §2.1–2.4, §3.7, §11.2: тексты UI */
export const TEXT = {
  tryDemo: 'Попробовать демо',
  loginHint: 'Аккаунт не нужен — выберите роль и посмотрите систему',
  /** Подпись в баннере (фронт v2, §2.3) */
  bannerHint: 'Это учебный стенд в браузере, на настоящую WMS не влияет',
  demoTitle: 'Попробуйте систему в демо',
  /** §2.1: данные личные, живут в этом браузере */
  demoPersonal: /личная песочница/i,
  /** §2.1, §3.7: про общий ночной сброс стенда больше не писать */
  demoNoNightly: /03:00|каждую ночь|ночн|стенд общий|данные общие/i,
  resetOnDemoPage: 'Сбросить демо-данные',
  bannerPrefix: /Вы в демо-режиме как/,
  switchRole: 'Сменить роль',
  resetData: 'Сбросить данные',
  logout: 'Выйти',
  /** §2.4 */
  swFailed: 'Не удалось запустить демо в этом браузере',
  toLogin: 'На страницу входа',
  /** v1: заглушка по ответу сервера `enabled=false` — в v2 её быть не должно */
  v1Disabled: 'Демо временно выключено',
  /** v1: блокировка мутаций стоп-листом */
  v1Forbidden: 'В демо-режиме это действие недоступно',
  noAccess: 'Нет доступа',
} as const

/** Признак JWT реального API (мок-токены начинаются с `mock.`) */
/** data-testid фронта v2 (оставлены только там, где роль/текст неоднозначны) */
export const TESTID = {
  banner: 'demo-banner',
  persona: (id: string) => `demo-persona-${id}`,
  tryDemo: 'try-demo',
  resetOnPage: 'demo-reset-page',
  startError: 'demo-start-error',
} as const

export const looksLikeJwt = (token: string | null | undefined) => !!token && /^eyJ[\w-]+\.[\w-]+\.[\w-]+$/.test(token)
