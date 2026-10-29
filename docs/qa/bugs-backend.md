# Баги бэкенда (main 83857e9 → ретест ce077b2 / PR #30), этап 1, 29.10.2026

Окружение: box, Postgres 17 (embedded-postgres, без docker), .NET 10.0.403, API http://localhost:5080/api.
Ретест: `./start-pg.sh && ./reset-db.sh && node api-test.mjs` + `./run-diff.sh diff-get` / `./run-diff.sh diff-mut`.

| ID | Серьёзность | Кратко | Статус |
|---|---|---|---|
| BE-01 | Major | POST /auth/register: строка длиннее 256 символов → 500 (varchar(256), Npgsql 22001) | закрыт (ретест ce077b2) |
| BE-02 | Medium | POST /auth/register принимает некорректный email и пароль из 1 символа (201) | закрыт (ретест ce077b2) |
| BE-03 | Low | Без тела или с битым JSON эндпоинт отвечает 400 «Некорректный запрос» раньше проверки роли (403) | закрыт (ретест ce077b2) |
| BE-04 | Low | POST /picking/batches с пустым orderIds → 409 «Часть заказов уже в работе» | закрыт (ретест ce077b2) |
| BE-05 | Low | POST /analytics/forecast/approve с {} → 422 «Неизвестный алгоритм», мок даёт 200 (уточнить) | закрыт (ретест ce077b2) |
| BE-06 | Low | Нет ограничений длины: название товара, имя зоны, юр. адрес, email приглашения (5000 символов → 2xx) | закрыт (ретест ce077b2) |

## BE-01
Шаги: POST /api/auth/register `{organizationType:'seller', organization:{name:'x'*5000, inn, city}, user:{fullName, email, phone, password}}`. То же самое для user.fullName (5000 символов) и user.email (>256 символов).
Ожидаемое: 400/422 ProblemDetails с `errors` по полю.
Фактическое: 500 «Внутренняя ошибка сервера». В логе DbUpdateException → PostgresException 22001: value too long for type character varying(256).

## BE-02
Шаги: register с `user.email='not-an-email-abc'` или с `password='1'`.
Ожидаемое: 400/422 (формат email, минимальная длина пароля). Фронт эти поля проверяет, но сервер тоже должен.
Фактическое: 201, пользователь создан, токен выдан.

## BE-03
Шаги: под picker@demo.ru отправить `PUT /api/staff/zzz` без тела. Под seller отправить `POST /api/picking/batches` без тела, под director `POST /api/fbo` без тела.
Ожидаемое: 403 «Недостаточно прав», как в моке (сначала права, потом тело).
Фактическое: 400 «Некорректный запрос». Затронуто: PUT /staff/{id}, PUT /billing/tariffs/{id}, POST /fbo, /fbo/{id}/advance, /tasks/{id}/assign, /picking/preview, /picking/batches, /returns/{id}/inspect, /staff/{id}/shifts, /integrations/{ch}/connect, PUT /integrations/{ch}, /analytics/forecast/approve, /analytics/slotting/apply, /admin/references, /work/tasks/{id}/scan. Для организации на проверке (owner1@pending) тоже 400 вместо 403. С пустым `{}` права проверяются правильно.

## BE-04
Шаги: под director `POST /api/picking/batches {"orderIds":[]}` или `{}`.
Ожидаемое: 400/422 «Выберите заказы».
Фактическое: 409 «Часть заказов уже в работе», текст вводит в заблуждение. При этом /picking/preview с {} → 200.

## BE-05
Шаги: под director `POST /api/analytics/forecast/approve {}`.
Ожидаемое (по моку): 200. Если 422 сделано намеренно, нужно описать это в README в разделе отклонений.
Фактическое: 422 «Неизвестный алгоритм».

## BE-06
Шаги: POST /catalog/products с name из 5000 символов (seller); POST /warehouses/{id}/zones с name из 5000 символов; PUT /settings/profile с legalAddress из 5000 символов; POST /settings/team/invite с email из 5000+ символов.
Ожидаемое: 400/422 с разумным лимитом (например, 200–256 символов).
Фактическое: 201/200, сохраняется.


## Ретест после PR #30 (main ce077b2), 29.10.2026
- api-test.mjs: 248 проверок, 0 упало. Добавлены границы: 256/257 (товар name/category, зона name, справочник name, юр. адрес), 2000/2001 (apiKey интеграции), пароль 1/5/6/128/129, register 400 + `errors`, batches [] → 422 «Выберите заказы».
- diff-get: 584 сравнения, 9 расхождений (как до фикса: /orders/{id}, /returns/{id} мок 200 vs реальный 404 на мок-id, не баг).
- diff-mut: 882 сравнения, расхождений 165 → 62. Все 62: мок отвечает 500 (падает без тела/с {}), реальный API корректно отдаёт 2xx/4xx. Это дефекты моков, к бэкенду не относятся.
- dotnet test: 253 теста (97+87+69), 0 упало.
- BE-01…BE-06 закрыты. Новых багов бэкенда нет.
- Уточнение: юр. адрес относится к коротким полям (лимит 256), лимит 2000 есть только у comment/note/notes/message/reason/description/apiKey/token.
