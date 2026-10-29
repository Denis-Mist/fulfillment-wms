# QA бэкенда
- start-pg.sh: поднимает Postgres (embedded-postgres в ~/pg, порт 5432, роль wms/wms).
- reset-db.sh: пересоздаёт БД wms и перезапускает API (миграции и сид при старте), лог в api.log.
- api-test.mjs: проверки на реальном API (auth, JWT, регистрация, пагинация, переходы статусов inbound/fbo/returns/acts/tasks/work, валидация, длинные строки, блокировка организации, межтенантный доступ, CORS). Результат пишется в api-test.json. Запускать на свежей БД.
- diff-get.test.ts и diff-mut.test.ts: сравнение мока (MSW в Node) с реальным API. GET: 64 маршрута × 8 пользователей + anon. Мутации: 49 маршрутов × 9 пользователей × 2 вида тела. Запуск: `./run-diff.sh diff-get`, результат в diff-*.json.
