# Fulfillment WMS — бэкенд

ASP.NET Core (.NET 10 LTS) + PostgreSQL + EF Core. API повторяет контракт MSW-моков фронта (`frontend/src/mocks`),
чтобы фронт переключался с моков без переделок.

## Архитектура (Clean Architecture)

```
src/
  Wms.Domain          сущности и доменные правила, без внешних зависимостей
  Wms.Application     сценарии (handlers), интерфейсы портов, DTO, валидация  → Domain
  Wms.Infrastructure  EF Core/Npgsql, миграции, репозитории, JWT, хеширование, сид → Application
  Wms.Api             тонкие minimal-API эндпоинты, ProblemDetails, авторизация, CORS → Application, Infrastructure (только DI)
tests/
  Wms.Domain.Tests, Wms.Application.Tests       юнит-тесты
  Wms.Api.IntegrationTests                      WebApplicationFactory + Postgres (Testcontainers)
```

Зависимости направлены только внутрь; реализации подключаются через DI (`AddApplication`, `AddInfrastructure`).

## Запуск

Требуется .NET 10 SDK и Docker.

```bash
cd backend
docker compose up -d                 # PostgreSQL 17 на localhost:5432 (wms/wms)
dotnet run --project src/Wms.Api     # http://localhost:5080, миграции и сид применяются при старте
```

Фронт против бэкенда (в `frontend/.env.local`):

```
VITE_USE_MOCKS=false
VITE_API_BASE_URL=http://localhost:5080/api
```

CORS по умолчанию разрешён для `http://localhost:5173` (`Cors:Origins` в `appsettings.json`).

### Демо-пользователи (пароль у всех `demo`, как в моках)

| Email | Роль |
|---|---|
| admin@demo.ru | platform_admin |
| director@demo.ru | fc_owner (СкладПро Фулфилмент) |
| manager@demo.ru | fc_manager |
| picker@demo.ru | fc_picker |
| seller@demo.ru | seller_owner |
| owner1@pending.demo.ru | организация на проверке |

Снимок организаций и пользователей — `src/Wms.Infrastructure/Seeding/Data/identity.json`, сгенерирован из
`frontend/src/mocks/db/seed.ts` (`seed()` детерминирован, seed 20261002).

## Тесты

```bash
dotnet test                                     # интеграционные поднимут Postgres через Testcontainers (нужен Docker)
WMS_TEST_CONNECTION="Host=localhost;Username=wms;Password=wms" dotnet test   # или существующий Postgres (нужны права CREATE DATABASE)
```

## Миграции

```bash
dotnet tool install -g dotnet-ef
dotnet ef migrations add <Name> -p src/Wms.Infrastructure -s src/Wms.Api -o Persistence/Migrations
dotnet ef database update -p src/Wms.Infrastructure -s src/Wms.Api
```

## Контракт

- База `/api`, JSON camelCase, enum — snake_case строки (`fc_owner`, `in_transit`).
- `Authorization: Bearer <JWT>`; организация выбирается заголовком `X-Organization-Id` (иначе первое членство;
  у `platform_admin` организации нет).
- Ошибки — RFC 7807 ProblemDetails `{ type, title, status, detail?, errors? }`.
- Списки — `Paged<T> { items, total, page, pageSize }`, параметры `page, pageSize, search, sortField, sortOrder`.
- Авторизация по ролям — политики `PlatformAdmin`, `Fulfillment`, `FulfillmentManagement`, `Worker`, `Seller`, `AnyMember`.

| Метод | Путь | Ответ |
|---|---|---|
| POST | /api/auth/login | 200 `{ accessToken, user }` / 401 |
| POST | /api/auth/register | 201 `{ accessToken, user }`, организация `pending` / 400 / 409 |
| GET | /api/auth/me | 200 `CurrentUser` / 401 |
| GET | /health | 200 |

Ключ подписи JWT в `appsettings.json` — только для разработки; в окружениях задавайте `Jwt__SigningKey`.

## Отклонения от моков (PR2: организации, справочники, настройки)

- Метрики складов/заказов/остатков в карточках и обзоре пока нулевые (`NullOperationalMetrics`) — появятся в PR3+.
- Аудит, уведомления и поиск наполняются только из реализованных доменов (организации, справочники, пользователи); остальные источники — в следующих PR.
- `reset-password` реально меняет хэш пароля (мок только возвращает временный пароль).
- При одобрении организации без настроек создаются настройки по умолчанию (мок отдаёт 404).
- Неизвестное значение enum-фильтра в списках игнорируется (как в моке, без 400).
- Заявки клиентов сидируются относительно момента сида; базовая дата сида — 2026-10-01.
- Даты сериализуются в UTC с суффиксом `Z` (как в моке).
- Заблокированные пользователи могут входить — как в моке.

## Отклонения от моков (PR3: склады, зоны, ячейки, каталог)

- `POST /api/warehouses/{id}/zones` с неизвестным `type` → 400 «Неизвестный тип зоны» (мок принимает любое значение).
- Фильтр `occupancy` ячеек повторяет мок: `empty`, `full`, любое другое непустое значение — «частично заполненные».
- `sold30d`, `orders30d` и входящие объёмы пока 0 — заполнятся в PR с заказами/приёмкой; `productsCount`, остатки и резервы — реальные (резервы из сида).
- Идентификаторы новых товаров — `prd_{suffix}`.
