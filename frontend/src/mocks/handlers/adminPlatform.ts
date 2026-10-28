import { http, HttpResponse } from 'msw'
import type {
  AuditCategory,
  AuditEvent,
  OrganizationDetails,
  PlatformOverview,
  PlatformUser,
  ReferenceInput,
  ReferenceItem,
  ReferenceKind,
} from '@/modules/admin/types'
import type { Role } from '@/modules/auth/types'
import type { SalesChannel } from '@/modules/orders/types'
import { DAY } from '../db/seed'
import { db } from '../db/store'
import type { UserRecord } from '../db/types'
import { localCollection } from '../lib/collection'
import { listParams, paginate, param, problem, sortItems, withAuth, type MockContext } from '../lib/http'
import { api } from './api'

const adminOnly = { roles: ['platform_admin' as const] }

const userFlags = localCollection<{ id: string; blocked: boolean }>('admin-user-flags', () => [])
const auditLog = localCollection<AuditEvent>('admin-audit', () => [])

const REFERENCE_SEED: [ReferenceKind, string, string, string?][] = [
  ['units', 'PCS', 'Штука', 'шт'],
  ['units', 'SET', 'Комплект', 'компл'],
  ['units', 'BOX', 'Короб', 'кор'],
  ['units', 'PLT', 'Паллета', 'пал'],
  ['units', 'KG', 'Килограмм', 'кг'],
  ['cell_types', 'SHELF_S', 'Полка малая', 'до 300 л, до 25 кг'],
  ['cell_types', 'SHELF_L', 'Полка большая', 'до 600 л, до 60 кг'],
  ['cell_types', 'PALLET', 'Паллетное место', '1200×800, до 800 кг'],
  ['cell_types', 'MEZ', 'Мезонин', 'мелкоштучный товар'],
  ['cell_types', 'TOTE', 'Пластиковый контейнер', '600×400×300'],
  ['carriers', 'WB', 'Wildberries', 'FBS, FBO'],
  ['carriers', 'OZON', 'Ozon', 'FBS, FBO'],
  ['carriers', 'YM', 'Яндекс Маркет', 'FBS'],
  ['carriers', 'CDEK', 'СДЭК', 'курьерская доставка'],
  ['carriers', 'PEK', 'ПЭК', 'межгород, паллеты'],
  ['services', 'RCV_UNIT', 'Приёмка поштучная', 'за единицу'],
  ['services', 'STORE_L', 'Хранение', 'за литр в сутки'],
  ['services', 'PICK', 'Сборка заказа', 'за заказ + строку'],
  ['services', 'PACK', 'Упаковка', 'по типу тары'],
  ['services', 'MARK', 'Маркировка', 'за этикетку'],
  ['services', 'FBO', 'Подготовка поставки FBO', 'за короб'],
]
const references = localCollection<ReferenceItem>('admin-references', () =>
  REFERENCE_SEED.map(([kind, code, name, note]) => ({ id: `ref_${kind}_${code}`, kind, code, name, note, isActive: true })),
)

function log(ctx: MockContext, category: AuditCategory, action: string, details?: string, entity?: string) {
  auditLog.upsert({
    id: `aud_${Date.now()}_${Math.random().toString(36).slice(2, 7)}`,
    at: new Date().toISOString(),
    category,
    action,
    details,
    entity,
    actorName: ctx.user.fullName,
  })
}

const orgName = (id?: string) => db.data.organizations.find((o) => o.id === id)?.name
const warehouseOwner = (warehouseId: string) => db.data.warehouses.find((w) => w.id === warehouseId)?.organizationId

/** Журнал: события из данных платформы + действия администратора. Записи только добавляются (NFR-03). */
function auditEvents(): AuditEvent[] {
  const now = Date.now()
  const events: AuditEvent[] = []
  for (const org of db.data.organizations) {
    events.push({
      id: `aud_org_${org.id}`,
      at: org.createdAt,
      category: 'organization',
      action: 'Регистрация организации',
      details: `ИНН ${org.inn}, ${org.city}`,
      actorName: 'Система',
      organizationId: org.id,
      organizationName: org.name,
      entity: org.name,
    })
  }
  for (const inbound of db.data.inbound) {
    const fcId = warehouseOwner(inbound.warehouseId)
    for (const event of inbound.events) {
      events.push({
        id: `aud_${event.id}`,
        at: event.at,
        category: 'inbound',
        action: event.title,
        details: event.description,
        actorName: event.actorName,
        organizationId: fcId,
        organizationName: orgName(fcId),
        entity: inbound.number,
      })
    }
  }
  for (const order of db.data.orders) {
    if (!order.shippedAt || now - new Date(order.shippedAt).getTime() > 7 * DAY) continue
    const fcId = warehouseOwner(order.warehouseId)
    events.push({
      id: `aud_ship_${order.id}`,
      at: order.shippedAt,
      category: 'order',
      action: 'Заказ отгружен',
      details: `${order.lines.length} строк, ${order.deliveryCity}`,
      actorName: 'Смена отгрузки',
      organizationId: fcId,
      organizationName: orgName(fcId),
      entity: order.number,
    })
  }
  return [...events, ...auditLog.all]
}

function toPlatformUser(user: UserRecord): PlatformUser {
  return {
    id: user.id,
    fullName: user.fullName,
    email: user.email,
    phone: user.phone,
    isPlatformAdmin: user.isPlatformAdmin,
    blocked: userFlags.find(user.id)?.blocked ?? false,
    memberships: user.memberships.map((m) => ({
      organizationId: m.organizationId,
      organizationName: orgName(m.organizationId) ?? '—',
      role: m.role,
    })),
  }
}

export const adminPlatformHandlers = [
  http.get(
    api('/admin/overview'),
    withAuth(() => {
      const now = Date.now()
      const since = now - 30 * DAY
      const prevSince = now - 60 * DAY
      const orgs = db.data.organizations
      const orders30 = db.data.orders.filter((o) => new Date(o.createdAt).getTime() >= since)
      const shipped = orders30.filter((o) => o.shippedAt)
      const onTime = shipped.filter((o) => o.shippedAt! <= o.shipBy).length

      const byDay = new Map<string, Record<SalesChannel, number>>()
      for (let i = 29; i >= 0; i--) {
        byDay.set(new Date(now - i * DAY).toISOString().slice(0, 10), { wb: 0, ozon: 0, own: 0 })
      }
      for (const order of orders30) {
        const bucket = byDay.get(order.createdAt.slice(0, 10))
        if (bucket) bucket[order.channel]++
      }

      const topFulfillments = orgs
        .filter((o) => o.type === 'fulfillment' && o.status === 'active')
        .map((fc) => {
          const warehouses = new Set(db.data.warehouses.filter((w) => w.organizationId === fc.id).map((w) => w.id))
          return {
            id: fc.id,
            name: fc.name,
            warehouses: warehouses.size,
            clients: db.data.contracts.filter((c) => c.fulfillmentId === fc.id).length,
            orders30d: orders30.filter((o) => warehouses.has(o.warehouseId)).length,
          }
        })
        .sort((a, b) => b.orders30d - a.orders30d)
        .slice(0, 5)

      const overview: PlatformOverview = {
        organizations: {
          total: orgs.length,
          fulfillment: orgs.filter((o) => o.type === 'fulfillment').length,
          seller: orgs.filter((o) => o.type === 'seller').length,
          pending: orgs.filter((o) => o.status === 'pending').length,
          blocked: orgs.filter((o) => o.status === 'blocked').length,
        },
        users: { total: db.data.users.length, blocked: userFlags.all.filter((f) => f.blocked).length },
        warehouses: db.data.warehouses.length,
        orders30d: orders30.length,
        ordersPrev30d: db.data.orders.filter((o) => {
          const t = new Date(o.createdAt).getTime()
          return t >= prevSince && t < since
        }).length,
        shipped30d: shipped.length,
        inbound30d: db.data.inbound.filter((i) => new Date(i.createdAt).getTime() >= since).length,
        onTimeRate: shipped.length ? onTime / shipped.length : 1,
        ordersByDay: [...byDay.entries()].map(([date, channels]) => ({ date, channels })),
        topFulfillments,
        recentEvents: sortItems(auditEvents(), 'at', 'desc').slice(0, 8),
      }
      return HttpResponse.json(overview)
    }, adminOnly),
  ),

  http.get(
    api('/admin/organizations/:id'),
    withAuth(({ params }) => {
      const org = db.data.organizations.find((o) => o.id === param(params, 'id'))
      if (!org) return problem(404, 'Организация не найдена')
      const since = Date.now() - 30 * DAY
      const warehouses = db.data.warehouses.filter((w) => w.organizationId === org.id)
      const contracts = db.data.contracts.filter((c) => (org.type === 'fulfillment' ? c.fulfillmentId : c.sellerId) === org.id)
      const whIds = new Set(warehouses.map((w) => w.id))
      const details: OrganizationDetails = {
        id: org.id,
        name: org.name,
        type: org.type,
        inn: org.inn,
        city: org.city,
        status: org.status,
        createdAt: org.createdAt,
        users: db.data.users
          .filter((u) => u.memberships.some((m) => m.organizationId === org.id))
          .map((u) => ({
            id: u.id,
            fullName: u.fullName,
            email: u.email,
            role: u.memberships.find((m) => m.organizationId === org.id)!.role,
          })),
        warehouses: warehouses.map((w) => ({ id: w.id, name: w.name, code: w.code, city: w.city })),
        partners: contracts.map((c) => {
          const partnerId = org.type === 'fulfillment' ? c.sellerId : c.fulfillmentId
          return { id: partnerId, name: orgName(partnerId) ?? '—', startedAt: c.startedAt, warehouses: c.warehouseIds.length }
        }),
        orders30d: db.data.orders.filter(
          (o) => new Date(o.createdAt).getTime() >= since && (org.type === 'seller' ? o.sellerId === org.id : whIds.has(o.warehouseId)),
        ).length,
        products: db.data.products.filter((p) => p.sellerId === org.id).length,
      }
      return HttpResponse.json(details)
    }, adminOnly),
  ),

  http.get(
    api('/admin/users'),
    withAuth(({ url }) => {
      const params = listParams(url, { sortField: 'fullName', sortOrder: 'asc' })
      const role = url.searchParams.get('role') as Role | null
      const state = url.searchParams.get('state')
      const items = db.data.users
        .map(toPlatformUser)
        .filter((u) => !role || (role === 'platform_admin' ? u.isPlatformAdmin : u.memberships.some((m) => m.role === role)))
        .filter((u) => !state || (state === 'blocked') === u.blocked)
        .filter(
          (u) =>
            !params.search ||
            u.fullName.toLowerCase().includes(params.search) ||
            u.email.toLowerCase().includes(params.search) ||
            u.memberships.some((m) => m.organizationName.toLowerCase().includes(params.search)),
        )
      return HttpResponse.json(paginate(sortItems(items, params.sortField, params.sortOrder), params.page, params.pageSize))
    }, adminOnly),
  ),

  http.post(
    api('/admin/users/:id/:action'),
    withAuth(({ params, ctx }) => {
      const user = db.data.users.find((u) => u.id === param(params, 'id'))
      if (!user) return problem(404, 'Пользователь не найден')
      const action = param(params, 'action')
      if (action === 'reset-password') {
        log(ctx, 'user', 'Сброс пароля', user.email, user.fullName)
        return HttpResponse.json({ temporaryPassword: `wms-${Math.random().toString(36).slice(2, 8)}` })
      }
      if (action !== 'block' && action !== 'unblock') return problem(400, 'Неизвестное действие')
      if (user.isPlatformAdmin) return problem(409, 'Нельзя заблокировать администратора платформы')
      userFlags.upsert({ id: user.id, blocked: action === 'block' })
      log(ctx, 'user', action === 'block' ? 'Пользователь заблокирован' : 'Пользователь разблокирован', user.email, user.fullName)
      return HttpResponse.json(toPlatformUser(user))
    }, adminOnly),
  ),

  http.get(
    api('/admin/audit'),
    withAuth(({ url }) => {
      const params = listParams(url, { sortField: 'at', sortOrder: 'desc' })
      const category = url.searchParams.get('category')
      const organizationId = url.searchParams.get('organizationId')
      const items = auditEvents().filter(
        (e) =>
          (!category || e.category === category) &&
          (!organizationId || e.organizationId === organizationId) &&
          (!params.search ||
            [e.action, e.details, e.actorName, e.entity].some((v) => v?.toLowerCase().includes(params.search))),
      )
      return HttpResponse.json(paginate(sortItems(items, 'at', params.sortOrder), params.page, params.pageSize))
    }, adminOnly),
  ),

  http.get(
    api('/admin/references'),
    withAuth(({ url }) => {
      const kind = url.searchParams.get('kind')
      return HttpResponse.json(references.all.filter((r) => !kind || r.kind === kind))
    }, adminOnly),
  ),

  http.post(
    api('/admin/references'),
    withAuth(async ({ request, ctx }) => {
      const input = (await request.json()) as ReferenceInput
      const code = input.code.trim().toUpperCase()
      if (!/^[A-Z0-9_]{2,12}$/.test(code)) return problem(400, 'Код: 2–12 латинских букв, цифр или «_»')
      if (!input.name.trim()) return problem(400, 'Укажите название')
      if (references.all.some((r) => r.kind === input.kind && r.code === code)) return problem(409, `Код ${code} уже есть в справочнике`)
      const item = references.upsert({
        id: `ref_${input.kind}_${code}`,
        kind: input.kind,
        code,
        name: input.name.trim(),
        note: input.note?.trim() || undefined,
        isActive: true,
      })
      log(ctx, 'reference', 'Добавлена запись справочника', `${code} · ${item.name}`)
      return HttpResponse.json(item, { status: 201 })
    }, adminOnly),
  ),

  http.post(
    api('/admin/references/:id/toggle'),
    withAuth(({ params, ctx }) => {
      const item = references.find(param(params, 'id'))
      if (!item) return problem(404, 'Запись не найдена')
      const updated = references.upsert({ ...item, isActive: !item.isActive })
      log(ctx, 'reference', updated.isActive ? 'Запись справочника включена' : 'Запись справочника архивирована', `${item.code} · ${item.name}`)
      return HttpResponse.json(updated)
    }, adminOnly),
  ),
]
