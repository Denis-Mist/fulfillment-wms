import type { PageQuery } from '@/shared/api/types'

export type StockStatus = 'available' | 'reserved' | 'defect'

export interface StockItem {
  id: string
  productId: string
  sku: string
  productName: string
  barcode: string
  sellerId: string
  sellerName: string
  warehouseId: string
  warehouseName: string
  locationId: string
  locationCode: string
  zoneCode: string
  quantity: number
  reserved: number
  available: number
  status: StockStatus
  volumeLiters: number
}

export interface StockQuery extends PageQuery {
  warehouseId?: string
  sellerId?: string
  zone?: string
  status?: StockStatus
}

export interface StockSummary {
  skuCount: number
  quantity: number
  reserved: number
  available: number
  defect: number
  emptyLocations: number
}

/** Остаток товара селлера в разрезе складов */
export interface ProductStock {
  id: string
  productId: string
  sku: string
  name: string
  available: number
  reserved: number
  defect: number
  avgDailySales: number
  /** На сколько дней хватит доступного остатка; null — продаж нет */
  daysOfCover: number | null
  warehouses: {
    warehouseId: string
    warehouseName: string
    fulfillmentName: string
    available: number
    reserved: number
  }[]
}

export type OperationType = 'move' | 'inventory' | 'writeoff'
export type ErrorReason = 'defect' | 'shortage' | 'missort' | 'expired'

export interface StockOperation {
  id: string
  number: string
  type: OperationType
  at: string
  actorName: string
  warehouseId: string
  warehouseName: string
  productId: string
  sku: string
  productName: string
  sellerName: string
  fromLocationCode?: string
  toLocationCode?: string
  /** Изменение количества: + оприходование, − списание, перемещение — перемещённое количество */
  quantity: number
  expected?: number
  counted?: number
  reason?: ErrorReason
  comment?: string
}

export interface OperationQuery extends PageQuery {
  type?: OperationType
  warehouseId?: string
  stockId?: string
}

export interface OperationSummary {
  byType: Record<OperationType, number>
  errors: { reason: ErrorReason; count: number; quantity: number }[]
  last30Days: number
}

export interface MoveInput {
  stockId: string
  toLocationId: string
  quantity: number
  comment?: string
}

export interface InventoryInput {
  stockId: string
  counted: number
  comment?: string
}

export interface WriteoffInput {
  stockId: string
  quantity: number
  reason: ErrorReason
  comment?: string
}

export interface StockLocationOption {
  id: string
  code: string
  zoneCode: string
  zoneType: string
}
