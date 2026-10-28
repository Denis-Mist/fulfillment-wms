import { http, HttpResponse } from 'msw'
import type {
  ErrorReason,
  OperationSummary,
  OperationType,
  ProductStock,
  StockItem,
  StockOperation,
  StockStatus,
  StockSummary,
} from '@/modules/stock/types'
import { FC_OFFICE_ROLES } from '@/modules/auth/roles'
import { db } from '../db/store'
import { DAY } from '../db/seed'
import type { StockRecord } from '../db/types'
import { localCollection } from '../lib/collection'
import { listParams, paginate, param, problem, sortItems, withAuth, type MockContext } from '../lib/http'
import { lookups } from '../lib/mappers'
import { createRandom } from '../lib/random'
import { canSee, visibleWarehouseIds } from '../lib/scope'
import { api } from './api'

const OPERATION_ROLES = [...FC_OFFICE_ROLES, 'fc_receiver' as const]

function scopedStock(ctx: MockContext, warehouseId?: string | null): StockRecord[] {
  return db.data.stock.filter((s) => (!warehouseId || s.warehouseId === warehouseId) && canSee(ctx, s))
}

function locationMaps() {
  const zones = new Map(db.data.zones.map((z) => [z.id, z]))
  const locations = new Map(db.data.locations.map((l) => [l.id, l]))
  return { zones, locations }
}

function toStockItem(s: StockRecord, l = lookups(), m = locationMaps()): StockItem {
  const product = l.products.get(s.productId)
  const location = m.locations.get(s.locationId)
  const zone = location ? m.zones.get(location.zoneId) : undefined
  const status: StockStatus =
    zone?.type === 'defect' ? 'defect' : s.reserved > 0 && s.reserved >= s.quantity ? 'reserved' : 'available'
  return {
    id: s.id,
    productId: s.productId,
    sku: product?.sku ?? '—',
    productName: product?.name ?? 'Товар',
    barcode: product?.barcode ?? '',
    sellerId: s.sellerId,
    sellerName: l.orgs.get(s.sellerId)?.name ?? '—',
    warehouseId: s.warehouseId,
    warehouseName: l.warehouses.get(s.warehouseId)?.name ?? '—',
    locationId: s.locationId,
    locationCode: location?.code ?? '—',
    zoneCode: zone?.code ?? '—',
    quantity: s.quantity,
    reserved: s.reserved,
    available: zone?.type === 'defect' ? 0 : Math.max(0, s.quantity - s.reserved),
    status,
    volumeLiters: Math.round((product?.volumeLiters ?? 0) * s.quantity * 10) / 10,
  }
}

/* ---------- Журнал операций: синтетическая история + действия пользователя ---------- */

const REASONS: ErrorReason[] = ['defect', 'shortage', 'missort', 'expired']

function seedOperations(): StockOperation[] {
  const r = createRandom(20260)
  const l = lookups()
  const m = locationMaps()
  const now = Date.now()
  const result: StockOperation[] = []
  const stock = db.data.stock.filter((s) => s.quantity > 5)
  const actorsByWarehouse = new Map<string, string[]>()
  for (const w of db.data.warehouses) {
    const names = db.data.users
      .filter((u) => u.memberships.some((ms) => ms.organizationId === w.organizationId))
      .map((u) => u.fullName)
    actorsByWarehouse.set(w.id, names.length ? names : ['Сотрудник склада'])
  }
  for (let i = 0; i < 160 && stock.length; i++) {
    const s = r.pick(stock)
    const item = toStockItem(s, l, m)
    const type: OperationType = r.pick(['move', 'move', 'move', 'inventory', 'inventory', 'writeoff'])
    const at = new Date(now - r.int(1, 30 * 24) * (DAY / 24)).toISOString()
    const base = {
      id: `op_seed_${i}`,
      number: `OP-${String(10000 + i)}`,
      type,
      at,
      actorName: r.pick(actorsByWarehouse.get(s.warehouseId)!),
      warehouseId: s.warehouseId,
      warehouseName: item.warehouseName,
      productId: s.productId,
      sku: item.sku,
      productName: item.productName,
      sellerName: item.sellerName,
    }
    if (type === 'move') {
      const target = r.pick(db.data.locations.filter((loc) => loc.warehouseId === s.warehouseId))
      result.push({
        ...base,
        fromLocationCode: target.code === item.locationCode ? '—' : target.code,
        toLocationCode: item.locationCode,
        quantity: r.int(2, 30),
      })
    } else if (type === 'inventory') {
      const delta = r.bool(0.55) ? 0 : r.int(-4, 3)
      result.push({
        ...base,
        fromLocationCode: item.locationCode,
        expected: s.quantity - delta,
        counted: s.quantity,
        quantity: delta,
        reason: delta < 0 ? r.pick(['shortage', 'missort']) : delta > 0 ? 'missort' : undefined,
      })
    } else {
      result.push({
        ...base,
        fromLocationCode: item.locationCode,
        quantity: -r.int(1, 5),
        reason: r.pick(REASONS),
      })
    }
  }
  return result
}

const operations = localCollection<StockOperation>('stock-operations', seedOperations)

function scopedOperations(ctx: MockContext, warehouseId?: string | null): StockOperation[] {
  const visible = visibleWarehouseIds(ctx)
  return operations.all.filter(
    (o) => (!warehouseId || o.warehouseId === warehouseId) && (!visible || visible.has(o.warehouseId)),
  )
}

function record(
  ctx: MockContext,
  s: StockRecord,
  data: Pick<StockOperation, 'type' | 'quantity'> & Partial<StockOperation>,
) {
  const item = toStockItem(s)
  const id = `op_${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 6)}`
  const op: StockOperation = {
    id,
    number: `OP-${String(20000 + operations.all.length)}`,
    at: new Date().toISOString(),
    actorName: ctx.user.fullName,
    warehouseId: s.warehouseId,
    warehouseName: item.warehouseName,
    productId: s.productId,
    sku: item.sku,
    productName: item.productName,
    sellerName: item.sellerName,
    ...data,
  }
  return operations.upsert(op)
}

async function body<T>(request: Request): Promise<T> {
  return (await request.json()) as T
}

function stockFor(ctx: MockContext, id: string): StockRecord | Response {
  const s = db.data.stock.find((x) => x.id === id)
  if (!s || !canSee(ctx, s)) return problem(404, 'Позиция остатка не найдена')
  return s
}

export const stockHandlers = [
  http.get(
    api('/stock/summary'),
    withAuth(({ ctx, url }) => {
      const q = url.searchParams
      const items = scopedStock(ctx, q.get('warehouseId'))
        .filter((s) => !q.get('sellerId') || s.sellerId === q.get('sellerId'))
        .map((s) => toStockItem(s))
      const used = new Set(items.map((i) => i.locationId))
      const visible = visibleWarehouseIds(ctx)
      const locations = db.data.locations.filter(
        (loc) =>
          (!q.get('warehouseId') || loc.warehouseId === q.get('warehouseId')) &&
          (!visible || visible.has(loc.warehouseId)),
      )
      const summary: StockSummary = {
        skuCount: new Set(items.map((i) => i.productId)).size,
        quantity: items.reduce((sum, i) => sum + i.quantity, 0),
        reserved: items.reduce((sum, i) => sum + i.reserved, 0),
        available: items.reduce((sum, i) => sum + i.available, 0),
        defect: items.filter((i) => i.status === 'defect').reduce((sum, i) => sum + i.quantity, 0),
        emptyLocations: locations.filter((loc) => !used.has(loc.id)).length,
      }
      return HttpResponse.json(summary)
    }),
  ),

  http.get(
    api('/stock/products'),
    withAuth(({ ctx, url }) => {
      const { page, pageSize, search, sortField, sortOrder } = listParams(url, {
        sortField: 'daysOfCover',
        sortOrder: 'asc',
      })
      const l = lookups()
      const m = locationMaps()
      const since = Date.now() - 30 * DAY
      const sales = new Map<string, number>()
      for (const o of db.data.orders) {
        if (!canSee(ctx, o) || Date.parse(o.createdAt) < since || o.status === 'cancelled') continue
        for (const line of o.lines)
          sales.set(line.productId, (sales.get(line.productId) ?? 0) + line.quantity)
      }
      const byProduct = new Map<string, ProductStock>()
      for (const s of scopedStock(ctx)) {
        const item = toStockItem(s, l, m)
        let entry = byProduct.get(s.productId)
        if (!entry) {
          entry = {
            id: s.productId,
            productId: s.productId,
            sku: item.sku,
            name: item.productName,
            available: 0,
            reserved: 0,
            defect: 0,
            avgDailySales: 0,
            daysOfCover: null,
            warehouses: [],
          }
          byProduct.set(s.productId, entry)
        }
        if (item.status === 'defect') entry.defect += s.quantity
        else {
          entry.available += item.available
          entry.reserved += s.reserved
        }
        let wh = entry.warehouses.find((w) => w.warehouseId === s.warehouseId)
        if (!wh) {
          const warehouse = l.warehouses.get(s.warehouseId)
          wh = {
            warehouseId: s.warehouseId,
            warehouseName: item.warehouseName,
            fulfillmentName: l.orgs.get(warehouse?.organizationId ?? '')?.name ?? '—',
            available: 0,
            reserved: 0,
          }
          entry.warehouses.push(wh)
        }
        wh.available += item.available
        wh.reserved += s.reserved
      }
      let items = [...byProduct.values()].map((p) => {
        const avg = Math.round(((sales.get(p.productId) ?? 0) / 30) * 10) / 10
        return { ...p, avgDailySales: avg, daysOfCover: avg > 0 ? Math.floor(p.available / avg) : null }
      })
      if (search)
        items = items.filter(
          (p) => p.name.toLowerCase().includes(search) || p.sku.toLowerCase().includes(search),
        )
      if (url.searchParams.get('lowStock') === 'true')
        items = items.filter((p) => p.daysOfCover !== null && p.daysOfCover < 14)
      // null (нет продаж) — в конец при сортировке по запасу
      const sorted = sortItems(
        items.map((p) => ({ ...p, daysOfCover: p.daysOfCover ?? (sortOrder === 'asc' ? 1e9 : -1) })),
        sortField,
        sortOrder,
      )
      return HttpResponse.json(
        paginate(
          sorted.map((p) => ({
            ...p,
            daysOfCover: p.daysOfCover >= 1e9 || p.daysOfCover < 0 ? null : p.daysOfCover,
          })),
          page,
          pageSize,
        ),
      )
    }),
  ),

  http.get(
    api('/stock/operations/summary'),
    withAuth(({ ctx, url }) => {
      const items = scopedOperations(ctx, url.searchParams.get('warehouseId'))
      const since = Date.now() - 30 * DAY
      const recent = items.filter((o) => Date.parse(o.at) >= since)
      const byType: Record<OperationType, number> = { move: 0, inventory: 0, writeoff: 0 }
      for (const o of recent) byType[o.type]++
      const errors = REASONS.map((reason) => {
        const list = recent.filter((o) => o.reason === reason)
        return {
          reason,
          count: list.length,
          quantity: list.reduce((sum, o) => sum + Math.abs(Math.min(0, o.quantity)), 0),
        }
      })
      const summary: OperationSummary = { byType, errors, last30Days: recent.length }
      return HttpResponse.json(summary)
    }),
  ),

  http.get(
    api('/stock/operations'),
    withAuth(({ ctx, url }) => {
      const q = url.searchParams
      const { page, pageSize, search, sortField, sortOrder } = listParams(url, {
        sortField: 'at',
        sortOrder: 'desc',
      })
      let items = scopedOperations(ctx, q.get('warehouseId'))
      const type = q.get('type')
      if (type) items = items.filter((o) => o.type === type)
      const stockId = q.get('stockId')
      if (stockId) {
        const s = db.data.stock.find((x) => x.id === stockId)
        items = s ? items.filter((o) => o.productId === s.productId && o.warehouseId === s.warehouseId) : []
      }
      if (search)
        items = items.filter((o) =>
          [o.number, o.sku, o.productName, o.actorName, o.fromLocationCode, o.toLocationCode].some((v) =>
            v?.toLowerCase().includes(search),
          ),
        )
      return HttpResponse.json(paginate(sortItems(items, sortField, sortOrder), page, pageSize))
    }),
  ),

  http.get(
    api('/stock'),
    withAuth(({ ctx, url }) => {
      const q = url.searchParams
      const { page, pageSize, search, sortField, sortOrder } = listParams(url, {
        sortField: 'locationCode',
        sortOrder: 'asc',
      })
      const l = lookups()
      const m = locationMaps()
      let items = scopedStock(ctx, q.get('warehouseId')).map((s) => toStockItem(s, l, m))
      const sellerId = q.get('sellerId')
      const zone = q.get('zone')
      const status = q.get('status')
      if (sellerId) items = items.filter((i) => i.sellerId === sellerId)
      if (zone) items = items.filter((i) => i.zoneCode === zone)
      if (status)
        items = items.filter((i) =>
          status === 'reserved' ? i.reserved > 0 && i.status !== 'defect' : i.status === status,
        )
      if (search)
        items = items.filter((i) =>
          [i.sku, i.productName, i.barcode, i.locationCode, i.sellerName].some((v) =>
            v.toLowerCase().includes(search),
          ),
        )
      return HttpResponse.json(paginate(sortItems(items, sortField, sortOrder), page, pageSize))
    }),
  ),

  http.get(
    api('/stock/locations'),
    withAuth(({ ctx, url }) => {
      const warehouseId = url.searchParams.get('warehouseId')
      const search = (url.searchParams.get('search') ?? '').toLowerCase()
      const visible = visibleWarehouseIds(ctx)
      if (!warehouseId || (visible && !visible.has(warehouseId))) return problem(404, 'Склад не найден')
      const zones = new Map(db.data.zones.map((z) => [z.id, z]))
      const items = db.data.locations
        .filter(
          (loc) => loc.warehouseId === warehouseId && (!search || loc.code.toLowerCase().includes(search)),
        )
        .slice(0, 30)
        .map((loc) => ({
          id: loc.id,
          code: loc.code,
          zoneCode: zones.get(loc.zoneId)?.code ?? '',
          zoneType: zones.get(loc.zoneId)?.type ?? 'storage',
        }))
      return HttpResponse.json(items)
    }),
  ),

  http.get(
    api('/stock/:id'),
    withAuth(({ ctx, params }) => {
      const s = stockFor(ctx, param(params, 'id'))
      if (s instanceof Response) return s
      return HttpResponse.json(toStockItem(s))
    }),
  ),

  http.post(
    api('/stock/move'),
    withAuth(
      async ({ ctx, request }) => {
        const input = await body<{
          stockId: string
          toLocationId: string
          quantity: number
          comment?: string
        }>(request)
        const s = stockFor(ctx, input.stockId)
        if (s instanceof Response) return s
        const target = db.data.locations.find((loc) => loc.id === input.toLocationId)
        if (!target || target.warehouseId !== s.warehouseId)
          return problem(422, 'Ячейка назначения не найдена на этом складе')
        if (target.id === s.locationId) return problem(422, 'Товар уже находится в этой ячейке')
        const free = s.quantity - s.reserved
        if (!Number.isInteger(input.quantity) || input.quantity <= 0)
          return problem(422, 'Количество должно быть больше нуля')
        if (input.quantity > free)
          return problem(
            409,
            'Недостаточно свободного остатка',
            `Свободно ${free} шт — зарезервированный товар перемещать нельзя.`,
          )
        const fromCode = toStockItem(s).locationCode
        db.upsert('stock', { ...s, quantity: s.quantity - input.quantity })
        const existing = db.data.stock.find(
          (x) => x.productId === s.productId && x.locationId === target.id && x.sellerId === s.sellerId,
        )
        if (existing) db.upsert('stock', { ...existing, quantity: existing.quantity + input.quantity })
        else
          db.upsert('stock', {
            id: `stk_${s.productId}_${target.id}_${Date.now().toString(36)}`,
            productId: s.productId,
            sellerId: s.sellerId,
            warehouseId: s.warehouseId,
            locationId: target.id,
            quantity: input.quantity,
            reserved: 0,
          })
        const current = db.data.stock.find((x) => x.id === s.id)!
        if (current.quantity === 0 && current.reserved === 0) db.remove('stock', s.id)
        return HttpResponse.json(
          record(ctx, s, {
            type: 'move',
            quantity: input.quantity,
            fromLocationCode: fromCode,
            toLocationCode: target.code,
            comment: input.comment,
          }),
          { status: 201 },
        )
      },
      { roles: OPERATION_ROLES },
    ),
  ),

  http.post(
    api('/stock/inventory'),
    withAuth(
      async ({ ctx, request }) => {
        const input = await body<{ stockId: string; counted: number; comment?: string }>(request)
        const s = stockFor(ctx, input.stockId)
        if (s instanceof Response) return s
        if (!Number.isInteger(input.counted) || input.counted < 0)
          return problem(422, 'Фактическое количество не может быть отрицательным')
        if (input.counted < s.reserved)
          return problem(
            409,
            'Факт меньше резерва',
            `Под заказы зарезервировано ${s.reserved} шт. Сначала снимите резерв.`,
          )
        const delta = input.counted - s.quantity
        db.upsert('stock', { ...s, quantity: input.counted })
        return HttpResponse.json(
          record(ctx, s, {
            type: 'inventory',
            quantity: delta,
            expected: s.quantity,
            counted: input.counted,
            fromLocationCode: toStockItem(s).locationCode,
            reason: delta < 0 ? 'shortage' : delta > 0 ? 'missort' : undefined,
            comment: input.comment,
          }),
          { status: 201 },
        )
      },
      { roles: OPERATION_ROLES },
    ),
  ),

  http.post(
    api('/stock/writeoff'),
    withAuth(
      async ({ ctx, request }) => {
        const input = await body<{
          stockId: string
          quantity: number
          reason: ErrorReason
          comment?: string
        }>(request)
        const s = stockFor(ctx, input.stockId)
        if (s instanceof Response) return s
        if (!REASONS.includes(input.reason)) return problem(422, 'Укажите причину списания')
        const free = s.quantity - s.reserved
        if (!Number.isInteger(input.quantity) || input.quantity <= 0)
          return problem(422, 'Количество должно быть больше нуля')
        if (input.quantity > free)
          return problem(409, 'Нельзя списать больше свободного остатка', `Свободно ${free} шт.`)
        db.upsert('stock', { ...s, quantity: s.quantity - input.quantity })
        return HttpResponse.json(
          record(ctx, s, {
            type: 'writeoff',
            quantity: -input.quantity,
            reason: input.reason,
            fromLocationCode: toStockItem(s).locationCode,
            comment: input.comment,
          }),
          { status: 201 },
        )
      },
      { roles: OPERATION_ROLES },
    ),
  ),
]
