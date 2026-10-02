import { http } from '@/shared/api/http'
import type { Paged } from '@/shared/api/types'
import type { Order, OrderListItem, OrderListQuery, OrderSummary } from './types'

export const ordersApi = {
  list: (query: OrderListQuery) => http.get<Paged<OrderListItem>>('/orders', { ...query }),
  summary: (query: Omit<OrderListQuery, 'page' | 'pageSize' | 'status'>) =>
    http.get<OrderSummary>('/orders/summary', { ...query }),
  get: (id: string) => http.get<Order>(`/orders/${id}`),
}
