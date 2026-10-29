import { http, HttpResponse } from 'msw'
import type {
  LocationInput,
  PutawayCheckInput,
  PutawayCheckResult,
  StorageLocation,
  WarehouseMapCell,
  Zone,
  ZoneInput,
} from '@/modules/warehouses/types'
import { db } from '../db/store'
import type { LocationRecord, StockRecord } from '../db/types'
import { readJson, listParams, type MockContext, paginate, param, problem, sortItems, withAuth } from '../lib/http'
import { visibleWarehouseIds } from '../lib/scope'
import { api } from './api'

const MANAGEMENT = { roles: ['fc_owner' as const, 'fc_manager' as const] }
/** Зоны, где допускается хранение товара (FR-06) */
const STORABLE = new Set(['storage', 'picking'])
const HEAVY_ITEM_G = 10_000
const MAX_HEAVY_LEVEL = 2

function stockByLocation(warehouseId: string) {
  const map = new Map<string, StockRecord[]>()
  for (const s of db.data.stock) {
    if (s.warehouseId !== warehouseId || s.quantity <= 0) continue
    const list = map.get(s.locationId) ?? []
    list.push(s)
    map.set(s.locationId, list)
  }
  return map
}

const productVolume = (productId: string) => db.data.products.find((p) => p.id === productId)?.volumeLiters ?? 0

function usedLiters(stock: StockRecord[] = []) {
  return Math.round(stock.reduce((sum, s) => sum + s.quantity * productVolume(s.productId), 0) * 10) / 10
}

function parseCode(code: string) {
  const [aisle = '', rack = '0'] = code.split('-')
  return { aisle, rack: Number(rack) }
}

function toLocation(record: LocationRecord, stock: StockRecord[] = []): StorageLocation {
  const zone = db.data.zones.find((z) => z.id === record.zoneId)
  const sellerIds = [...new Set(stock.map((s) => s.sellerId))]
  return {
    ...record,
    ...parseCode(record.code),
    zoneCode: zone?.code ?? '—',
    usedLiters: usedLiters(stock),
    skuCount: new Set(stock.map((s) => s.productId)).size,
    quantity: stock.reduce((sum, s) => sum + s.quantity, 0),
    sellerNames: sellerIds.map((id) => db.data.organizations.find((o) => o.id === id)?.name ?? '—'),
  }
}

function warehouseFor(ctx: MockContext, id: string) {
  const visible = visibleWarehouseIds(ctx)
  const warehouse = db.data.warehouses.find((w) => w.id === id)
  if (!warehouse || (visible && !visible.has(id))) return null
  return warehouse
}

function toZone(zoneId: string, stock: Map<string, StockRecord[]>): Zone {
  const zone = db.data.zones.find((z) => z.id === zoneId)!
  const locations = db.data.locations.filter((l) => l.zoneId === zoneId)
  return {
    ...zone,
    locationsCount: locations.length,
    capacityLiters: locations.reduce((sum, l) => sum + l.capacityLiters, 0),
    usedLiters: Math.round(locations.reduce((sum, l) => sum + usedLiters(stock.get(l.id)), 0)),
  }
}

export const warehouseHandlers = [
  http.get(
    api('/warehouses/:id/zones'),
    withAuth(({ ctx, params }) => {
      const warehouse = warehouseFor(ctx, param(params, 'id'))
      if (!warehouse) return problem(404, 'Склад не найден')
      const stock = stockByLocation(warehouse.id)
      return HttpResponse.json(db.data.zones.filter((z) => z.warehouseId === warehouse.id).map((z) => toZone(z.id, stock)))
    }),
  ),

  http.post(
    api('/warehouses/:id/zones'),
    withAuth(async ({ ctx, params, request }) => {
      const warehouse = warehouseFor(ctx, param(params, 'id'))
      if (!warehouse) return problem(404, 'Склад не найден')
      const input = (await readJson(request)) as ZoneInput
      const code = input.code.trim().toUpperCase()
      if (!/^[A-Z0-9]{2,6}$/.test(code)) return problem(400, 'Код зоны: 2–6 латинских букв или цифр')
      if (db.data.zones.some((z) => z.warehouseId === warehouse.id && z.code === code)) {
        return problem(409, 'Зона с таким кодом уже существует')
      }
      const record = { id: `${warehouse.id}_z_${code}`, warehouseId: warehouse.id, code, name: input.name.trim(), type: input.type }
      db.upsert('zones', record)
      return HttpResponse.json(toZone(record.id, new Map()), { status: 201 })
    }, MANAGEMENT),
  ),

  http.get(
    api('/warehouses/:id/locations'),
    withAuth(({ ctx, params, url }) => {
      const warehouse = warehouseFor(ctx, param(params, 'id'))
      if (!warehouse) return problem(404, 'Склад не найден')
      const query = listParams(url, { sortField: 'code', sortOrder: 'asc' })
      const zoneId = url.searchParams.get('zoneId')
      const occupancy = url.searchParams.get('occupancy')
      const stock = stockByLocation(warehouse.id)
      const items = db.data.locations
        .filter((l) => l.warehouseId === warehouse.id && (!zoneId || l.zoneId === zoneId))
        .filter((l) => !query.search || l.code.toLowerCase().includes(query.search))
        .map((l) => toLocation(l, stock.get(l.id)))
        .filter((l) => {
          if (!occupancy) return true
          const ratio = l.usedLiters / l.capacityLiters
          if (occupancy === 'empty') return l.quantity === 0
          if (occupancy === 'full') return ratio >= 0.9
          return l.quantity > 0 && ratio < 0.9
        })
      return HttpResponse.json(paginate(sortItems(items, query.sortField, query.sortOrder), query.page, query.pageSize))
    }),
  ),

  http.post(
    api('/warehouses/:id/locations'),
    withAuth(async ({ ctx, params, request }) => {
      const warehouse = warehouseFor(ctx, param(params, 'id'))
      if (!warehouse) return problem(404, 'Склад не найден')
      const input = (await readJson(request)) as LocationInput
      const aisle = input.aisle.trim().toUpperCase()
      if (!/^[A-Z]{1,2}$/.test(aisle)) return problem(400, 'Проход: 1–2 латинские буквы')
      if (input.rack < 1 || input.level < 1 || input.capacityLiters <= 0) return problem(400, 'Проверьте стеллаж, ярус и вместимость')
      const zone = db.data.zones.find((z) => z.id === input.zoneId && z.warehouseId === warehouse.id)
      if (!zone) return problem(400, 'Зона не найдена')
      const code = `${aisle}-${String(input.rack).padStart(2, '0')}-${input.level}`
      if (db.data.locations.some((l) => l.warehouseId === warehouse.id && l.code === code)) {
        return problem(409, `Ячейка ${code} уже существует`)
      }
      const aisleIndex = Math.max(0, 'ABCDEFGHJKLMNPQRSTUVWXYZ'.indexOf(aisle[0]!))
      const record: LocationRecord = {
        id: `${warehouse.id}_l_${code}`,
        warehouseId: warehouse.id,
        zoneId: zone.id,
        code,
        x: Math.round((4 + aisleIndex * 3.2) * 10) / 10,
        y: Math.round((3 + (input.rack - 1) * 1.4) * 10) / 10,
        level: input.level,
        capacityLiters: input.capacityLiters,
      }
      db.upsert('locations', record)
      return HttpResponse.json(toLocation(record), { status: 201 })
    }, MANAGEMENT),
  ),

  http.get(
    api('/warehouses/:id/map'),
    withAuth(({ ctx, params, url }) => {
      const warehouse = warehouseFor(ctx, param(params, 'id'))
      if (!warehouse) return problem(404, 'Склад не найден')
      const level = Number(url.searchParams.get('level') ?? 1)
      const stock = stockByLocation(warehouse.id)
      const cells: WarehouseMapCell[] = db.data.locations
        .filter((l) => l.warehouseId === warehouse.id && l.level === level)
        .map((l) => ({
          id: l.id,
          code: l.code,
          ...parseCode(l.code),
          fill: Math.min(1, usedLiters(stock.get(l.id)) / l.capacityLiters),
        }))
      return HttpResponse.json(cells)
    }),
  ),

  http.get(
    api('/warehouses/:id/products'),
    withAuth(({ ctx, params, url }) => {
      const warehouse = warehouseFor(ctx, param(params, 'id'))
      if (!warehouse) return problem(404, 'Склад не найден')
      const search = (url.searchParams.get('search') ?? '').trim().toLowerCase()
      const sellers = new Set(db.data.contracts.filter((c) => c.warehouseIds.includes(warehouse.id)).map((c) => c.sellerId))
      const items = db.data.products
        .filter((p) => sellers.has(p.sellerId))
        .filter((p) => !search || p.name.toLowerCase().includes(search) || p.sku.toLowerCase().includes(search) || p.barcode.includes(search))
        .slice(0, 30)
        .map((p) => ({
          id: p.id,
          sku: p.sku,
          name: p.name,
          volumeLiters: p.volumeLiters,
          weightG: p.weightG,
          sellerName: db.data.organizations.find((o) => o.id === p.sellerId)?.name ?? '—',
        }))
      return HttpResponse.json(items)
    }),
  ),

  http.post(
    api('/warehouses/:id/putaway-check'),
    withAuth(async ({ ctx, params, request }) => {
      const warehouse = warehouseFor(ctx, param(params, 'id'))
      if (!warehouse) return problem(404, 'Склад не найден')
      const input = (await readJson(request)) as PutawayCheckInput
      const location = db.data.locations.find((l) => l.id === input.locationId && l.warehouseId === warehouse.id)
      const product = db.data.products.find((p) => p.id === input.productId)
      if (!location || !product) return problem(400, 'Укажите ячейку и товар')
      if (!(input.quantity > 0)) return problem(400, 'Количество должно быть больше нуля')

      const zone = db.data.zones.find((z) => z.id === location.zoneId)
      const stock = stockByLocation(warehouse.id).get(location.id) ?? []
      const free = location.capacityLiters - usedLiters(stock)
      const needed = product.volumeLiters * input.quantity
      const foreign = stock.filter((s) => s.sellerId !== product.sellerId)
      const contracted = db.data.contracts.some((c) => c.sellerId === product.sellerId && c.warehouseIds.includes(warehouse.id))

      const checks: PutawayCheckResult['checks'] = [
        {
          rule: 'Договор хранения',
          passed: contracted,
          detail: contracted ? 'Клиент обслуживается на этом складе' : 'У клиента нет договора на этот склад',
        },
        {
          rule: 'Тип зоны',
          passed: STORABLE.has(zone?.type ?? ''),
          detail: STORABLE.has(zone?.type ?? '')
            ? `${zone?.name} допускает хранение`
            : `${zone?.name ?? 'Зона'} не предназначена для хранения`,
        },
        {
          rule: 'Вместимость',
          passed: needed <= free,
          detail: `Нужно ${needed.toFixed(1)} л, свободно ${Math.max(0, free).toFixed(1)} л из ${location.capacityLiters} л`,
        },
        {
          rule: 'Габариты и масса',
          passed: product.weightG < HEAVY_ITEM_G || location.level <= MAX_HEAVY_LEVEL,
          detail:
            product.weightG < HEAVY_ITEM_G
              ? `Масса единицы ${(product.weightG / 1000).toFixed(1)} кг`
              : `Тяжёлый товар (${(product.weightG / 1000).toFixed(1)} кг) — только ярусы 1–${MAX_HEAVY_LEVEL}`,
        },
        {
          rule: 'Принадлежность ячейки',
          passed: foreign.length === 0,
          detail: foreign.length === 0 ? 'В ячейке нет товара других клиентов' : 'В ячейке хранится товар другого клиента',
        },
      ]
      const result: PutawayCheckResult = {
        allowed: checks.every((c) => c.passed),
        checks,
        freeLitersAfter: Math.round((free - needed) * 10) / 10,
      }
      return HttpResponse.json(result)
    }),
  ),
]
