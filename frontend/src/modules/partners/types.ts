import type { ClientRequestStatus } from '@/modules/catalog/types'

export type FcService = 'storage' | 'fbs' | 'fbo' | 'marking' | 'returns' | 'photo' | 'kits'

export interface FcWarehouseInfo {
  id: string
  name: string
  city: string
  address: string
  areaSqm: number
}

export interface FcStartRates {
  receivingPerUnit: number
  storagePerM3Day: number
  pickingPerOrder: number
  packingPerUnit: number
}

export interface FulfillmentCard {
  id: string
  name: string
  city: string
  cities: string[]
  rating: number
  clientsCount: number
  services: FcService[]
  marketplaces: string[]
  warehouses: FcWarehouseInfo[]
  rates: FcStartRates
  slaHours: number
  connection: 'connected' | ClientRequestStatus | 'none'
  contract?: { id: string; startedAt: string; warehouseIds: string[] }
  requestedAt?: string
}

export interface ConnectRequestInput {
  skuEstimate: number
  ordersPerDay: number
  marketplaces: string[]
  message: string
}
