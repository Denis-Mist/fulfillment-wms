import type { PageQuery } from '@/shared/api/types'

export type FboMarketplace = 'wb' | 'ozon'
export type FboStatus = 'draft' | 'confirmed' | 'assembling' | 'ready' | 'shipped' | 'cancelled'

export interface FboLine {
  productId: string
  sku: string
  name: string
  quantity: number
  packed: number
  available?: number
}

export interface FboListItem {
  id: string
  number: string
  marketplace: FboMarketplace
  destination: string
  status: FboStatus
  sellerId: string
  sellerName: string
  warehouseId: string
  warehouseName: string
  plannedAt: string
  createdAt: string
  units: number
  packed: number
  boxes: number
  skuCount: number
}

export interface FboDetails extends FboListItem {
  lines: FboLine[]
  supplyId?: string
  history: { at: string; status: FboStatus; by: string }[]
}

export interface FboQuery extends PageQuery {
  status?: FboStatus
  marketplace?: FboMarketplace
  sellerId?: string
  warehouseId?: string
}

export interface FboSummary {
  total: number
  byStatus: Partial<Record<FboStatus, number>>
  unitsInWork: number
  dueToday: number
}

export interface FboInput {
  marketplace: FboMarketplace
  destination: string
  warehouseId: string
  plannedAt: string
  lines: { productId: string; quantity: number }[]
}

export interface FboStockOption {
  productId: string
  sku: string
  name: string
  available: number
  warehouseId: string
  warehouseName: string
}
