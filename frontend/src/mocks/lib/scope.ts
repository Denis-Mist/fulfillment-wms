import { db } from '../db/store'
import type { InboundRecord, OrderRecord } from '../db/types'
import type { MockContext } from './http'

/** Склады, доступные текущей организации; `null` — все (администратор платформы) */
export function visibleWarehouseIds(ctx: MockContext): Set<string> | null {
  if (!ctx.org) return null
  if (ctx.org.type === 'fulfillment') {
    return new Set(db.data.warehouses.filter((w) => w.organizationId === ctx.org!.id).map((w) => w.id))
  }
  return new Set(db.data.contracts.filter((c) => c.sellerId === ctx.org!.id).flatMap((c) => c.warehouseIds))
}

export function canSee(ctx: MockContext, record: { sellerId: string; warehouseId: string }): boolean {
  if (!ctx.org) return true
  if (ctx.org.type === 'seller') return record.sellerId === ctx.org.id
  return visibleWarehouseIds(ctx)!.has(record.warehouseId)
}

function scoped<T extends { sellerId: string; warehouseId: string }>(
  ctx: MockContext,
  items: T[],
  warehouseId?: string | null,
): T[] {
  const warehouses = visibleWarehouseIds(ctx)
  return items.filter((item) => {
    if (warehouseId && item.warehouseId !== warehouseId) return false
    if (!ctx.org) return true
    if (ctx.org.type === 'seller') return item.sellerId === ctx.org.id
    return warehouses!.has(item.warehouseId)
  })
}

export const scopedOrders = (ctx: MockContext, warehouseId?: string | null): OrderRecord[] =>
  scoped(ctx, db.data.orders, warehouseId)

export const scopedInbound = (ctx: MockContext, warehouseId?: string | null): InboundRecord[] =>
  scoped(ctx, db.data.inbound, warehouseId)
