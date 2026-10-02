import type { PageQuery } from '@/shared/api/types'

export type OrderStatus =
  | 'new'
  | 'reserved'
  | 'out_of_stock'
  | 'picking'
  | 'picked'
  | 'packed'
  | 'shipped'
  | 'cancelled'

export type SalesChannel = 'wb' | 'ozon' | 'own'

export interface OrderListItem {
  id: string
  number: string
  externalNumber?: string
  channel: SalesChannel
  status: OrderStatus
  sellerId: string
  sellerName: string
  warehouseId: string
  warehouseName: string
  createdAt: string
  shipBy: string
  shippedAt?: string
  linesCount: number
  unitsCount: number
  amount: number
  deliveryCity: string
  isOverdue: boolean
}

export interface OrderLine {
  productId: string
  sku: string
  name: string
  barcode: string
  quantity: number
  price: number
}

export interface Order extends OrderListItem {
  lines: OrderLine[]
}

export interface OrderListQuery extends PageQuery {
  status?: OrderStatus | 'overdue'
  channel?: SalesChannel
  sellerId?: string
  warehouseId?: string
}

export interface OrderSummary {
  total: number
  overdue: number
  byStatus: Partial<Record<OrderStatus, number>>
}
