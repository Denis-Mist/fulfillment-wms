import type { PageQuery } from '@/shared/api/types'

export interface Warehouse {
  id: string
  organizationId: string
  name: string
  code: string
  city: string
  address: string
  areaSqm: number
  locationsCount: number
  occupiedLocations: number
  isActive: boolean
}

export type ZoneType = 'receiving' | 'storage' | 'picking' | 'packing' | 'shipping' | 'defect'

export interface Zone {
  id: string
  warehouseId: string
  code: string
  name: string
  type: ZoneType
  locationsCount: number
  capacityLiters: number
  usedLiters: number
}

export interface StorageLocation {
  id: string
  warehouseId: string
  zoneId: string
  zoneCode: string
  code: string
  aisle: string
  rack: number
  x: number
  y: number
  level: number
  capacityLiters: number
  usedLiters: number
  skuCount: number
  quantity: number
  sellerNames: string[]
}

export interface LocationQuery extends PageQuery {
  zoneId?: string
  occupancy?: 'empty' | 'partial' | 'full'
}

export interface LocationInput {
  zoneId: string
  aisle: string
  rack: number
  level: number
  capacityLiters: number
}

export interface ZoneInput {
  code: string
  name: string
  type: ZoneType
}

export interface PutawayCheckInput {
  locationId: string
  productId: string
  quantity: number
}

export interface PutawayCheckResult {
  allowed: boolean
  /** Пояснения по каждому ограничению (FR-06) */
  checks: { rule: string; passed: boolean; detail: string }[]
  freeLitersAfter: number
}

export interface WarehouseMapCell {
  id: string
  code: string
  aisle: string
  rack: number
  fill: number
}

export interface WarehouseProductOption {
  id: string
  sku: string
  name: string
  volumeLiters: number
  weightG: number
  sellerName: string
}
