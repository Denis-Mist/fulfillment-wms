import type { CurrentUser } from '@/modules/auth/types'
import type { InboundListItem, InboundShipment } from '@/modules/inbound/types'
import type { Order, OrderListItem } from '@/modules/orders/types'
import type { Warehouse } from '@/modules/warehouses/types'
import { db } from '../db/store'
import { isOrderActive } from '../db/seed'
import type { InboundRecord, OrderRecord, OrgRecord, ProductRecord, UserRecord, WarehouseRecord } from '../db/types'

/** Индексы для быстрых join'ов внутри одного запроса */
export function lookups() {
  return {
    orgs: new Map<string, OrgRecord>(db.data.organizations.map((o) => [o.id, o])),
    warehouses: new Map<string, WarehouseRecord>(db.data.warehouses.map((w) => [w.id, w])),
    products: new Map<string, ProductRecord>(db.data.products.map((p) => [p.id, p])),
  }
}

export type Lookups = ReturnType<typeof lookups>

export function toCurrentUser(user: UserRecord): CurrentUser {
  return {
    id: user.id,
    email: user.email,
    fullName: user.fullName,
    phone: user.phone,
    isPlatformAdmin: user.isPlatformAdmin,
    memberships: user.memberships.flatMap((membership) => {
      const org = db.data.organizations.find((o) => o.id === membership.organizationId)
      if (!org) return []
      return [
        {
          organizationId: org.id,
          organizationName: org.name,
          organizationType: org.type,
          organizationStatus: org.status,
          role: membership.role,
        },
      ]
    }),
  }
}

export function toOrderListItem(order: OrderRecord, l: Lookups, now = Date.now()): OrderListItem {
  const units = order.lines.reduce((sum, line) => sum + line.quantity, 0)
  return {
    id: order.id,
    number: order.number,
    externalNumber: order.externalNumber,
    channel: order.channel,
    status: order.status,
    sellerId: order.sellerId,
    sellerName: l.orgs.get(order.sellerId)?.name ?? '—',
    warehouseId: order.warehouseId,
    warehouseName: l.warehouses.get(order.warehouseId)?.name ?? '—',
    createdAt: order.createdAt,
    shipBy: order.shipBy,
    shippedAt: order.shippedAt,
    linesCount: order.lines.length,
    unitsCount: units,
    amount: order.lines.reduce((sum, line) => sum + line.quantity * line.price, 0),
    deliveryCity: order.deliveryCity,
    isOverdue: isOrderActive(order.status) && Date.parse(order.shipBy) < now,
  }
}

export function toOrder(order: OrderRecord, l: Lookups): Order {
  return {
    ...toOrderListItem(order, l),
    lines: order.lines.map((line) => {
      const product = l.products.get(line.productId)
      return {
        productId: line.productId,
        sku: product?.sku ?? '—',
        name: product?.name ?? 'Товар удалён',
        barcode: product?.barcode ?? '',
        quantity: line.quantity,
        price: line.price,
      }
    }),
  }
}

export function inboundHasDiscrepancy(record: InboundRecord) {
  if (record.status !== 'received' && record.status !== 'placed') return false
  return record.lines.some((line) => line.receivedQty !== line.expectedQty || line.defectQty > 0)
}

export function toInboundListItem(record: InboundRecord, l: Lookups): InboundListItem {
  const warehouse = l.warehouses.get(record.warehouseId)
  return {
    id: record.id,
    number: record.number,
    status: record.status,
    sellerId: record.sellerId,
    sellerName: l.orgs.get(record.sellerId)?.name ?? '—',
    fulfillmentName: (warehouse && l.orgs.get(warehouse.organizationId)?.name) ?? '—',
    warehouseId: record.warehouseId,
    warehouseName: warehouse?.name ?? '—',
    createdAt: record.createdAt,
    expectedAt: record.expectedAt,
    packageType: record.packageType,
    packagesCount: record.packagesCount,
    linesCount: record.lines.length,
    expectedUnits: record.lines.reduce((sum, line) => sum + line.expectedQty, 0),
    receivedUnits: record.lines.reduce((sum, line) => sum + line.receivedQty, 0),
    defectUnits: record.lines.reduce((sum, line) => sum + line.defectQty, 0),
    hasDiscrepancy: inboundHasDiscrepancy(record),
  }
}

export function toInbound(record: InboundRecord, l: Lookups): InboundShipment {
  return {
    ...toInboundListItem(record, l),
    comment: record.comment,
    driverName: record.driverName,
    vehicleNumber: record.vehicleNumber,
    events: record.events,
    lines: record.lines.map((line) => {
      const product = l.products.get(line.productId)
      return {
        id: line.id,
        productId: line.productId,
        sku: product?.sku ?? '—',
        name: product?.name ?? 'Товар удалён',
        barcode: product?.barcode ?? '',
        expectedQty: line.expectedQty,
        receivedQty: line.receivedQty,
        defectQty: line.defectQty,
        tracksExpiry: product?.tracksExpiry ?? false,
        lotNumber: line.lotNumber,
        expiresAt: line.expiresAt,
      }
    }),
  }
}

export function warehouseOccupancy(warehouseId: string) {
  const total = db.data.locations.filter((l) => l.warehouseId === warehouseId).length
  const occupied = new Set(
    db.data.stock.filter((s) => s.warehouseId === warehouseId && s.quantity > 0).map((s) => s.locationId),
  ).size
  return { total, occupied }
}

export function toWarehouse(record: WarehouseRecord): Warehouse {
  const { total, occupied } = warehouseOccupancy(record.id)
  return { ...record, locationsCount: total, occupiedLocations: occupied }
}
