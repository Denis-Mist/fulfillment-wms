# Что стало неактуальным из e2e демо v1 (PR #52)

SPEC-v2 заменяет SPEC v1: демо не ходит в серверную БД, `POST /api/auth/demo` на сервере — вечный 404 или удалён,
`Demo__Enabled` на проде не включается, серверный сброс/TRUNCATE упразднён (§3.6, §4, §7: «Серверный
`POST /api/auth/demo` и ночной сброс больше не являются критериями приёмки»).

Удалено целиком:

- `api.spec.ts` — контракт серверного демо: `GET /api/auth/demo/status` → `{enabled:true}`, 404 при выключенном
  флаге, 422 на неизвестную персону, JWT с claim `demo` и TTL `Demo:TokenLifetimeMinutes`, вход персон в
  канонических сидов прод-БД, rate limit `POST /auth/demo` (10/мин, 60/ч), стоп-лист 403 «В демо-режиме это действие
  недоступно» на серверных мутациях.
- `api.ts` — помощники запросов к серверному `/auth/demo` и вход по паролю `demo` в сидов.
- `ui.spec.ts` — заменён на `entry/personas/lifecycle/isolation.spec.ts`. Выкинуто:
  - «Демо временно выключено» по `status.enabled=false` и ответы 429/503 на `POST /auth/demo` (в v2 нет серверного
    выключателя; вместо этого — ошибка старта SW, §2.4);
  - «Сменить роль заменяет сессию в `wms.token`» (в v2 демо-сессия в отдельных ключах, `wms.token` не трогается, §3.3);
  - «Выйти → `wms.token` удалён» (в v2 боевая сессия восстанавливается, §2.2);
  - стоп-лист UI у демо-админа (неактивные «Сбросить пароль», «Одобрить/Отклонить» → тост «демо»): в v2 стоп-лист
    ослаблен, теперь проверяется обратное (§2.3);
  - прямой `fetch` reset-password с демо-JWT → 403;
  - вход по паролю `director@demo.ru`/`admin@demo.ru` на реальном API (сидовые пароли — дыра, §1.1, §5).
- `global-setup.ts`: проверка `Demo__Enabled=true` через `/auth/demo/status` и прогрев кабинетов входом по паролю
  `demo` — убраны; осталась только проверка `/health` реального API.
- `fixtures.ts`: подстановка `X-Forwarded-For` через `page.route` (нужна была для лимита `POST /auth/demo`) — убрана,
  `page.route` на `/api` мешал бы service worker. XFF для login/register теперь ставит прокси `vite preview`.
- `env.ts`: `DEMO_TTL_MIN`, `LOGIN_TTL_MIN`, Vite dev с абсолютным `VITE_API_BASE_URL` на :5080 — заменено
  production-like сборкой с `VITE_API_BASE_URL=/api`.

Сохранено и адаптировано: 11 персон и их кабинеты/подписи баннера (`personas.ts`), кнопка на `/login` и отсутствие
сетки паролей, 11 карточек и адаптив `/demo`, меню `fc_account_manager`, баннер на ТСД 390px, `@prod-smoke` только
по `E2E_DEMO_ALLOW_PROD=1`.
