import { http, HttpResponse } from 'msw'
import type { AppNotification } from '@/modules/notifications/api'
import type { SearchResult } from '@/modules/search/api'
import { INBOUND_STATUSES, ORDER_STATUSES } from '@/shared/config/statuses'
import { db } from '../db/store'
import { DAY, isOrderActive } from '../db/seed'
import { withAuth, type MockContext } from '../lib/http'
import { inboundHasDiscrepancy, lookups } from '../lib/mappers'
import { scopedInbound, scopedOrders } from '../lib/scope'
import { api } from './api'

function buildNotifications(ctx: MockContext): Omit<AppNotification, 'read'>[] {
  const now = Date.now()
  const l = lookups()
  if (!ctx.org) {
    return db.data.organizations
      .filter((o) => o.status === 'pending')
      .map((o) => ({
        id: `pending_${o.id}`,
        title: 'Новая заявка на регистрацию',
        text: `${o.name} (${o.city}) ожидает проверки`,
        at: o.createdAt,
        tone: 'warn' as const,
      }))
  }

  const inbound = scopedInbound(ctx)
  const result: Omit<AppNotification, 'read'>[] = []
  if (ctx.org.type === 'fulfillment') {
    const overdue = scopedOrders(ctx).filter((o) => isOrderActive(o.status) && Date.parse(o.shipBy) < now)
    if (overdue.length) {
      result.push({
        id: `overdue_${new Date().toDateString()}`,
        title: 'Есть просроченные заказы',
        text: `${overdue.length} заказов не отгружены в срок`,
        at: new Date(now - 20 * 60_000).toISOString(),
        tone: 'danger',
      })
    }
    for (const item of inbound.filter((i) => i.status === 'arrived' || i.status === 'receiving').slice(0, 4)) {
      const event = item.events[item.events.length - 1]!
      result.push({
        id: `inb_${item.id}_${item.status}`,
        title: item.status === 'arrived' ? `Поставка ${item.number} прибыла` : `Идёт приёмка ${item.number}`,
        text: `${l.orgs.get(item.sellerId)?.name} · ${l.warehouses.get(item.warehouseId)?.name}`,
        at: event.at,
        tone: item.status === 'arrived' ? 'warn' : 'info',
        link: { type: 'inbound', id: item.id },
      })
    }
  } else {
    const recent = inbound
      .filter((i) => (i.status === 'received' || i.status === 'placed') && Date.parse(i.expectedAt) > now - 10 * DAY)
      .sort((a, b) => Date.parse(b.expectedAt) - Date.parse(a.expectedAt))
      .slice(0, 5)
    for (const item of recent) {
      const discrepancy = inboundHasDiscrepancy(item)
      const event = item.events[item.events.length - 1]!
      result.push({
        id: `inb_${item.id}_${item.status}`,
        title: discrepancy ? `Поставка ${item.number} принята с расхождениями` : `Поставка ${item.number} ${INBOUND_STATUSES[item.status].label.toLowerCase()}`,
        text: `${l.warehouses.get(item.warehouseId)?.name} · ${event.description ?? ''}`,
        at: event.at,
        tone: discrepancy ? 'warn' : 'success',
        link: { type: 'inbound', id: item.id },
      })
    }
  }
  return result.sort((a, b) => Date.parse(b.at) - Date.parse(a.at))
}

export const miscHandlers = [
  http.get(
    api('/notifications'),
    withAuth(({ ctx }) => {
      const read = new Set(db.data.notificationReads.map((r) => r.id))
      const items: AppNotification[] = buildNotifications(ctx).map((n) => ({
        ...n,
        read: read.has(`${ctx.user.id}:${n.id}`),
      }))
      return HttpResponse.json(items)
    }),
  ),

  http.post(
    api('/notifications/read-all'),
    withAuth(({ ctx }) => {
      for (const n of buildNotifications(ctx)) db.upsert('notificationReads', { id: `${ctx.user.id}:${n.id}` })
      return new HttpResponse(null, { status: 204 })
    }),
  ),

  http.get(
    api('/search'),
    withAuth(({ ctx, url }) => {
      const q = (url.searchParams.get('q') ?? '').trim().toLowerCase()
      if (q.length < 2) return HttpResponse.json([])
      const l = lookups()
      const results: SearchResult[] = []

      for (const order of scopedOrders(ctx)) {
        if (results.length >= 5) break
        if (order.number.toLowerCase().includes(q) || order.externalNumber?.toLowerCase().includes(q)) {
          results.push({
            type: 'order',
            id: order.id,
            title: order.number,
            subtitle: `${ORDER_STATUSES[order.status].label} · ${l.orgs.get(order.sellerId)?.name} · ${order.externalNumber ?? ''}`,
          })
        }
      }
      let inboundCount = 0
      for (const item of scopedInbound(ctx)) {
        if (inboundCount >= 5) break
        if (item.number.toLowerCase().includes(q)) {
          inboundCount++
          results.push({
            type: 'inbound',
            id: item.id,
            title: item.number,
            subtitle: `${INBOUND_STATUSES[item.status].label} · ${l.orgs.get(item.sellerId)?.name} · ${l.warehouses.get(item.warehouseId)?.name}`,
          })
        }
      }
      const sellerScope = ctx.org?.type === 'seller' ? ctx.org.id : null
      const fcSellers =
        ctx.org?.type === 'fulfillment'
          ? new Set(db.data.contracts.filter((c) => c.fulfillmentId === ctx.org!.id).map((c) => c.sellerId))
          : null
      let productCount = 0
      for (const product of db.data.products) {
        if (productCount >= 5) break
        if (sellerScope && product.sellerId !== sellerScope) continue
        if (fcSellers && !fcSellers.has(product.sellerId)) continue
        if (
          product.sku.toLowerCase().includes(q) ||
          product.name.toLowerCase().includes(q) ||
          product.barcode.includes(q)
        ) {
          productCount++
          results.push({
            type: 'product',
            id: product.id,
            title: product.name,
            subtitle: `${product.sku} · ШК ${product.barcode} · ${l.orgs.get(product.sellerId)?.name}`,
          })
        }
      }
      return HttpResponse.json(results)
    }),
  ),
]
