import { http, HttpResponse } from 'msw'
import type { Role } from '@/modules/auth/types'
import { RETURN_GRADES } from '@/modules/returns/labels'
import type {
  InspectInput,
  ReturnDetails,
  ReturnGrade,
  ReturnListItem,
  ReturnReason,
  ReturnStatus,
  ReturnSummary,
} from '@/modules/returns/types'
import { HOUR } from '../db/seed'
import { db } from '../db/store'
import { localCollection } from '../lib/collection'
import { parseBody, countBy, listParams, paginate, param, problem, sortItems, withAuth, type MockContext } from '../lib/http'
import { inspectSchema } from '../lib/schemas'
import { lookups } from '../lib/mappers'
import { createRandom } from '../lib/random'
import { canSee } from '../lib/scope'
import { api } from './api'

const FC_OPS: Role[] = ['fc_owner', 'fc_manager', 'fc_supervisor', 'fc_receiver']

interface ReturnRecord {
  id: string
  number: string
  orderId: string
  reason: ReturnReason
  status: ReturnStatus
  sellerId: string
  warehouseId: string
  createdAt: string
  receivedAt?: string
  inspectedAt?: string
  inspectedBy?: string
  comment?: string
  photos: number
  lines: { productId: string; quantity: number; grade?: ReturnGrade }[]
}

function seedReturns(): ReturnRecord[] {
  const rng = createRandom(4242)
  const now = Date.now()
  const shipped = db.data.orders.filter((o) => o.status === 'shipped' || o.status === 'packed')
  const picked = rng.sample(shipped, Math.min(64, shipped.length))
  return picked.map((o, i) => {
    const status = rng.weighted<ReturnStatus>([
      ['expected', 3],
      ['received', 3],
      ['inspected', 2],
      ['closed', 5],
    ])
    const reason = rng.weighted<ReturnReason>([
      ['not_bought', 6],
      ['size', 3],
      ['defect', 2],
      ['damaged', 1],
      ['wrong_item', 1],
    ])
    const created = now - rng.int(2, 30 * 24) * HOUR
    const lines = o.lines.slice(0, rng.int(1, Math.min(3, o.lines.length))).map((l) => ({
      productId: l.productId,
      quantity: Math.min(l.quantity, rng.int(1, 2)),
      grade:
        status === 'inspected' || status === 'closed'
          ? rng.weighted<ReturnGrade>([
              ['as_new', reason === 'not_bought' ? 7 : 2],
              ['resale', 3],
              ['defect', reason === 'defect' || reason === 'damaged' ? 6 : 1],
              ['dispose', 1],
            ])
          : undefined,
    }))
    const received = status !== 'expected' ? created + rng.int(12, 72) * HOUR : undefined
    return {
      id: `ret_${i + 1}`,
      number: `ВЗ-${String(20400 + i)}`,
      orderId: o.id,
      reason,
      status,
      sellerId: o.sellerId,
      warehouseId: o.warehouseId,
      createdAt: new Date(created).toISOString(),
      receivedAt: received ? new Date(Math.min(now, received)).toISOString() : undefined,
      inspectedAt:
        received && (status === 'inspected' || status === 'closed')
          ? new Date(Math.min(now, received + rng.int(2, 20) * HOUR)).toISOString()
          : undefined,
      inspectedBy: status === 'inspected' || status === 'closed' ? 'Ольга Петрова' : undefined,
      photos: status === 'expected' ? 0 : rng.int(1, 4),
      lines,
    }
  })
}

const returns = localCollection<ReturnRecord>('returns', seedReturns)

function resolution(r: ReturnRecord): ReturnListItem['resolution'] {
  if (r.status !== 'closed' && r.status !== 'inspected') return undefined
  const restock = r.lines.filter((l) => l.grade && RETURN_GRADES[l.grade].restock).length
  return restock === r.lines.length ? 'restocked' : restock === 0 ? 'defect' : 'mixed'
}

function toItem(r: ReturnRecord): ReturnListItem {
  const l = lookups()
  const order = db.data.orders.find((o) => o.id === r.orderId)
  return {
    id: r.id,
    number: r.number,
    orderNumber: order?.number ?? '—',
    channel: order?.channel ?? 'wb',
    reason: r.reason,
    status: r.status,
    sellerId: r.sellerId,
    sellerName: l.orgs.get(r.sellerId)?.name ?? '',
    warehouseId: r.warehouseId,
    warehouseName: l.warehouses.get(r.warehouseId)?.name ?? '',
    units: r.lines.reduce((a, x) => a + x.quantity, 0),
    createdAt: r.createdAt,
    receivedAt: r.receivedAt,
    inspectedAt: r.inspectedAt,
    resolution: resolution(r),
  }
}

function toDetails(r: ReturnRecord): ReturnDetails {
  const l = lookups()
  return {
    ...toItem(r),
    comment: r.comment,
    inspectedBy: r.inspectedBy,
    photos: r.photos,
    lines: r.lines.map((x) => {
      const p = l.products.get(x.productId)
      return {
        productId: x.productId,
        sku: p?.sku ?? '',
        name: p?.name ?? '',
        quantity: x.quantity,
        grade: x.grade,
      }
    }),
  }
}

function scoped(ctx: MockContext, url: URL) {
  const wh = url.searchParams.get('warehouseId')
  const seller = url.searchParams.get('sellerId')
  return returns.all.filter(
    (r) => canSee(ctx, r) && (!wh || r.warehouseId === wh) && (!seller || r.sellerId === seller),
  )
}

function mutate(ctx: MockContext, id: string, apply: (r: ReturnRecord) => ReturnRecord | Response) {
  const r = returns.find(id)
  if (!r || !canSee(ctx, r)) return problem(404, 'Возврат не найден')
  const next = apply(r)
  if (next instanceof Response) return next
  returns.upsert(next)
  return HttpResponse.json(toDetails(next))
}

export const returnsHandlers = [
  http.get(
    api('/returns/summary'),
    withAuth(({ ctx, url }) => {
      const list = scoped(ctx, url)
      const graded = list.flatMap((r) => r.lines.filter((l) => l.grade))
      const restock = graded.filter((l) => RETURN_GRADES[l.grade!].restock)
      const summary: ReturnSummary = {
        total: list.length,
        byStatus: countBy(list, (r) => r.status),
        restockRate: graded.length ? Math.round((restock.length / graded.length) * 100) : 0,
        defectUnits: graded
          .filter((l) => !RETURN_GRADES[l.grade!].restock)
          .reduce((a, l) => a + l.quantity, 0),
      }
      return HttpResponse.json(summary)
    }),
  ),

  http.get(
    api('/returns'),
    withAuth(({ ctx, url }) => {
      const p = listParams(url, { sortField: 'createdAt' })
      const status = url.searchParams.get('status')
      const reason = url.searchParams.get('reason')
      const search = p.search.toLowerCase()
      let items = scoped(ctx, url)
        .filter((r) => (!status || r.status === status) && (!reason || r.reason === reason))
        .map(toItem)
      if (search)
        items = items.filter(
          (r) => r.number.toLowerCase().includes(search) || r.orderNumber.toLowerCase().includes(search),
        )
      return HttpResponse.json(paginate(sortItems(items, p.sortField, p.sortOrder), p.page, p.pageSize))
    }),
  ),

  http.get(
    api('/returns/:id'),
    withAuth(({ ctx, params }) => {
      const r = returns.find(param(params, 'id'))
      if (!r || !canSee(ctx, r)) return problem(404, 'Возврат не найден')
      return HttpResponse.json(toDetails(r))
    }),
  ),

  http.post(
    api('/returns/:id/receive'),
    withAuth(
      ({ ctx, params }) =>
        mutate(ctx, param(params, 'id'), (r) =>
          r.status !== 'expected'
            ? problem(409, 'Возврат уже принят')
            : { ...r, status: 'received', receivedAt: new Date().toISOString() },
        ),
      { roles: FC_OPS },
    ),
  ),

  http.post(
    api('/returns/:id/inspect'),
    withAuth(
      async ({ ctx, params, request }) => {
        const input = (await parseBody(request, inspectSchema)) as unknown as InspectInput
        return mutate(ctx, param(params, 'id'), (r) => {
          if (r.status !== 'received') return problem(409, 'Оценка доступна после приёмки')
          if (r.lines.some((l) => !input.grades[l.productId]))
            return problem(422, 'Оцените все позиции', 'Для каждой позиции нужно указать состояние товара.')
          return {
            ...r,
            status: 'inspected',
            inspectedAt: new Date().toISOString(),
            inspectedBy: ctx.user.fullName,
            comment: input.comment?.trim() || undefined,
            photos: Math.max(r.photos, 1),
            lines: r.lines.map((l) => ({ ...l, grade: input.grades[l.productId] })),
          }
        })
      },
      { roles: FC_OPS },
    ),
  ),

  http.post(
    api('/returns/:id/close'),
    withAuth(
      ({ ctx, params }) =>
        mutate(ctx, param(params, 'id'), (r) => {
          if (r.status !== 'inspected') return problem(409, 'Закрыть можно только оценённый возврат')
          // Годный товар возвращается в остаток (в ячейку приёмки), брак — в карантин
          for (const line of r.lines) {
            const stock = db.data.stock.find(
              (s) => s.productId === line.productId && s.warehouseId === r.warehouseId,
            )
            if (stock && line.grade && RETURN_GRADES[line.grade].restock)
              db.upsert('stock', { ...stock, quantity: stock.quantity + line.quantity })
          }
          return { ...r, status: 'closed' }
        }),
      { roles: FC_OPS },
    ),
  ),
]
