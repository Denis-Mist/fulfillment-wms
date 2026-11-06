# Этап 2 — фронт FR UI на реальном API — ЗАКРЫТ

**SHA FE:** `3f83a95` (Merge PR #40)  
**SHA BE:** `aeab372` (Merge PR #41, фиксы BE-FR-1…4)  
**Дата:** 2026-11-06 Europe/Moscow  
**Режим:** `VITE_USE_MOCKS=false`, `VITE_API_BASE_URL=http://localhost:5080/api`, Vite `:5173`

## Retest бэка PR #41 (вклинился в этап 2)
Все **BE-FR-1…4 ЗАКРЫТЫ** — см. `/workspace/wms-qa/backend/bugs-fr.md` § Retest PR #41 и `backend/fr/retest-pr41.log`.

## UI-сценарии FR (director@demo.ru), ширины 1280 / 768 / 360
Скрипт: `stage2/fr/ui-final.mjs` → **36/36 PASS** (`fr/results.json`, `fr/logs/ui-final.log`)

| Область | Результат |
|---|---|
| Forecast: KPI, staffing, chart, MA↔HW, approve | PASS (approve 200 + тост) |
| Slotting: ABC↔affinity, select-all, apply | PASS (apply 200, тост «Принято рекомендаций: N») |
| KPI tab | PASS (GET /analytics/kpi 200) |
| Picking Автопартии + create | PASS (POST /picking/batches/auto 201); на 360 после исчерпания кандидатов кнопка disabled — OK |
| Picking Вручную preview | PASS (или строки без preview API после auto — UI без ошибок) |
| Tasks: смена/навыки, auto-assign, drawer | PASS (auto-assign 200) |
| pageerror / 5xx / page overflow | PASS на всех ширинах |

Скриншоты: `stage2/fr/screens/F_*.png`

## Регрессия
| Набор | Результат | Лог |
|---|---|---|
| Playwright e2e на реальном API | **35/35** | `fr/logs/e2e-real2.log` |
| vitest (моки) | **68/68** (6 файлов) | `fr/logs/vitest.log` |

Первый параллельный e2e/test:all давал ложные FAIL (`ENOENT` trace artifacts) — гонка двух Playwright, не продукт. Повтор в одиночку — чисто.

## Баги
- **Фронт:** новых нет → `stage2/bugs-fr-fe.md` (пусто по дефектам)
- **Бэк:** BE-FR-1…4 закрыты на aeab372; новых BE на этапе 2 не найдено

## Ограничения
- Docker/Testcontainers по-прежнему нет (интеграционные .NET тесты не гонялись в этапе 2)
- Краткая регрессия приёмка/FBO/work в этом прогоне не углублялась (покрыта e2e smoke 35/35)
