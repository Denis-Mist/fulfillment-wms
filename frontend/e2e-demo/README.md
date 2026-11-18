# E2E демо-режима (реальный бэкенд)

Сценарии по `SPEC демо-режима §4.3`: `/demo` и 11 карточек, вход ролями (кабинет + баннер), «Сменить роль»/«Выйти»,
меню и доступ `fc_account_manager`, стоп-лист демо-токена (API и UI), обычный вход без баннера, контракт `/api/auth/demo*`.

Отдельно от e2e на моках: свой конфиг `playwright.demo.config.ts`, каталог `e2e-demo/`, скрипт `npm run test:e2e:demo`.
`npm run test:e2e` / `test:all` и CI `frontend-tests` эти файлы не подхватывают.

## Локальный прогон

1. Postgres с чистой БД (сид создаётся при первом старте API на пустой БД).
2. API с включённым демо (CORS по умолчанию пускает `127.0.0.1:5173/5174`):

   ```bash
   cd backend
   Demo__Enabled=true Demo__TokenLifetimeMinutes=120 \
   dotnet run --project src/Wms.Api --urls http://127.0.0.1:5080
   ```

3. Тесты (Vite с `VITE_USE_MOCKS=false` поднимается сам на `E2E_DEMO_PORT`, по умолчанию 5173):

   ```bash
   cd frontend
   npm run test:e2e:demo
   ```

| env | по умолчанию | смысл |
|---|---|---|
| `E2E_DEMO_API_URL` | `http://127.0.0.1:5080/api` | базовый URL API (уходит и во `VITE_API_BASE_URL`) |
| `E2E_DEMO_PORT` | `5173` | порт Vite |
| `E2E_DEMO_BASE_URL` | — | готовый фронт; тогда Vite не стартует |
| `E2E_DEMO_REUSE_SERVER` | — | `1` — переиспользовать уже запущенный Vite (только если он с `VITE_USE_MOCKS=false`) |
| `E2E_DEMO_TTL_MIN` | `120` | ожидаемый `Demo:TokenLifetimeMinutes` |
| `E2E_DEMO_XFF` | вкл. | `0` — не подставлять `X-Forwarded-For` |

Лимит `POST /api/auth/demo` — 10/мин на IP. Тесты дают каждому сценарию свой «IP» через `X-Forwarded-For`
(API доверяет ему только от прокси `127.0.0.1`), поэтому API надо слушать на `127.0.0.1`, а не на `::1`.

Набор мутирует БД (попытки из стоп-листа, сохранение профиля обычным директором). После прогона с найденным
багом стоп-листа БД стоит пересоздать.

## Прод

Против `korobfulfillment.ru` полный набор запрещён (конфиг падает). Дымовой прогон — только тесты `@prod-smoke`
(чтение + один вход в демо), явно:

```bash
E2E_DEMO_ALLOW_PROD=1 E2E_DEMO_BASE_URL=https://wms.korobfulfillment.ru \
E2E_DEMO_API_URL=https://wms.korobfulfillment.ru/api npm run test:e2e:demo
```
