import { http, HttpResponse } from 'msw'
import type { OrderListItem, OrderSummary } from '@/modules/orders/types'
import { db } from '../db/store'
import { countBy, listParams, paginate, param, problem, sortItems, withAuth, type MockContext } from '../lib/http'
import { lookups, toOrder, toOrderListItem } from '../lib/mappers'
import { canSee, scopedOrders } from '../lib/scope'
import { api } from './api'

function filteredOrders(ctx: MockContext, url: URL, withStatus: boolean): OrderListItem[] {
  const q = url.searchParams
  const { search } = listParams(url)
  const l = lookups()
  const now = Date.now()
  const status = withStatus ? q.get('status') : null
  const channel = q.get('channel')
  const sellerId = q.get('sellerId')

  return scopedOrders(ctx, q.get('warehouseId'))
    .filter((order) => (!channel || order.channel === channel) && (!sellerId || order.sellerId === sellerId))
    .map((order) => toOrderListItem(order, l, now))
    .filter((order) => {
      if (status === 'overdue' && !order.isOverdue) return false
      if (status && status !== 'overdue' && order.status !== status) return false
      if (!search) return true
      return (
        order.number.toLowerCase().includes(search) ||
        (order.externalNumber?.toLowerCase().includes(search) ?? false) ||
        order.deliveryCity.toLowerCase().includes(search) ||
        order.sellerName.toLowerCase().includes(search)
      )
    })
}

export const orderHandlers = [
  http.get(
    api('/orders/summary'),
    withAuth(({ ctx, url }) => {
      const orders = filteredOrders(ctx, url, false)
      const summary: OrderSummary = {
        total: orders.length,
        overdue: orders.filter((o) => o.isOverdue).length,
        byStatus: countBy(orders, (o) => o.status),
      }
      return HttpResponse.json(summary)
    }),
  ),

  http.get(
    api('/orders'),
    withAuth(({ ctx, url }) => {
      const params = listParams(url, { sortField: 'createdAt', sortOrder: 'desc' })
      const sorted = sortItems(filteredOrders(ctx, url, true), params.sortField, params.sortOrder)
      return HttpResponse.json(paginate(sorted, params.page, params.pageSize))
    }),
  ),

  http.get(
    api('/orders/:id'),
    withAuth(({ ctx, params }) => {
      const order = db.data.orders.find((o) => o.id === param(params, 'id'))
      if (!order || !canSee(ctx, order)) return problem(404, 'Заказ не найден')
      return HttpResponse.json(toOrder(order, lookups()))
    }),
  ),
]
