import type { PageQuery } from '@/shared/api/types'
import type { SalesChannel } from '@/modules/orders/types'

export type ReturnStatus = 'expected' | 'received' | 'inspected' | 'closed'
export type ReturnReason = 'not_bought' | 'defect' | 'size' | 'damaged' | 'wrong_item'
export type ReturnGrade = 'as_new' | 'resale' | 'defect' | 'dispose'

export interface ReturnLine {
  productId: string
  sku: string
  name: string
  quantity: number
  grade?: ReturnGrade
}

export interface ReturnListItem {
  id: string
  number: string
  orderNumber: string
  channel: SalesChannel
  reason: ReturnReason
  status: ReturnStatus
  sellerId: string
  sellerName: string
  warehouseId: string
  warehouseName: string
  units: number
  createdAt: string
  receivedAt?: string
  inspectedAt?: string
  resolution?: 'restocked' | 'defect' | 'mixed'
}

export interface ReturnDetails extends ReturnListItem {
  lines: ReturnLine[]
  comment?: string
  inspectedBy?: string
  photos: number
}

export interface ReturnQuery extends PageQuery {
  status?: ReturnStatus
  reason?: ReturnReason
  sellerId?: string
  warehouseId?: string
}

export interface ReturnSummary {
  total: number
  byStatus: Partial<Record<ReturnStatus, number>>
  restockRate: number
  defectUnits: number
}

export interface InspectInput {
  grades: Record<string, ReturnGrade>
  comment?: string
}
