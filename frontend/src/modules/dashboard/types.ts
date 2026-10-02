import type { InboundListItem } from '@/modules/inbound/types'
import type { OrderStatus, SalesChannel } from '@/modules/orders/types'

export interface KpiValue {
  value: number
  previous?: number
}

export type AttentionTone = 'info' | 'warn' | 'danger' | 'success'

export interface AttentionItem {
  key: string
  title: string
  description: string
  count: number
  tone: AttentionTone
  /** Имя маршрута без префикса кабинета, например `orders` */
  section: string
  query?: Record<string, string>
}

export interface TrendPoint {
  date: string
  created: number
  shipped: number
}

export type DashboardPeriod = '7d' | '30d' | '90d'

export interface FcDashboard {
  kpis: {
    ordersToday: KpiValue
    shippedToday: KpiValue
    inProgress: KpiValue
    overdue: KpiValue
    inboundToday: KpiValue
    occupancy: KpiValue
  }
  pipeline: { status: OrderStatus; count: number }[]
  attention: AttentionItem[]
  trend: TrendPoint[]
  channels: { channel: SalesChannel; count: number }[]
  warehouses: { id: string; name: string; occupancy: number; inProgress: number }[]
  topClients: { id: string; name: string; orders: number; units: number }[]
}

export interface SellerDashboard {
  kpis: {
    stockUnits: KpiValue
    stockValue: KpiValue
    orders30d: KpiValue
    onTimeRate: KpiValue
    chargesMonth: KpiValue
  }
  attention: AttentionItem[]
  trend: { date: string; wb: number; ozon: number; own: number }[]
  fulfillments: { id: string; name: string; city: string; units: number; skus: number; warehouses: number }[]
  lowStock: { productId: string; sku: string; name: string; available: number; dailySales: number; daysLeft: number }[]
  recentInbound: InboundListItem[]
}
