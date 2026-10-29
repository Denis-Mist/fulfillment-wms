import { http, HttpResponse } from 'msw'
import type { Role } from '@/modules/auth/types'
import { FBO_DESTINATIONS, FBO_NEXT } from '@/modules/fbo/labels'
import type {
  FboDetails,
  FboInput,
  FboListItem,
  FboMarketplace,
  FboStatus,
  FboStockOption,
  FboSummary,
} from '@/modules/fbo/types'
import { DAY, HOUR } from '../db/seed'
import { db } from '../db/store'
import { localCollection } from '../lib/collection'
import { readJson, countBy, listParams, paginate, param, problem, sortItems, withAuth, type MockContext } from '../lib/http'
import { lookups } from '../lib/mappers'
import { createRandom } from '../lib/random'
import { canSee, visibleWarehouseIds } from '../lib/scope'
import { api } from './api'

const FC_OPS: Role[] = ['fc_owner', 'fc_manager', 'fc_supervisor']
const ORDER: FboStatus[] = ['draft', 'confirmed', 'assembling', 'ready', 'shipped']

interface FboRecord {
  id: string
  number: string
  marketplace: FboMarketplace
  destination: string
  status: FboStatus
  sellerId: string
  warehouseId: string
  plannedAt: string
  createdAt: string
  supplyId?: string
  lines: { productId: string; quantity: number; packed: number }[]
  history: { at: string; status: FboStatus; by: string }[]
}

function seedFbo(): FboRecord[] {
  const rng = createRandom(9191)
  const now = Date.now()
  const out: FboRecord[] = []
  let n = 0
  for (const contract of db.data.contracts) {
    const stock = db.data.stock.filter(
      (s) => s.sellerId === contract.sellerId && contract.warehouseIds.includes(s.warehouseId),
    )
    if (!stock.length) continue
    for (let i = 0; i < rng.int(3, 6); i++) {
      const status = rng.weighted<FboStatus>([
        ['draft', 1],
        ['confirmed', 2],
        ['assembling', 2],
        ['ready', 1],
        ['shipped', 4],
        ['cancelled', 0.5],
      ])
      const marketplace = rng.pick<FboMarketplace>(['wb', 'ozon'])
      const wh = stock[0]!.warehouseId
      const items = rng.sample(
        stock.filter((s) => s.warehouseId === wh),
        rng.int(3, 9),
      )
      const created = now - rng.int(1, 20) * DAY
      const planned = status === 'shipped' ? created + rng.int(2, 5) * DAY : now + rng.int(-1, 6) * DAY
      const idx = ORDER.indexOf(status)
      const lines = items.map((s) => {
        const quantity = rng.int(10, 120)
        return {
          productId: s.productId,
          quantity,
          packed: idx >= 3 ? quantity : status === 'assembling' ? rng.int(0, quantity) : 0,
        }
      })
      const history = ORDER.slice(0, Math.max(1, idx + 1)).map((s, k) => ({
        at: new Date(created + k * rng.int(4, 20) * HOUR).toISOString(),
        status: s,
        by: k === 0 ? 'Менеджер селлера' : 'Сотрудник ФЦ',
      }))
      if (status === 'cancelled')
        history.push({ at: new Date(created + 6 * HOUR).toISOString(), status, by: 'Менеджер селлера' })
      n++
      out.push({
        id: `fbo_${n}`,
        number: `FBO-${String(3100 + n)}`,
        marketplace,
        destination: rng.pick(FBO_DESTINATIONS[marketplace]),
        status,
        sellerId: contract.sellerId,
        warehouseId: wh,
        plannedAt: new Date(planned).toISOString(),
        createdAt: new Date(created).toISOString(),
        supplyId:
          idx >= 1 ? `${marketplace === 'wb' ? 'WB-GI-' : 'OZ-'}${rng.int(1000000, 9999999)}` : undefined,
        lines,
        history,
      })
    }
  }
  return out
}

const shipments = localCollection<FboRecord>('fbo', seedFbo)

function toItem(r: FboRecord): FboListItem {
  const l = lookups()
  const units = r.lines.reduce((a, x) => a + x.quantity, 0)
  return {
    id: r.id,
    number: r.number,
    marketplace: r.marketplace,
    destination: r.destination,
    status: r.status,
    sellerId: r.sellerId,
    sellerName: l.orgs.get(r.sellerId)?.name ?? '',
    warehouseId: r.warehouseId,
    warehouseName: l.warehouses.get(r.warehouseId)?.name ?? '',
    plannedAt: r.plannedAt,
    createdAt: r.createdAt,
    units,
    packed: r.lines.reduce((a, x) => a + x.packed, 0),
    boxes: Math.max(1, Math.ceil(units / 40)),
    skuCount: r.lines.length,
  }
}

function toDetails(r: FboRecord): FboDetails {
  const l = lookups()
  return {
    ...toItem(r),
    supplyId: r.supplyId,
    history: r.history,
    lines: r.lines.map((x) => {
      const p = l.products.get(x.productId)
      const available = db.data.stock
        .filter((s) => s.productId === x.productId && s.warehouseId === r.warehouseId)
        .reduce((a, s) => a + s.quantity - s.reserved, 0)
      return { ...x, sku: p?.sku ?? '', name: p?.name ?? '', available }
    }),
  }
}

const scoped = (ctx: MockContext, url: URL) => {
  const wh = url.searchParams.get('warehouseId')
  return shipments.all.filter((r) => canSee(ctx, r) && (!wh || r.warehouseId === wh))
}

function mutate(ctx: MockContext, id: string, fn: (r: FboRecord) => FboRecord | Response) {
  const r = shipments.find(id)
  if (!r || !canSee(ctx, r)) return problem(404, 'Заявка не найдена')
  const next = fn(r)
  if (next instanceof Response) return next
  shipments.upsert(next)
  return HttpResponse.json(toDetails(next))
}

const step = (r: FboRecord, status: FboStatus, by: string): FboRecord => ({
  ...r,
  status,
  history: [...r.history, { at: new Date().toISOString(), status, by }],
})

export const fboHandlers = [
  http.get(
    api('/fbo/summary'),
    withAuth(({ ctx, url }) => {
      const list = scoped(ctx, url)
      const active = list.filter((r) => ['confirmed', 'assembling', 'ready'].includes(r.status))
      const end = new Date()
      end.setHours(23, 59, 59, 999)
      const summary: FboSummary = {
        total: list.length,
        byStatus: countBy(list, (r) => r.status),
        unitsInWork: active.reduce((a, r) => a + r.lines.reduce((b, x) => b + x.quantity, 0), 0),
        dueToday: active.filter((r) => Date.parse(r.plannedAt) <= end.getTime()).length,
      }
      return HttpResponse.json(summary)
    }),
  ),

  http.get(
    api('/fbo/stock-options'),
    withAuth(({ ctx }) => {
      if (!ctx.org || ctx.org.type !== 'seller') return HttpResponse.json([])
      const l = lookups()
      const visible = visibleWarehouseIds(ctx)
      const map = new Map<string, FboStockOption>()
      for (const s of db.data.stock) {
        if (s.sellerId !== ctx.org.id || (visible && !visible.has(s.warehouseId))) continue
        const key = `${s.productId}:${s.warehouseId}`
        const p = l.products.get(s.productId)
        const cur = map.get(key) ?? {
          productId: s.productId,
          sku: p?.sku ?? '',
          name: p?.name ?? '',
          available: 0,
          warehouseId: s.warehouseId,
          warehouseName: l.warehouses.get(s.warehouseId)?.name ?? '',
        }
        cur.available += s.quantity - s.reserved
        map.set(key, cur)
      }
      return HttpResponse.json(
        [...map.values()].filter((o) => o.available > 0).sort((a, b) => a.sku.localeCompare(b.sku)),
      )
    }),
  ),

  http.get(
    api('/fbo'),
    withAuth(({ ctx, url }) => {
      const p = listParams(url, { sortField: 'plannedAt' })
      const status = url.searchParams.get('status')
      const mp = url.searchParams.get('marketplace')
      const seller = url.searchParams.get('sellerId')
      const q = p.search.toLowerCase()
      let items = scoped(ctx, url)
        .filter(
          (r) =>
            (!status || r.status === status) &&
            (!mp || r.marketplace === mp) &&
            (!seller || r.sellerId === seller),
        )
        .map(toItem)
      if (q)
        items = items.filter(
          (r) => r.number.toLowerCase().includes(q) || r.destination.toLowerCase().includes(q),
        )
      return HttpResponse.json(paginate(sortItems(items, p.sortField, p.sortOrder), p.page, p.pageSize))
    }),
  ),

  http.get(
    api('/fbo/:id'),
    withAuth(({ ctx, params }) => {
      const r = shipments.find(param(params, 'id'))
      if (!r || !canSee(ctx, r)) return problem(404, 'Заявка не найдена')
      return HttpResponse.json(toDetails(r))
    }),
  ),

  http.post(
    api('/fbo'),
    withAuth(
      async ({ ctx, request }) => {
        const input = (await readJson(request)) as Partial<FboInput>
        if (!ctx.org) return problem(403, 'Недостаточно прав')
        const visible = visibleWarehouseIds(ctx)
        if (!visible?.has(input.warehouseId ?? '')) return problem(422, 'Выберите склад фулфилмента')
        if (!(input.marketplace && input.destination && FBO_DESTINATIONS[input.marketplace]?.includes(input.destination)))
          return problem(422, 'Выберите склад маркетплейса')
        const lines = (input.lines ?? []).filter((x) => x.quantity > 0)
        if (!lines.length) return problem(422, 'Добавьте товары в заявку')
        for (const line of lines) {
          const available = db.data.stock
            .filter(
              (s) =>
                s.productId === line.productId &&
                s.warehouseId === input.warehouseId &&
                s.sellerId === ctx.org!.id,
            )
            .reduce((a, s) => a + s.quantity - s.reserved, 0)
          if (line.quantity > available) {
            const p = lookups().products.get(line.productId)
            return problem(
              422,
              'Недостаточно товара',
              `${p?.sku ?? line.productId}: доступно ${available} шт.`,
            )
          }
        }
        const n = shipments.all.length + 1
        const record: FboRecord = {
          id: `fbo_u${Date.now()}`,
          number: `FBO-${String(3100 + n)}`,
          marketplace: input.marketplace,
          destination: input.destination,
          status: 'draft',
          sellerId: ctx.org.id,
          warehouseId: input.warehouseId!,
          plannedAt: input.plannedAt ?? new Date().toISOString(),
          createdAt: new Date().toISOString(),
          lines: lines.map((x) => ({ ...x, packed: 0 })),
          history: [{ at: new Date().toISOString(), status: 'draft', by: ctx.user.fullName }],
        }
        shipments.upsert(record)
        return HttpResponse.json(toDetails(record), { status: 201 })
      },
      { roles: ['seller_owner', 'seller_manager'] },
    ),
  ),

  http.post(
    api('/fbo/:id/submit'),
    withAuth(
      ({ ctx, params }) =>
        mutate(ctx, param(params, 'id'), (r) =>
          r.status !== 'draft'
            ? problem(409, 'Заявка уже отправлена')
            : {
                ...step(r, 'confirmed', ctx.user.fullName),
                supplyId: `${r.marketplace === 'wb' ? 'WB-GI-' : 'OZ-'}${Math.floor(1e6 + Math.random() * 9e6)}`,
              },
        ),
      { roles: ['seller_owner', 'seller_manager'] },
    ),
  ),

  http.post(
    api('/fbo/:id/cancel'),
    withAuth(
      ({ ctx, params }) =>
        mutate(ctx, param(params, 'id'), (r) =>
          !['draft', 'confirmed'].includes(r.status)
            ? problem(409, 'Отменить можно до начала сборки')
            : step(r, 'cancelled', ctx.user.fullName),
        ),
      { roles: ['seller_owner', 'seller_manager'] },
    ),
  ),

  http.post(
    api('/fbo/:id/advance'),
    withAuth(
      async ({ ctx, params, request }) => {
        const { to } = (await readJson(request)) as { to?: FboStatus }
        return mutate(ctx, param(params, 'id'), (r) => {
          if (!to) return problem(422, 'Укажите статус')
          if (FBO_NEXT[r.status]?.to !== to) return problem(409, 'Недопустимый переход статуса')
          const next = step(r, to, ctx.user.fullName)
          if (to === 'ready') next.lines = next.lines.map((x) => ({ ...x, packed: x.quantity }))
          if (to === 'assembling') next.lines = next.lines.map((x) => ({ ...x, packed: 0 }))
          return next
        })
      },
      { roles: FC_OPS },
    ),
  ),
]
