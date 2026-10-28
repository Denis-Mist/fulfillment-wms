# fulfillment-wms

Monorepo WMS / fulfillment.

## Структура

```
fulfillment-wms/
├── .cursor/rules/          — маршрутизация задач (workspace)
├── backend/                — API и бизнес-логика (.ai/, .cursor/rules/, AGENTS.md)
└── frontend/               — UI (.ai/, .cursor/rules/, AGENTS.md)
```

Правила для AI — по образцу `work_bpo`: долгосрочная память в `.ai/`, Cursor rules в `.cursor/rules/`.

Открывай в Cursor корень `fulfillment-wms` или отдельно `backend/` / `frontend/` — подтянутся соответствующие project rules.
## Тесты

Все тесты работают на мок-API (MSW), бэкенд не нужен. Требуется Node.js ≥ 22.12 (MSW 3).

```bash
cd frontend
npm ci
npx playwright install chromium   # один раз: браузер для e2e (в Linux CI — с флагом --with-deps)

npm test              # unit-тесты Vitest (однократно)
npm run test:watch    # Vitest в режиме наблюдения
npm run test:e2e      # e2e Playwright (сам поднимет vite на 127.0.0.1:5174)
npm run test:e2e:ui   # Playwright UI-режим
npm run test:all      # всё подряд
```

Отчёт e2e: `npx playwright show-report` (папка `frontend/playwright-report`).

**Что покрыто**

- `tests/unit` (Vitest, jsdom, MSW в Node на тех же обработчиках, что и в браузере):
  - роли и кабинеты (`roles.ts`), охранник роутера: гость → `/login?redirect`, чужой раздел → `?denied`, организация на модерации → `/pending`;
  - `useFormValidation` (показ ошибок, серверные 409 на поле), форматтеры (`format.ts`);
  - мок-хендлеры: вход/401, регистрация (400/409/201), изоляция данных селлера, 403 по ролям, валидация и дубли зон, ячеек, справочников;
  - `mocks/lib/http` (пагинация, сортировка, параметры списков), политика повторов vue-query.
- `e2e` (Playwright, Chromium; каждый тест — чистый контекст и пустой localStorage, т. е. исходные мок-данные):
  - smoke: все 10 демо-ролей × все пункты меню — без `pageerror`/`console.error`, есть заголовок;
  - RBAC: прямой URL в запрещённый раздел → редирект на главную и тост «Нет доступа к разделу»;
  - состояния 500 (ErrorState + «Повторить») и пустых данных;
  - адаптивность 360/390/768/1024/1440: нет горизонтального скролла, кнопки ≥ 44 px на мобильных;
  - формы: вход, регистрация в 3 шага, FBO-поставка, товар, зона, ячейка;
  - светлая и тёмная темы.

CI: `.github/workflows/frontend-tests.yml` — на pull request и push в `main`: `npm ci` → eslint → vue-tsc → build → vitest → playwright (отчёт — артефакт `playwright-report`).
