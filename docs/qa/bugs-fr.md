# Баги FR-12…17 (этап 1, бэкенд)
SHA: `a6ff1e2c938d68bc2f732a0760bc2b299f5d6c13` (main, Merge PR #39)
Дата: 2026-11-06 Europe/Moscow
Скрипт: `/workspace/wms-qa/backend/fr-intel-test.mjs`
Результаты: `/workspace/wms-qa/backend/fr/fr-intel-results.json`

---

## BE-FR-1 [Major] GET /analytics/forecast?algorithm=unknown → 200 вместо 422

**Endpoint:** `GET /api/analytics/forecast?algorithm=unknown`

**Шаги:**
1. Войти как `director@demo.ru` / `demo`
2. `GET /api/analytics/forecast?algorithm=unknown`

**Ожидаемое:** `422` (неизвестный algorithm), по спецификации FR-16.

**Фактическое:** `200`, `meta.algorithm = "holt_winters"` (тихий fallback).

**Причина:** `ListQuery.Enum<ForecastAlgorithm>` возвращает `null` на неизвестное значение; `ForecastAsync` подставляет `algorithmParam ?? ForecastAlgorithm.HoltWinters`.

**Фрагмент ответа:**
```json
{"meta":{"algorithm":"holt_winters","version":"2.0.0",...},"horizon":14,...}
```

**Аналогично:** `GET /analytics/slotting?algorithm=unknown` → 200 с `abc_affinity` (см. BE-FR-2).

---

## BE-FR-2 [Low] GET /analytics/slotting?algorithm=unknown → 200 вместо 422

**Endpoint:** `GET /api/analytics/slotting?warehouseId=wh_khm1&algorithm=unknown`

**Шаги:** как BE-FR-1, но slotting.

**Ожидаемое:** `422`.

**Фактическое:** `200`, `meta.algorithm = "abc_affinity"`.

**Причина:** тот же паттерн `Enum → null → default AbcAffinity`.

---

## BE-FR-3 [Major] POST /analytics/slotting/apply помечает несуществующие id как applied

**Endpoint:** `POST /api/analytics/slotting/apply`

**Шаги:**
1. Войти как `director@demo.ru`
2. `POST /api/analytics/slotting/apply` с телом `{"ids":["slot_does_not_exist"]}`
3. Проверить таблицу `slotting_applied` и ответ

**Ожидаемое:**
- ход не применяется;
- `applied: 0` (или 404/422 «рекомендация не найдена»);
- в `slotting_applied` нет фейкового id.

**Фактическое:**
```json
{"applied":1}
```
```sql
select id from slotting_applied;
-- slot_does_not_exist
```
При `{"ids":["<реальный>","slot_fake_xxx"]}` ответ `{"applied":2}`, в таблице оба id.

**Причина (AnalyticsHandler.ApplyAsync):**
```csharp
foreach (var id in ids) await TryApplyMoveAsync(...); // early return при ошибке
foreach (var id in ids)
    if (existing.TryGetValue(id, out var row)) row.Reapply(now);
    else db.SlottingApplied.Add(new SlottingApplied(id, now)); // всегда
return new ApplyResult(ids.Count); // всегда ids.Count, не число успешных
```
`TryApplyMoveAsync` для неизвестного префикса/`stock` просто `return`, но id всё равно пишется в `slotting_applied`.

**Риск:** ложный успех для клиента; засорение `slotting_applied`; реальный move с тем же id позже будет считаться уже applied.

---

## BE-FR-4 [Major] POST /tasks/{id}/assign с несуществующим userId → вводящий в заблуждение 422

**Endpoint:** `POST /api/tasks/{id}/assign`

**Шаги:**
1. Взять задание со статусом `new` (например packing `tsk_19`)
2. `POST /api/tasks/tsk_19/assign` с `{"userId":"usr_does_not_exist"}`

**Ожидаемое:** `404` «Сотрудник не найден» (или эквивалент).

**Фактическое:**
```json
{"type":"https://httpstatuses.io/422","title":"Сотрудник не может выполнять это задание","status":422,"detail":"Нужна роль: упаковка"}
```

**Причина:** `workers.RoleInAsync` для отсутствующего пользователя возвращает `null`, и ветка `role is null || !EligibleRoles` всегда отдаёт текст про роль, не отличая «нет пользователя» от «не та роль».

---

## Замечания (не заводил как баг / Low-наблюдения)

1. **POST /picking/preview с `orderIds:[]`** → `200` с пустым маршрутом (distance 0). `POST /picking/batches` с пустым списком корректно даёт `422`. Для preview поведение спорное; CreateBatch защищён.
2. **Невалидный query `status=unassigned`** у `GET /tasks` игнорируется (как и unknown algorithm) — отдаёт все статусы. Pre-existing паттерн ListQuery.Enum.
3. На маленьких батчах (2 заказа) `baselineDistance == optimizedDistance`; на preview из 4 заказов выигрыш есть (98 → 84). Алгоритм переключается (`algorithm` в задании = baseline|optimized).
4. KPI `utilization` и `errorRate` в сиде = 0 — похоже на отсутствие данных, не константа мока (orders=1053 из БД).


---

## Блокер среды (не баг продукта)
`Wms.Api.IntegrationTests` (7 тестов FR-related) не запускаются: Testcontainers требует Docker (`unix:///var/run/docker.sock`), Docker на box отсутствует. Domain 36/36 + Application 21/21 + fr-intel API + api-test 248/0 достаточны для этапа 1.

---

## Retest PR #41 (SHA `aeab3729569d85b53f137b4d75434ce824b1ff6c`) — 2026-11-06 Europe/Moscow

API пересобран и перезапущен на этом SHA. Лог: `fr/retest-pr41.log`.

| ID | Ожидание | Факт | Статус |
|---|---|---|---|
| BE-FR-1 | forecast?algorithm=unknown → 422 | 422 «Неизвестный алгоритм» | **ЗАКРЫТ** |
| BE-FR-2 | slotting?algorithm=unknown → 422 | 422 «Неизвестный алгоритм» | **ЗАКРЫТ** |
| BE-FR-3 | apply fake id → applied:0, нет в slotting_applied | `{"applied":0}`, 0 rows; real+fake → applied:1, только реальный id | **ЗАКРЫТ** |
| BE-FR-4 | assign usr_does_not_exist → 404 | 404 «Сотрудник не найден» | **ЗАКРЫТ** |

Регрессия sanity: forecast?algorithm=holt_winters → 200, slotting?algorithm=abc_affinity → 200.
