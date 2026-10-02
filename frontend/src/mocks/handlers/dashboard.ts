import { http, HttpResponse } from 'msw'
import type { AttentionItem, DashboardPeriod, FcDashboard, SellerDashboard } from '@/modules/dashboard/types'
import type { OrderStatus, SalesChannel } from '@/modules/orders/types'
import { db } from '../db/store'
import { DAY, isOrderActive } from '../db/seed'
import type { OrderRecord } from '../db/types'
import { problem, withAuth } from '../lib/http'
import { inboundHasDiscrepancy, lookups, toInboundListItem, warehouseOccupancy } from '../lib/mappers'
import { scopedInbound, scopedOrders, visibleWarehouseIds } from '../lib/scope'
import { api } from './api'

const PERIOD_DAYS: Record<DashboardPeriod, number> = { '7d': 7, '30d': 30, '90d': 90 }

function startOfDay(ms: number) {
  const date = new Date(ms)
  date.setHours(0, 0, 0, 0)
  return date.getTime()
}

const dayKey = (ms: number) => {
  const d = new Date(ms)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function dayBuckets(days: number, now: number) {
  const today = startOfDay(now)
  return Array.from({ length: days }, (_, index) => dayKey(today - (days - 1 - index) * DAY))
}

const between = (iso: string | undefined, from: number, to: number) => {
  if (!iso) return false
  const t = Date.parse(iso)
  return t >= from && t < to
}

const units = (order: OrderRecord) => order.lines.reduce((sum, line) => sum + line.quantity, 0)

const PIPELINE: OrderStatus[] = ['new', 'reserved', 'picking', 'picked', 'packed']

export const dashboardHandlers = [
  http.get(
    api('/dashboard/fulfillment'),
    withAuth(({ ctx, url }) => {
      if (ctx.org?.type !== 'fulfillment') return problem(403, 'Дашборд доступен только фулфилмент-центрам')
      const now = Date.now()
      const today = startOfDay(now)
      const elapsed = now - today
      const period = (url.searchParams.get('period') as DashboardPeriod) ?? '30d'
      const days = PERIOD_DAYS[period] ?? 30
      const warehouseId = url.searchParams.get('warehouseId') || null
      const orders = scopedOrders(ctx, warehouseId)
      const inbound = scopedInbound(ctx, warehouseId)
      const l = lookups()

      const active = orders.filter((o) => isOrderActive(o.status))
      const overdue = active.filter((o) => Date.parse(o.shipBy) < now)
      const outOfStock = active.filter((o) => o.status === 'out_of_stock')
      const arrived = inbound.filter((i) => i.status === 'arrived')
      const awaitingPlacement = inbound.filter((i) => i.status === 'received')
      const newOrders = active.filter((o) => o.status === 'new')

      const visible = [...(visibleWarehouseIds(ctx) ?? [])]
      const scopeWarehouses = warehouseId ? [warehouseId] : visible
      const occupancy = scopeWarehouses.reduce(
        (acc, id) => {
          const o = warehouseOccupancy(id)
          return { total: acc.total + o.total, occupied: acc.occupied + o.occupied }
        },
        { total: 0, occupied: 0 },
      )

      const attention: AttentionItem[] = [
        {
          key: 'overdue',
          title: 'Просроченные заказы',
          description: 'Срок отгрузки истёк — маркетплейс может оштрафовать',
          count: overdue.length,
          tone: 'danger' as const,
          section: 'orders',
          query: { status: 'overdue' },
        },
        {
          key: 'out_of_stock',
          title: 'Заказы без остатка',
          description: 'Товара не хватает для резерва',
          count: outOfStock.length,
          tone: 'danger' as const,
          section: 'orders',
          query: { status: 'out_of_stock' },
        },
        {
          key: 'arrived',
          title: 'Поставки ждут приёмки',
          description: 'Машины на складе, приёмка не начата',
          count: arrived.length,
          tone: 'warn' as const,
          section: 'inbound',
          query: { status: 'arrived' },
        },
        {
          key: 'placement',
          title: 'Ждут размещения',
          description: 'Товар принят, но ещё не в ячейках',
          count: awaitingPlacement.length,
          tone: 'info' as const,
          section: 'inbound',
          query: { status: 'received' },
        },
        {
          key: 'new',
          title: 'Новые заказы',
          description: 'Ожидают резервирования',
          count: newOrders.length,
          tone: 'info' as const,
          section: 'orders',
          query: { status: 'new' },
        },
      ].filter((item) => item.count > 0)

      const buckets = dayBuckets(days, now)
      const trendMap = new Map(buckets.map((key) => [key, { date: key, created: 0, shipped: 0 }]))
      const periodStart = today - (days - 1) * DAY
      const channels = new Map<SalesChannel, number>()
      const clients = new Map<string, { orders: number; units: number }>()
      for (const order of orders) {
        const created = Date.parse(order.createdAt)
        if (created >= periodStart) {
          trendMap.get(dayKey(created))!.created++
          channels.set(order.channel, (channels.get(order.channel) ?? 0) + 1)
          const client = clients.get(order.sellerId) ?? { orders: 0, units: 0 }
          client.orders++
          client.units += units(order)
          clients.set(order.sellerId, client)
        }
        if (order.shippedAt && Date.parse(order.shippedAt) >= periodStart) {
          const bucket = trendMap.get(dayKey(Date.parse(order.shippedAt)))
          if (bucket) bucket.shipped++
        }
      }

      const body: FcDashboard = {
        kpis: {
          ordersToday: {
            value: orders.filter((o) => between(o.createdAt, today, now + 1)).length,
            previous: orders.filter((o) => between(o.createdAt, today - DAY, today - DAY + elapsed)).length,
          },
          shippedToday: {
            value: orders.filter((o) => between(o.shippedAt, today, now + 1)).length,
            previous: orders.filter((o) => between(o.shippedAt, today - DAY, today - DAY + elapsed)).length,
          },
          inProgress: { value: active.length },
          overdue: { value: overdue.length },
          inboundToday: {
            value: inbound.filter((i) => i.status !== 'cancelled' && between(i.expectedAt, today, today + DAY)).length,
          },
          occupancy: { value: occupancy.total ? Math.round((occupancy.occupied / occupancy.total) * 100) : 0 },
        },
        pipeline: PIPELINE.map((status) => ({ status, count: active.filter((o) => o.status === status).length })),
        attention,
        trend: [...trendMap.values()],
        channels: [...channels.entries()].map(([channel, count]) => ({ channel, count })).sort((a, b) => b.count - a.count),
        warehouses: visible.map((id) => {
          const o = warehouseOccupancy(id)
          return {
            id,
            name: l.warehouses.get(id)?.name ?? id,
            occupancy: o.total ? Math.round((o.occupied / o.total) * 100) : 0,
            inProgress: scopedOrders(ctx, id).filter((order) => isOrderActive(order.status)).length,
          }
        }),
        topClients: [...clients.entries()]
          .map(([id, value]) => ({ id, name: l.orgs.get(id)?.name ?? id, ...value }))
          .sort((a, b) => b.orders - a.orders)
          .slice(0, 5),
      }
      return HttpResponse.json(body)
    }),
  ),

  http.get(
    api('/dashboard/seller'),
    withAuth(({ ctx, url }) => {
      if (ctx.org?.type !== 'seller') return problem(403, 'Дашборд доступен только селлерам')
      const sellerId = ctx.org.id
      const now = Date.now()
      const today = startOfDay(now)
      const period = (url.searchParams.get('period') as DashboardPeriod) ?? '30d'
      const days = PERIOD_DAYS[period] ?? 30
      const periodStart = today - (days - 1) * DAY
      const prevStart = periodStart - days * DAY
      const l = lookups()

      const orders = scopedOrders(ctx)
      const inbound = scopedInbound(ctx)
      const stock = db.data.stock.filter((s) => s.sellerId === sellerId)
      const products = db.data.products.filter((p) => p.sellerId === sellerId)

      const stockUnits = stock.reduce((sum, s) => sum + s.quantity, 0)
      const stockValue = stock.reduce((sum, s) => sum + s.quantity * (l.products.get(s.productId)?.price ?? 0), 0)

      const inPeriod = orders.filter((o) => Date.parse(o.createdAt) >= periodStart)
      const inPrev = orders.filter((o) => between(o.createdAt, prevStart, periodStart))
      const onTime = (list: OrderRecord[]) => {
        const shipped = list.filter((o) => o.shippedAt)
        if (!shipped.length) return 100
        const good = shipped.filter((o) => Date.parse(o.shippedAt!) <= Date.parse(o.shipBy)).length
        return Math.round((good / shipped.length) * 1000) / 10
      }

      const monthStart = new Date(today)
      monthStart.setDate(1)
      const monthDays = Math.max(1, Math.ceil((now - monthStart.getTime()) / DAY))
      const storageLiters = stock.reduce((sum, s) => sum + s.quantity * (l.products.get(s.productId)?.volumeLiters ?? 1), 0)
      const monthOrders = orders.filter((o) => Date.parse(o.createdAt) >= monthStart.getTime())
      const charges =
        storageLiters * 0.012 * monthDays + monthOrders.length * 45 + monthOrders.reduce((sum, o) => sum + units(o), 0) * 12

      const soldByProduct = new Map<string, number>()
      const salesWindow = today - 30 * DAY
      for (const order of orders) {
        if (Date.parse(order.createdAt) < salesWindow || order.status === 'cancelled') continue
        for (const line of order.lines) soldByProduct.set(line.productId, (soldByProduct.get(line.productId) ?? 0) + line.quantity)
      }
      const lowStock = products
        .map((product) => {
          const available = stock
            .filter((s) => s.productId === product.id)
            .reduce((sum, s) => sum + s.quantity - s.reserved, 0)
          const dailySales = (soldByProduct.get(product.id) ?? 0) / 30
          return {
            productId: product.id,
            sku: product.sku,
            name: product.name,
            available,
            dailySales: Math.round(dailySales * 10) / 10,
            daysLeft: dailySales > 0 ? Math.floor(available / dailySales) : 999,
          }
        })
        .filter((item) => item.dailySales > 0)
        .sort((a, b) => a.daysLeft - b.daysLeft)

      const recentDiscrepancies = inbound.filter(
        (i) => inboundHasDiscrepancy(i) && Date.parse(i.expectedAt) > today - 14 * DAY,
      )
      const overdue = orders.filter((o) => isOrderActive(o.status) && Date.parse(o.shipBy) < now)
      const inTransit = inbound.filter((i) => i.status === 'in_transit')

      const attention: AttentionItem[] = [
        {
          key: 'low_stock',
          title: 'Товары заканчиваются',
          description: 'Запаса хватит менее чем на 7 дней продаж',
          count: lowStock.filter((i) => i.daysLeft < 7).length,
          tone: 'warn' as const,
          section: 'stock',
        },
        {
          key: 'overdue',
          title: 'Заказы с просроченной отгрузкой',
          description: 'ФЦ не успел отгрузить в срок',
          count: overdue.length,
          tone: 'danger' as const,
          section: 'orders',
          query: { status: 'overdue' },
        },
        {
          key: 'discrepancy',
          title: 'Поставки с расхождениями',
          description: 'За последние 14 дней — проверьте акты',
          count: recentDiscrepancies.length,
          tone: 'warn' as const,
          section: 'inbound',
        },
        {
          key: 'in_transit',
          title: 'Поставки в пути',
          description: 'Ожидаются на складах ФЦ',
          count: inTransit.length,
          tone: 'info' as const,
          section: 'inbound',
          query: { status: 'in_transit' },
        },
      ].filter((item) => item.count > 0)

      const buckets = dayBuckets(days, now)
      const trendMap = new Map(buckets.map((key) => [key, { date: key, wb: 0, ozon: 0, own: 0 }]))
      for (const order of inPeriod) trendMap.get(dayKey(Date.parse(order.createdAt)))![order.channel]++

      const contracts = db.data.contracts.filter((c) => c.sellerId === sellerId)
      const body: SellerDashboard = {
        kpis: {
          stockUnits: { value: stockUnits },
          stockValue: { value: Math.round(stockValue) },
          orders30d: { value: inPeriod.length, previous: inPrev.length },
          onTimeRate: { value: onTime(inPeriod), previous: onTime(inPrev) },
          chargesMonth: { value: Math.round(charges) },
        },
        attention,
        trend: [...trendMap.values()],
        fulfillments: contracts.map((contract) => {
          const fcStock = stock.filter((s) => contract.warehouseIds.includes(s.warehouseId))
          const org = l.orgs.get(contract.fulfillmentId)
          return {
            id: contract.fulfillmentId,
            name: org?.name ?? '—',
            city: org?.city ?? '',
            units: fcStock.reduce((sum, s) => sum + s.quantity, 0),
            skus: new Set(fcStock.filter((s) => s.quantity > 0).map((s) => s.productId)).size,
            warehouses: contract.warehouseIds.length,
          }
        }),
        lowStock: lowStock.slice(0, 6),
        recentInbound: [...inbound]
          .filter((i) => i.status !== 'draft')
          .sort((a, b) => Date.parse(b.expectedAt) - Date.parse(a.expectedAt))
          .slice(0, 5)
          .map((i) => toInboundListItem(i, l)),
      }
      return HttpResponse.json(body)
    }),
  ),
]
