import type { PageQuery } from '@/shared/api/types'

export type InboundStatus = 'draft' | 'in_transit' | 'arrived' | 'receiving' | 'received' | 'placed' | 'cancelled'

export type PackageType = 'boxes' | 'pallets'

export interface InboundListItem {
  id: string
  number: string
  status: InboundStatus
  sellerId: string
  sellerName: string
  fulfillmentName: string
  warehouseId: string
  warehouseName: string
  createdAt: string
  expectedAt: string
  packageType: PackageType
  packagesCount: number
  linesCount: number
  expectedUnits: number
  receivedUnits: number
  defectUnits: number
  hasDiscrepancy: boolean
}

export interface InboundLine {
  id: string
  productId: string
  sku: string
  name: string
  barcode: string
  expectedQty: number
  receivedQty: number
  defectQty: number
  tracksExpiry: boolean
  lotNumber?: string
  expiresAt?: string
}

export type InboundEventType = InboundStatus | 'discrepancy' | 'comment'

export interface InboundEvent {
  id: string
  at: string
  type: InboundEventType
  title: string
  description?: string
  actorName: string
}

export interface InboundShipment extends InboundListItem {
  lines: InboundLine[]
  events: InboundEvent[]
  comment?: string
  driverName?: string
  vehicleNumber?: string
}

export type InboundAction = 'arrive' | 'start_receiving' | 'complete_receiving' | 'complete_placement' | 'cancel'

export interface InboundListQuery extends PageQuery {
  status?: InboundStatus
  sellerId?: string
  warehouseId?: string
}

export interface InboundSummary {
  total: number
  byStatus: Partial<Record<InboundStatus, number>>
}
