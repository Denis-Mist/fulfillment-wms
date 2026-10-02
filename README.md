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