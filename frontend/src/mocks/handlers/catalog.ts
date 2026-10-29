import { http, HttpResponse } from 'msw'
import type {
  ClientDetails,
  ClientListItem,
  ClientRequest,
  ImportResult,
  ProductDetails,
  ProductFacets,
  ProductInput,
  ProductListItem,
} from '@/modules/catalog/types'
import { SELLER_ROLES } from '@/modules/auth/roles'
import { db } from '../db/store'
import { DAY } from '../db/seed'
import type { ContractRecord, ProductRecord } from '../db/types'
import { localCollection } from '../lib/collection'
import { parseBody, readJson, listParams, paginate, param, problem, sortItems, withAuth, type MockContext } from '../lib/http'
import { productImportSchema, productSchema } from '../lib/schemas'
import { lookups } from '../lib/mappers'
import { visibleWarehouseIds } from '../lib/scope'
import { api } from './api'

/* Архивные товары храним отдельно, чтобы не менять схему ProductRecord */
const archived = localCollection<{ id: string; archived: boolean }>('product-archive', () => [])
const isArchived = (id: string) => archived.find(id)?.archived ?? false

function visibleProducts(ctx: MockContext): ProductRecord[] {
  if (!ctx.org) return db.data.products
  if (ctx.org.type === 'seller') return db.data.products.filter((p) => p.sellerId === ctx.org!.id)
  const sellers = new Set(
    db.data.contracts.filter((c) => c.fulfillmentId === ctx.org!.id).map((c) => c.sellerId),
  )
  return db.data.products.filter((p) => sellers.has(p.sellerId))
}

function stockByProduct(ctx: MockContext): Map<string, number> {
  const warehouses = visibleWarehouseIds(ctx)
  const map = new Map<string, number>()
  for (const s of db.data.stock) {
    if (warehouses && !warehouses.has(s.warehouseId)) continue
    map.set(s.productId, (map.get(s.productId) ?? 0) + s.quantity)
  }
  return map
}

function toItem(p: ProductRecord, stock: Map<string, number>, l = lookups()): ProductListItem {
  return {
    id: p.id,
    sku: p.sku,
    name: p.name,
    category: p.category,
    barcode: p.barcode,
    wbBarcode: p.wbBarcode,
    sellerId: p.sellerId,
    sellerName: l.orgs.get(p.sellerId)?.name ?? '—',
    price: p.price,
    weightG: p.weightG,
    volumeLiters: p.volumeLiters,
    tracksExpiry: p.tracksExpiry,
    requiresMarking: p.requiresMarking,
    stockQty: stock.get(p.id) ?? 0,
    archived: isArchived(p.id),
  }
}

function filtered(ctx: MockContext, url: URL, withFlag: boolean): ProductListItem[] {
  const q = url.searchParams
  const { search } = listParams(url)
  const stock = stockByProduct(ctx)
  const l = lookups()
  const flag = withFlag ? q.get('flag') : null
  return visibleProducts(ctx)
    .filter((p) => !q.get('sellerId') || p.sellerId === q.get('sellerId'))
    .filter((p) => !withFlag || !q.get('category') || p.category === q.get('category'))
    .map((p) => toItem(p, stock, l))
    .filter(
      (p) =>
        !search ||
        [p.sku, p.name, p.barcode, p.wbBarcode ?? '', p.sellerName].some((v) =>
          v.toLowerCase().includes(search),
        ),
    )
    .filter((p) => {
      if (flag === 'archived') return p.archived
      if (!withFlag) return true
      if (p.archived) return false
      if (flag === 'marking') return p.requiresMarking
      if (flag === 'expiry') return p.tracksExpiry
      if (flag === 'no_stock') return p.stockQty === 0
      return true
    })
}

function validate(input: ProductInput, sellerId: string, selfId?: string): string | null {
  if (!input.sku?.trim()) return 'Укажите артикул'
  if (!/^[A-Za-z0-9-_.]{2,32}$/.test(input.sku.trim()))
    return 'Артикул: 2–32 символа, латиница, цифры, «-», «_», «.»'
  if (!input.name?.trim() || input.name.trim().length < 3) return 'Название не короче 3 символов'
  if (!/^\d{8}$|^\d{13}$/.test(input.barcode ?? '')) return 'Штрихкод должен содержать 8 или 13 цифр'
  if (input.wbBarcode && !/^\d{8,14}$/.test(input.wbBarcode)) return 'ШК маркетплейса: 8–14 цифр'
  if (!(input.price > 0)) return 'Цена должна быть больше нуля'
  if (!(input.weightG > 0) || !(input.volumeLiters > 0)) return 'Укажите вес и объём'
  const duplicate = db.data.products.find(
    (p) =>
      p.sellerId === sellerId &&
      p.id !== selfId &&
      (p.sku.toLowerCase() === input.sku.trim().toLowerCase() || p.barcode === input.barcode),
  )
  if (duplicate) return `Товар с таким артикулом или штрихкодом уже есть: ${duplicate.sku}`
  return null
}

function toRecord(input: ProductInput, sellerId: string, id: string): ProductRecord {
  return {
    id,
    sellerId,
    sku: input.sku.trim(),
    name: input.name.trim(),
    category: input.category?.trim() || 'Без категории',
    barcode: input.barcode,
    wbBarcode: input.wbBarcode || undefined,
    price: input.price,
    weightG: input.weightG,
    volumeLiters: input.volumeLiters,
    tracksExpiry: !!input.tracksExpiry,
    requiresMarking: !!input.requiresMarking,
  }
}

const newId = () => `prd_${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`

/* ---------- Клиенты ФЦ ---------- */

const contractNumber = (c: ContractRecord) => `Д-${c.id.replace(/\D/g, '').padStart(4, '0')}`

function tariffFor(contractId: string) {
  const seed = [...contractId].reduce((s, ch) => s + ch.charCodeAt(0), 0)
  return {
    storagePerLiterDay: 0.35 + (seed % 5) * 0.05,
    pickPerOrder: 45 + (seed % 4) * 5,
    receivingPerUnit: 6 + (seed % 3),
  }
}

function clientItem(c: ContractRecord, l = lookups()): ClientListItem {
  const org = l.orgs.get(c.sellerId)
  const since = Date.now() - 30 * DAY
  const warehouseSet = new Set(c.warehouseIds)
  return {
    id: c.sellerId,
    name: org?.name ?? '—',
    inn: org?.inn ?? '',
    city: org?.city ?? '',
    contractId: c.id,
    contractNumber: contractNumber(c),
    startedAt: c.startedAt,
    warehouses: c.warehouseIds.map((id) => ({ id, name: l.warehouses.get(id)?.name ?? id })),
    skuCount: db.data.products.filter((p) => p.sellerId === c.sellerId).length,
    stockUnits: db.data.stock
      .filter((s) => s.sellerId === c.sellerId && warehouseSet.has(s.warehouseId))
      .reduce((sum, s) => sum + s.quantity, 0),
    orders30d: db.data.orders.filter(
      (o) => o.sellerId === c.sellerId && warehouseSet.has(o.warehouseId) && Date.parse(o.createdAt) >= since,
    ).length,
    marketplaces: org?.marketplaces ?? [],
  }
}

const REQUEST_SEED: Omit<ClientRequest, 'id' | 'createdAt' | 'status'>[] = [
  {
    companyName: 'ИП Волкова А. С.',
    inn: '771234567890',
    city: 'Москва',
    contactName: 'Анна Волкова',
    contactPhone: '+7 916 204-11-87',
    skuEstimate: 120,
    ordersPerDay: 60,
    marketplaces: ['wb', 'ozon'],
    message: 'Текстиль для дома, ищем склад рядом с МКАД. Нужна упаковка в фирменные пакеты.',
  },
  {
    companyName: 'ООО «Эко Снек»',
    inn: '5024198765',
    city: 'Красногорск',
    contactName: 'Дмитрий Орлов',
    contactPhone: '+7 925 118-40-02',
    skuEstimate: 45,
    ordersPerDay: 180,
    marketplaces: ['ozon', 'yandex'],
    message: 'Снеки с коротким сроком годности — нужен учёт партий и FEFO.',
  },
  {
    companyName: 'ООО «ГаджетПро»',
    inn: '7725456123',
    city: 'Москва',
    contactName: 'Игорь Ли',
    contactPhone: '+7 903 777-12-30',
    skuEstimate: 300,
    ordersPerDay: 90,
    marketplaces: ['wb'],
    message: 'Электроника с маркировкой «Честный знак», FBO-поставки на WB.',
  },
  {
    companyName: 'ИП Сафин Р. Р.',
    inn: '165912345678',
    city: 'Казань',
    contactName: 'Ринат Сафин',
    contactPhone: '+7 987 300-55-19',
    skuEstimate: 30,
    ordersPerDay: 25,
    marketplaces: ['wb'],
    message: 'Детские игрушки, старт продаж в следующем месяце.',
  },
]

function seedRequests(): ClientRequest[] {
  const now = Date.now()
  return REQUEST_SEED.map((r, i) => ({
    ...r,
    id: `creq_${i + 1}`,
    createdAt: new Date(now - (i * 19 + 3) * 3_600_000).toISOString(),
    status: i === 3 ? 'declined' : 'pending',
    decidedAt: i === 3 ? new Date(now - 30 * 3_600_000).toISOString() : undefined,
  }))
}
/* Заявки — по организации ФЦ, ключ включает id организации */
const requestStores = new Map<string, ReturnType<typeof localCollection<ClientRequest>>>()
export function requests(orgId: string) {
  let store = requestStores.get(orgId)
  if (!store) {
    store = localCollection<ClientRequest>(`client-requests.${orgId}`, seedRequests)
    requestStores.set(orgId, store)
  }
  return store
}

export const catalogHandlers = [
  http.get(
    api('/catalog/products/facets'),
    withAuth(({ ctx, url }) => {
      const items = filtered(ctx, url, false)
      const active = items.filter((p) => !p.archived)
      const categories = new Map<string, number>()
      for (const p of active) categories.set(p.category, (categories.get(p.category) ?? 0) + 1)
      const facets: ProductFacets = {
        total: active.length,
        categories: [...categories.entries()]
          .map(([value, count]) => ({ value, count }))
          .sort((a, b) => b.count - a.count),
        marking: active.filter((p) => p.requiresMarking).length,
        expiry: active.filter((p) => p.tracksExpiry).length,
        noStock: active.filter((p) => p.stockQty === 0).length,
        archived: items.length - active.length,
      }
      return HttpResponse.json(facets)
    }),
  ),

  http.post(
    api('/catalog/products/import'),
    withAuth(
      async ({ ctx, request }) => {
        const { rows } = (await parseBody(request, productImportSchema)) as unknown as { rows: ProductInput[] }
        const result: ImportResult = { created: 0, updated: 0, errors: [] }
        rows.forEach((row, index) => {
          const existing = db.data.products.find(
            (p) => p.sellerId === ctx.org!.id && p.sku.toLowerCase() === row.sku?.trim().toLowerCase(),
          )
          const error = validate(row, ctx.org!.id, existing?.id)
          if (error) return result.errors.push({ row: index + 2, message: error })
          db.upsert('products', toRecord(row, ctx.org!.id, existing?.id ?? newId()))
          if (existing) result.updated++
          else result.created++
        })
        return HttpResponse.json(result)
      },
      { roles: SELLER_ROLES },
    ),
  ),

  http.get(
    api('/catalog/products'),
    withAuth(({ ctx, url }) => {
      const { page, pageSize, sortField, sortOrder } = listParams(url, {
        sortField: 'name',
        sortOrder: 'asc',
      })
      return HttpResponse.json(
        paginate(sortItems(filtered(ctx, url, true), sortField, sortOrder), page, pageSize),
      )
    }),
  ),

  http.get(
    api('/catalog/products/:id'),
    withAuth(({ ctx, params }) => {
      const product = visibleProducts(ctx).find((p) => p.id === param(params, 'id'))
      if (!product) return problem(404, 'Товар не найден')
      const l = lookups()
      const warehouses = visibleWarehouseIds(ctx)
      const stock = db.data.stock.filter(
        (s) => s.productId === product.id && (!warehouses || warehouses.has(s.warehouseId)),
      )
      const byWarehouse = new Map<string, ProductDetails['stock'][number]>()
      for (const s of stock) {
        const entry = byWarehouse.get(s.warehouseId) ?? {
          warehouseId: s.warehouseId,
          warehouseName: l.warehouses.get(s.warehouseId)?.name ?? '—',
          quantity: 0,
          reserved: 0,
          locations: 0,
        }
        entry.quantity += s.quantity
        entry.reserved += s.reserved
        entry.locations++
        byWarehouse.set(s.warehouseId, entry)
      }
      const since = Date.now() - 30 * DAY
      const sold30d = db.data.orders
        .filter(
          (o) =>
            Date.parse(o.createdAt) >= since &&
            o.status !== 'cancelled' &&
            (!warehouses || warehouses.has(o.warehouseId)),
        )
        .reduce(
          (sum, o) =>
            sum +
            o.lines.filter((line) => line.productId === product.id).reduce((s, line) => s + line.quantity, 0),
          0,
        )
      const details: ProductDetails = {
        ...toItem(product, stockByProduct(ctx), l),
        stock: [...byWarehouse.values()],
        sold30d,
      }
      return HttpResponse.json(details)
    }),
  ),

  http.post(
    api('/catalog/products'),
    withAuth(
      async ({ ctx, request }) => {
        const input = (await parseBody(request, productSchema)) as unknown as ProductInput
        const error = validate(input, ctx.org!.id)
        if (error) return problem(422, error)
        const record = toRecord(input, ctx.org!.id, newId())
        db.upsert('products', record)
        return HttpResponse.json(toItem(record, stockByProduct(ctx)), { status: 201 })
      },
      { roles: SELLER_ROLES },
    ),
  ),

  http.put(
    api('/catalog/products/:id'),
    withAuth(
      async ({ ctx, request, params }) => {
        const product = visibleProducts(ctx).find((p) => p.id === param(params, 'id'))
        if (!product) return problem(404, 'Товар не найден')
        const input = (await readJson(request)) as ProductInput
        const error = validate(input, product.sellerId, product.id)
        if (error) return problem(422, error)
        const record = toRecord(input, product.sellerId, product.id)
        db.upsert('products', record)
        return HttpResponse.json(toItem(record, stockByProduct(ctx)))
      },
      { roles: SELLER_ROLES },
    ),
  ),

  http.post(
    api('/catalog/products/:id/:action'),
    withAuth(
      ({ ctx, params }) => {
        const product = visibleProducts(ctx).find((p) => p.id === param(params, 'id'))
        if (!product) return problem(404, 'Товар не найден')
        const action = param(params, 'action')
        if (action !== 'archive' && action !== 'restore') return problem(404, 'Неизвестное действие')
        if (action === 'archive' && (stockByProduct(ctx).get(product.id) ?? 0) > 0)
          return problem(
            409,
            'Нельзя архивировать товар с остатком',
            'Сначала отгрузите или спишите остаток на складах.',
          )
        archived.upsert({ id: product.id, archived: action === 'archive' })
        return HttpResponse.json(toItem(product, stockByProduct(ctx)))
      },
      { roles: SELLER_ROLES },
    ),
  ),

  http.get(
    api('/fc/clients'),
    withAuth(({ ctx, url }) => {
      const search = (url.searchParams.get('search') ?? '').toLowerCase()
      const l = lookups()
      const items = db.data.contracts
        .filter((c) => !ctx.org || c.fulfillmentId === ctx.org.id)
        .map((c) => clientItem(c, l))
        .filter((c) => !search || c.name.toLowerCase().includes(search) || c.inn.includes(search))
        .sort((a, b) => b.orders30d - a.orders30d)
      return HttpResponse.json(items)
    }),
  ),

  http.get(
    api('/fc/clients/:id'),
    withAuth(({ ctx, params }) => {
      const contract = db.data.contracts.find(
        (c) => c.sellerId === param(params, 'id') && (!ctx.org || c.fulfillmentId === ctx.org.id),
      )
      if (!contract) return problem(404, 'Клиент не найден')
      const base = clientItem(contract)
      const owner = db.data.users.find((u) =>
        u.memberships.some((m) => m.organizationId === contract.sellerId && m.role === 'seller_owner'),
      )
      const warehouseSet = new Set(contract.warehouseIds)
      const today = new Date()
      today.setHours(0, 0, 0, 0)
      const ordersTrend = Array.from({ length: 14 }, (_, i) => {
        const start = today.getTime() - (13 - i) * DAY
        return {
          date: new Date(start).toISOString(),
          count: db.data.orders.filter(
            (o) =>
              o.sellerId === contract.sellerId &&
              warehouseSet.has(o.warehouseId) &&
              Date.parse(o.createdAt) >= start &&
              Date.parse(o.createdAt) < start + DAY,
          ).length,
        }
      })
      const details: ClientDetails = {
        ...base,
        ownerName: owner?.fullName ?? '—',
        ownerEmail: owner?.email ?? '—',
        ownerPhone: owner?.phone ?? '—',
        tariff: tariffFor(contract.id),
        ordersTrend,
        inboundActive: db.data.inbound.filter(
          (i) =>
            i.sellerId === contract.sellerId &&
            warehouseSet.has(i.warehouseId) &&
            ['in_transit', 'arrived', 'receiving'].includes(i.status),
        ).length,
      }
      return HttpResponse.json(details)
    }),
  ),

  http.get(
    api('/fc/client-requests'),
    withAuth(({ ctx }) => {
      if (!ctx.org || ctx.org.type !== 'fulfillment') return HttpResponse.json([])
      return HttpResponse.json(
        [...requests(ctx.org.id).all].sort((a, b) => b.createdAt.localeCompare(a.createdAt)),
      )
    }),
  ),

  http.post(
    api('/fc/client-requests/:id/:decision'),
    withAuth(
      async ({ ctx, params, request }) => {
        if (!ctx.org || ctx.org.type !== 'fulfillment') return problem(403, 'Недостаточно прав')
        const store = requests(ctx.org.id)
        const item = store.find(param(params, 'id'))
        if (!item) return problem(404, 'Заявка не найдена')
        if (item.status !== 'pending') return problem(409, 'Заявка уже рассмотрена')
        const decision = param(params, 'decision')
        if (decision === 'accept') {
          const { warehouseIds } = (await request.json().catch(() => ({}))) as { warehouseIds?: string[] }
          const own = new Set(
            db.data.warehouses.filter((w) => w.organizationId === ctx.org!.id).map((w) => w.id),
          )
          if (!warehouseIds?.length || warehouseIds.some((id) => !own.has(id)))
            return problem(422, 'Выберите склады для договора')
        } else if (decision !== 'decline') return problem(404, 'Неизвестное действие')
        return HttpResponse.json(
          store.upsert({
            ...item,
            status: decision === 'accept' ? 'accepted' : 'declined',
            decidedAt: new Date().toISOString(),
          }),
        )
      },
      { roles: ['fc_owner', 'fc_account_manager'] },
    ),
  ),
]
