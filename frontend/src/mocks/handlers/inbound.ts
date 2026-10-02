import { http, HttpResponse } from 'msw'
import type { InboundAction, InboundEvent, InboundListItem, InboundStatus, InboundSummary } from '@/modules/inbound/types'
import { FC_OFFICE_ROLES, SELLER_ROLES } from '@/modules/auth/roles'
import { db } from '../db/store'
import type { InboundRecord } from '../db/types'
import { createRandom } from '../lib/random'
import { countBy, listParams, paginate, param, problem, sortItems, withAuth, type MockContext } from '../lib/http'
import { lookups, toInbound, toInboundListItem } from '../lib/mappers'
import { canSee, scopedInbound } from '../lib/scope'
import { api } from './api'

function filteredInbound(ctx: MockContext, url: URL, withStatus: boolean): InboundListItem[] {
  const q = url.searchParams
  const { search } = listParams(url)
  const l = lookups()
  const status = withStatus ? q.get('status') : null
  const sellerId = q.get('sellerId')
  return scopedInbound(ctx, q.get('warehouseId'))
    .filter((r) => (!status || r.status === status) && (!sellerId || r.sellerId === sellerId))
    .map((r) => toInboundListItem(r, l))
    .filter(
      (item) =>
        !search ||
        item.number.toLowerCase().includes(search) ||
        item.sellerName.toLowerCase().includes(search) ||
        item.warehouseName.toLowerCase().includes(search),
    )
}

const TRANSITIONS: Record<InboundAction, { from: InboundStatus[]; to: InboundStatus; title: string }> = {
  arrive: { from: ['in_transit'], to: 'arrived', title: 'Машина прибыла на склад' },
  start_receiving: { from: ['arrived'], to: 'receiving', title: 'Начата приёмка' },
  complete_receiving: { from: ['receiving'], to: 'received', title: 'Приёмка завершена' },
  complete_placement: { from: ['received'], to: 'placed', title: 'Товар размещён в ячейках' },
  cancel: { from: ['draft', 'in_transit'], to: 'cancelled', title: 'Поставка отменена' },
}

function applyAction(record: InboundRecord, action: InboundAction, actorName: string): InboundRecord {
  const next: InboundRecord = structuredClone(record)
  const at = new Date().toISOString()
  const events: InboundEvent[] = next.events
  const push = (type: InboundEvent['type'], title: string, description?: string) =>
    events.push({ id: `${next.id}_e${events.length + 1}_${Date.now().toString(36)}`, type, at, title, actorName, description })

  const transition = TRANSITIONS[action]
  next.status = transition.to

  if (action === 'complete_receiving') {
    const r = createRandom(Date.now())
    for (const line of next.lines) {
      if (line.receivedQty === 0) line.receivedQty = line.expectedQty - (r.bool(0.15) ? r.int(1, 4) : 0)
      line.defectQty = r.bool(0.08) ? r.int(1, 2) : 0
    }
    const expected = next.lines.reduce((s, l) => s + l.expectedQty, 0)
    const received = next.lines.reduce((s, l) => s + l.receivedQty, 0)
    push('received', transition.title, `Принято ${received} из ${expected} шт`)
    const shortage = next.lines.reduce((s, l) => s + Math.max(0, l.expectedQty - l.receivedQty), 0)
    const defect = next.lines.reduce((s, l) => s + l.defectQty, 0)
    if (shortage || defect) {
      const parts = [shortage && `недостача ${shortage} шт`, defect && `брак ${defect} шт`].filter(Boolean)
      push('discrepancy', 'Обнаружены расхождения', parts.join(', '))
    }
  } else if (action === 'complete_placement') {
    const locations = db.data.locations.filter((l) => l.warehouseId === next.warehouseId)
    for (const line of next.lines) {
      const good = line.receivedQty - line.defectQty
      if (good <= 0) continue
      const existing = db.data.stock.find((s) => s.productId === line.productId && s.warehouseId === next.warehouseId)
      if (existing) {
        db.upsert('stock', { ...existing, quantity: existing.quantity + good })
      } else {
        db.upsert('stock', {
          id: `stk_${line.productId}_${next.warehouseId}_${Date.now().toString(36)}`,
          productId: line.productId,
          sellerId: next.sellerId,
          warehouseId: next.warehouseId,
          locationId: locations[Math.floor(Math.random() * locations.length)]!.id,
          quantity: good,
          reserved: 0,
        })
      }
    }
    push('placed', transition.title, `Размещено ${next.lines.length} позиций`)
  } else {
    push(transition.to, transition.title)
  }
  return next
}

export const inboundHandlers = [
  http.get(
    api('/inbound-shipments/summary'),
    withAuth(({ ctx, url }) => {
      const items = filteredInbound(ctx, url, false)
      const summary: InboundSummary = { total: items.length, byStatus: countBy(items, (i) => i.status) }
      return HttpResponse.json(summary)
    }),
  ),

  http.get(
    api('/inbound-shipments'),
    withAuth(({ ctx, url }) => {
      const params = listParams(url, { sortField: 'expectedAt', sortOrder: 'desc' })
      const sorted = sortItems(filteredInbound(ctx, url, true), params.sortField, params.sortOrder)
      return HttpResponse.json(paginate(sorted, params.page, params.pageSize))
    }),
  ),

  http.get(
    api('/inbound-shipments/:id'),
    withAuth(({ ctx, params }) => {
      const record = db.data.inbound.find((r) => r.id === param(params, 'id'))
      if (!record || !canSee(ctx, record)) return problem(404, 'Поставка не найдена')
      return HttpResponse.json(toInbound(record, lookups()))
    }),
  ),

  http.post(
    api('/inbound-shipments/:id/actions/:action'),
    withAuth(({ ctx, params }) => {
      const record = db.data.inbound.find((r) => r.id === param(params, 'id'))
      if (!record || !canSee(ctx, record)) return problem(404, 'Поставка не найдена')
      const action = param(params, 'action') as InboundAction
      const transition = TRANSITIONS[action]
      if (!transition) return problem(400, 'Неизвестное действие')
      const allowedRoles = action === 'cancel' ? [...FC_OFFICE_ROLES, ...SELLER_ROLES] : FC_OFFICE_ROLES
      if (!allowedRoles.includes(ctx.role)) return problem(403, 'Недостаточно прав для этого действия')
      if (!transition.from.includes(record.status)) {
        return problem(409, 'Действие недоступно', 'Статус поставки изменился. Обновите страницу.')
      }
      const updated = applyAction(record, action, ctx.user.fullName)
      db.upsert('inbound', updated)
      return HttpResponse.json(toInbound(updated, lookups()))
    }),
  ),
]
