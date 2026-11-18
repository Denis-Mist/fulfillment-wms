# E2E демо-режима v2 (`test:e2e:demo`)

Спецификация: `SPEC-v2` (демо целиком в браузере на MSW + `localStorage`), §7 «Тестирование» и §11.4.
Демо больше не ходит в серверную БД; серверные `/api/auth/demo*`, стоп-лист 403 и ночной сброс — не критерии
приёмки. Что выкинуто из v1 — см. [OBSOLETE.md](./OBSOLETE.md).

Основной контур (`playwright.config.ts`, `e2e/`, `VITE_USE_MOCKS=true`, CI `frontend-tests`) этот набор не трогает.

## Стенд

- **Фронт** — production-like сборка, как на проде: `vite build` с `VITE_USE_MOCKS=false`, `VITE_API_BASE_URL=/api`,
  затем `vite preview` (`vite.e2e-demo.config.ts`), где `/api` и `/health` проксируются на реальный API. Это делает
  `webServer` в `playwright.demo.config.ts`; сборка — в `node_modules/.cache/e2e-demo/dist`.
- **Реальный API** стенда тестов (по умолчанию `http://127.0.0.1:5080`) с обычной локальной БД. `Demo__Enabled` **не
  нужен**. Сценарии «выход из демо → реальный логин/регистрация» регистрируют одноразовых пользователей
  `e2e-demo-v2-*@example.com` (организации в статусе pending) — поэтому только стенд тестов, не прод.
- Сидовые пароли не используются.

## Запуск

```bash
cd frontend
npm run test:e2e:demo:list   # перечислить сценарии (без сборки и без API)
npm run test:e2e:demo        # полный прогон: сборка + preview + тесты, API должен быть поднят
```

| env | по умолчанию | смысл |
|---|---|---|
| `E2E_DEMO_API_TARGET` | `http://127.0.0.1:5080` | куда preview проксирует `/api`, `/health` |
| `E2E_DEMO_API_URL` | `${E2E_DEMO_API_TARGET}/api` | реальный API для прямых проверок из Node |
| `E2E_DEMO_PORT` | `4180` | порт `vite preview` |
| `E2E_DEMO_BASE_URL` | — | готовый фронт (сборка не запускается) |
| `E2E_DEMO_REUSE_SERVER` | — | `1` — переиспользовать уже запущенный preview |
| `E2E_DEMO_XFF` | вкл. | `0` — не подставлять `X-Forwarded-For` (rate limit login/register 20/мин на IP) |
| `E2E_DEMO_SKIP_PRECHECK` | — | `1` — не проверять `/health` API перед прогоном |

Артефакты падений: `node_modules/.cache/e2e-demo/test-results`, отчёт — `playwright-report-demo/`.

## Контракт

Имена, на которые опираются тесты, собраны в `contract.ts`: флаг `wms.demo.active`, заголовок `X-WMS-Demo: 1`,
боевые ключи `wms.token`/`wms.organizationId`/`wms.warehouseId`, overlay `wms.mockdb.v2`/`wms.mock.*`, тексты UI.
Демо-токен ищется по значению `mock.demo:…`, имя ключа не зашито (в спеке — «например `wms.demo.token`»).

Сквозной страж `demoLeaks` (fixtures.ts) валит любой тест, если запрос с `X-WMS-Demo` или мок-токеном дошёл до
реального API (MSW `passthrough()` или вкладка без SW).

## Прод (только после выкладки и только явно)

Полный набор против `korobfulfillment.ru` запрещён (`env.ts` бросает ошибку). Выборочная проверка — тесты
`@prod-smoke` (только чтение, вход в демо обслуживает MSW в браузере):

```bash
E2E_DEMO_ALLOW_PROD=1 E2E_DEMO_BASE_URL=https://wms.korobfulfillment.ru npm run test:e2e:demo
```

TgSearch проверяется вручную на сервере (контейнер `tgsearch` Up).
