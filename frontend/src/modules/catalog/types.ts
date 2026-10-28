import type { PageQuery } from '@/shared/api/types'

export interface ProductListItem {
  id: string
  sku: string
  name: string
  category: string
  barcode: string
  wbBarcode?: string
  sellerId: string
  sellerName: string
  price: number
  weightG: number
  volumeLiters: number
  tracksExpiry: boolean
  requiresMarking: boolean
  stockQty: number
  archived: boolean
}

export interface ProductDetails extends ProductListItem {
  stock: {
    warehouseId: string
    warehouseName: string
    quantity: number
    reserved: number
    locations: number
  }[]
  sold30d: number
}

export interface ProductQuery extends PageQuery {
  sellerId?: string
  category?: string
  flag?: 'marking' | 'expiry' | 'no_stock' | 'archived'
}

export interface ProductInput {
  sku: string
  name: string
  category: string
  barcode: string
  wbBarcode?: string
  price: number
  weightG: number
  volumeLiters: number
  tracksExpiry: boolean
  requiresMarking: boolean
}

export interface ImportResult {
  created: number
  updated: number
  errors: { row: number; message: string }[]
}

export interface ProductFacets {
  total: number
  categories: { value: string; count: number }[]
  marking: number
  expiry: number
  noStock: number
  archived: number
}

/* ---------- Клиенты ФЦ ---------- */

export interface ClientListItem {
  id: string
  name: string
  inn: string
  city: string
  contractId: string
  contractNumber: string
  startedAt: string
  warehouses: { id: string; name: string }[]
  skuCount: number
  stockUnits: number
  orders30d: number
  marketplaces: string[]
}

export interface ClientDetails extends ClientListItem {
  ownerName: string
  ownerEmail: string
  ownerPhone: string
  tariff: { storagePerLiterDay: number; pickPerOrder: number; receivingPerUnit: number }
  ordersTrend: { date: string; count: number }[]
  inboundActive: number
}

export type ClientRequestStatus = 'pending' | 'accepted' | 'declined'

export interface ClientRequest {
  id: string
  companyName: string
  inn: string
  city: string
  contactName: string
  contactPhone: string
  skuEstimate: number
  ordersPerDay: number
  marketplaces: string[]
  message: string
  createdAt: string
  status: ClientRequestStatus
  decidedAt?: string
}
