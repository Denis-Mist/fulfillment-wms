import type { Marketplace, OrganizationStatus, OrganizationType, Role } from '@/modules/auth/types'
import type { InboundEvent, InboundStatus, PackageType } from '@/modules/inbound/types'
import type { OrderStatus, SalesChannel } from '@/modules/orders/types'

export interface OrgRecord {
  id: string
  type: OrganizationType
  name: string
  inn: string
  city: string
  status: OrganizationStatus
  createdAt: string
  marketplaces?: Marketplace[]
}

export interface UserRecord {
  id: string
  email: string
  password: string
  fullName: string
  phone: string
  isPlatformAdmin: boolean
  memberships: { organizationId: string; role: Role }[]
}

export interface ContractRecord {
  id: string
  fulfillmentId: string
  sellerId: string
  warehouseIds: string[]
  startedAt: string
}

export interface WarehouseRecord {
  id: string
  organizationId: string
  name: string
  code: string
  city: string
  address: string
  areaSqm: number
  isActive: boolean
}

export type ZoneType = 'receiving' | 'storage' | 'picking' | 'packing' | 'shipping' | 'defect'

export interface ZoneRecord {
  id: string
  warehouseId: string
  code: string
  name: string
  type: ZoneType
}

export interface LocationRecord {
  id: string
  warehouseId: string
  zoneId: string
  code: string
  /** Координаты в метрах от точки входа — для расчёта маршрутов */
  x: number
  y: number
  level: number
  capacityLiters: number
}

export interface ProductRecord {
  id: string
  sellerId: string
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

export interface StockRecord {
  id: string
  productId: string
  sellerId: string
  warehouseId: string
  locationId: string
  quantity: number
  reserved: number
}

export interface OrderRecord {
  id: string
  number: string
  externalNumber?: string
  channel: SalesChannel
  status: OrderStatus
  sellerId: string
  warehouseId: string
  createdAt: string
  shipBy: string
  shippedAt?: string
  deliveryCity: string
  lines: { productId: string; quantity: number; price: number }[]
}

export interface InboundRecord {
  id: string
  number: string
  status: InboundStatus
  sellerId: string
  warehouseId: string
  createdAt: string
  expectedAt: string
  packageType: PackageType
  packagesCount: number
  comment?: string
  driverName?: string
  vehicleNumber?: string
  lines: {
    id: string
    productId: string
    expectedQty: number
    receivedQty: number
    defectQty: number
    lotNumber?: string
    expiresAt?: string
  }[]
  events: InboundEvent[]
}

export interface NotificationReadRecord {
  id: string
}

export interface Collections {
  organizations: OrgRecord[]
  users: UserRecord[]
  contracts: ContractRecord[]
  warehouses: WarehouseRecord[]
  zones: ZoneRecord[]
  locations: LocationRecord[]
  products: ProductRecord[]
  stock: StockRecord[]
  orders: OrderRecord[]
  inbound: InboundRecord[]
  notificationReads: NotificationReadRecord[]
}

export type CollectionName = keyof Collections
export type RecordOf<K extends CollectionName> = Collections[K][number]
