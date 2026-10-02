import type { Role } from '@/modules/auth/types'
import type { InboundEvent, InboundStatus } from '@/modules/inbound/types'
import type { OrderStatus, SalesChannel } from '@/modules/orders/types'
import { createRandom, type Random } from '../lib/random'
import {
  DELIVERY_CITIES,
  FEMALE_NAMES,
  FULFILLMENTS,
  MALE_NAMES,
  PENDING_ORGS,
  SELLERS,
  SURNAMES,
  VEHICLE_LETTERS,
  type SellerSeed,
} from './dictionaries'
import type { Collections, ContractRecord, InboundRecord, OrderRecord, ProductRecord, ZoneType } from './types'

export const HOUR = 3_600_000
export const DAY = 24 * HOUR
const HISTORY_DAYS = 120
export const DEMO_PASSWORD = 'demo'

const iso = (ms: number) => new Date(ms).toISOString()
const pad = (value: number, length: number) => String(value).padStart(length, '0')

function startOfDay(ms: number) {
  const date = new Date(ms)
  date.setHours(0, 0, 0, 0)
  return date.getTime()
}

function personName(rng: Random, female = rng.bool()) {
  const first = rng.pick(female ? FEMALE_NAMES : MALE_NAMES)
  const last = rng.pick(SURNAMES) + (female ? 'а' : '')
  return `${first} ${last}`
}

function phone(rng: Random) {
  return `+7 9${rng.digits(2)} ${rng.digits(3)}-${rng.digits(2)}-${rng.digits(2)}`
}

function vehicleNumber(rng: Random) {
  const l = () => rng.pick(VEHICLE_LETTERS)
  return `${l()}${rng.digits(3)}${l()}${l()} ${rng.pick(['77', '177', '777', '50', '750', '78', '178', '16', '23'])}`
}

const ZONES: [string, string, ZoneType][] = [
  ['RCV', 'Зона приёмки', 'receiving'],
  ['STR', 'Зона хранения', 'storage'],
  ['PCK', 'Зона отбора', 'picking'],
  ['PAK', 'Зона упаковки', 'packing'],
  ['SHP', 'Зона отгрузки', 'shipping'],
  ['DEF', 'Брак и карантин', 'defect'],
]
const AISLE_LETTERS = 'ABCDEFGHJKLMN'
const LEVELS = 4

function buildTopology(c: Collections, warehouseId: string, aisles: number, racks: number) {
  for (const [code, name, type] of ZONES) {
    c.zones.push({ id: `${warehouseId}_z_${code}`, warehouseId, code, name, type })
  }
  for (let a = 0; a < aisles; a++) {
    for (let r = 0; r < racks; r++) {
      for (let level = 1; level <= LEVELS; level++) {
        const code = `${AISLE_LETTERS[a]}-${pad(r + 1, 2)}-${level}`
        c.locations.push({
          id: `${warehouseId}_l_${code}`,
          warehouseId,
          // Нижний ярус — ячейки отбора, верхние — хранение
          zoneId: `${warehouseId}_z_${level === 1 ? 'PCK' : 'STR'}`,
          code,
          x: Math.round((4 + a * 3.2) * 10) / 10,
          y: Math.round((3 + r * 1.4) * 10) / 10,
          level,
          capacityLiters: level === 1 ? 300 : 600,
        })
      }
    }
  }
}

function buildProducts(rng: Random, seller: SellerSeed, sellerId: string): ProductRecord[] {
  const combos: string[] = []
  for (const base of seller.bases) {
    for (const variant of seller.variants) {
      if (seller.colors) for (const color of seller.colors) combos.push(`${base} ${variant}, ${color}`)
      else combos.push(`${base} ${variant}`)
    }
  }
  const names = rng.sample(combos, rng.int(28, 42))
  return names.map((name, index) => {
    const weightG = rng.int(seller.weightRange[0], seller.weightRange[1])
    return {
      id: `prd_${seller.prefix.toLowerCase()}_${index + 1}`,
      sellerId,
      sku: `${seller.prefix}-${10001 + index}`,
      name,
      category: seller.category,
      barcode: `46${rng.digits(11)}`,
      wbBarcode: `20${rng.digits(11)}`,
      price: Math.floor(rng.int(seller.priceRange[0], seller.priceRange[1]) / 100) * 100 + 90,
      weightG,
      volumeLiters: Math.round((weightG / 1000) * rng.float(1.5, 4) * 10) / 10,
      tracksExpiry: seller.tracksExpiry,
      requiresMarking: seller.requiresMarking,
    }
  })
}

const ACTIVE_ORDER_STATUSES: OrderStatus[] = ['new', 'reserved', 'out_of_stock', 'picking', 'picked', 'packed']

function resolveOrderStatus(r: Random, createdAt: number, now: number): { status: OrderStatus; shippedAt?: string } {
  const ageH = (now - createdAt) / HOUR
  if (r.bool(0.025)) return { status: 'cancelled' }
  const shipped = (minH: number, maxH: number) => ({
    status: 'shipped' as const,
    shippedAt: iso(Math.min(createdAt + r.float(minH, maxH) * HOUR, now - 5 * 60_000)),
  })
  if (ageH > 40) return shipped(3, 38)
  if (ageH > 20) {
    const status = r.weighted<OrderStatus>([
      ['shipped', 0.75],
      ['packed', 0.1],
      ['picked', 0.07],
      ['picking', 0.05],
      ['out_of_stock', 0.03],
    ])
    return status === 'shipped' ? shipped(3, ageH) : { status }
  }
  if (ageH > 6) {
    const status = r.weighted<OrderStatus>([
      ['shipped', 0.35],
      ['packed', 0.2],
      ['picked', 0.12],
      ['picking', 0.15],
      ['reserved', 0.13],
      ['out_of_stock', 0.05],
    ])
    return status === 'shipped' ? shipped(2, ageH) : { status }
  }
  return {
    status: r.weighted<OrderStatus>([
      ['new', 0.3],
      ['reserved', 0.35],
      ['picking', 0.17],
      ['picked', 0.1],
      ['out_of_stock', 0.04],
      ['packed', 0.04],
    ]),
  }
}

export function isOrderActive(status: OrderStatus) {
  return ACTIVE_ORDER_STATUSES.includes(status)
}

const INBOUND_COMMENTS = [
  'Хрупкое, не штабелировать',
  'Требуется стикеровка ШК Wildberries',
  'Часть коробов без маркировки',
  'Прошу принять до конца дня',
]

interface InboundContext {
  seqNumber: number
  sellerId: string
  warehouseId: string
  products: ProductRecord[]
  tracksExpiry: boolean
  sellerActor: string
  receivers: string[]
  expectedAt: number
  now: number
}

function buildInbound(ctx: InboundContext): InboundRecord {
  const r = createRandom(0x1b0 ^ (ctx.seqNumber * 2654435761))
  const id = `inb_${ctx.seqNumber}`
  const { expectedAt, now } = ctx
  const createdAt = expectedAt - r.int(2, 6) * DAY - r.int(0, 8) * HOUR

  const lines: InboundRecord['lines'] = r.sample(ctx.products, r.int(3, 12)).map((product, index) => ({
    id: `${id}_ln${index + 1}`,
    productId: product.id,
    expectedQty: r.pick([20, 30, 40, 50, 60, 80, 100, 120, 150, 200]),
    receivedQty: 0,
    defectQty: 0,
    lotNumber: ctx.tracksExpiry ? `L${r.digits(6)}` : undefined,
    expiresAt: ctx.tracksExpiry ? iso(expectedAt + r.int(180, 720) * DAY) : undefined,
  }))
  const units = lines.reduce((sum, line) => sum + line.expectedQty, 0)
  const packageType = units > 900 ? 'pallets' : 'boxes'
  const packagesCount = packageType === 'pallets' ? Math.ceil(units / 400) : Math.ceil(units / 45)

  let status: InboundStatus
  const sinceExpected = now - expectedAt
  if (sinceExpected > 0 && r.bool(0.03)) status = 'cancelled'
  else if (expectedAt > now + 1.5 * DAY) status = r.bool(0.4) ? 'draft' : 'in_transit'
  else if (expectedAt > now) status = 'in_transit'
  else if (sinceExpected < 2 * HOUR) status = 'arrived'
  else if (sinceExpected < 6 * HOUR) status = 'receiving'
  else if (sinceExpected < 30 * HOUR) status = 'received'
  else status = 'placed'

  if (status === 'received' || status === 'placed') {
    for (const line of lines) {
      const shortage = r.bool(0.12) ? r.int(1, 5) : 0
      const surplus = !shortage && r.bool(0.03) ? r.int(1, 3) : 0
      line.receivedQty = line.expectedQty - shortage + surplus
      line.defectQty = r.bool(0.07) ? r.int(1, 3) : 0
    }
  } else if (status === 'receiving') {
    const done = r.int(1, lines.length - 1)
    lines.slice(0, done).forEach((line) => (line.receivedQty = line.expectedQty))
  }

  const receiver = r.pick(ctx.receivers)
  const events: InboundEvent[] = []
  const clamp = (ms: number) => Math.min(ms, now - 60_000)
  const push = (type: InboundEvent['type'], at: number, title: string, actorName: string, description?: string) =>
    events.push({ id: `${id}_e${events.length + 1}`, type, at: iso(clamp(at)), title, actorName, description })

  push('draft', createdAt, 'Поставка создана', ctx.sellerActor, `${lines.length} позиций, ${units} шт`)
  const order: InboundStatus[] = ['draft', 'in_transit', 'arrived', 'receiving', 'received', 'placed']
  const reached = status === 'cancelled' ? 1 : order.indexOf(status)
  const sentAt = Math.min(createdAt + r.int(6, 30) * HOUR, expectedAt - 2 * HOUR)
  const arrivedAt = expectedAt + r.int(-60, 60) * 60_000
  const receivingAt = arrivedAt + r.int(20, 80) * 60_000
  const receivedAt = receivingAt + r.int(1, 4) * HOUR
  const placedAt = receivedAt + r.int(1, 6) * HOUR
  const vehicle = vehicleNumber(r)

  if (reached >= 1) {
    push('in_transit', sentAt, 'Поставка отправлена', ctx.sellerActor, `${packagesCount} ${packageType === 'pallets' ? 'палл.' : 'кор.'}`)
  }
  if (reached >= 2) push('arrived', arrivedAt, 'Машина прибыла на склад', receiver, vehicle)
  if (reached >= 3) push('receiving', receivingAt, 'Начата приёмка', receiver)
  if (reached >= 4) {
    const received = lines.reduce((sum, line) => sum + line.receivedQty, 0)
    push('received', receivedAt, 'Приёмка завершена', receiver, `Принято ${received} из ${units} шт`)
    const shortage = lines.reduce((sum, line) => sum + Math.max(0, line.expectedQty - line.receivedQty), 0)
    const defect = lines.reduce((sum, line) => sum + line.defectQty, 0)
    if (shortage || defect) {
      const parts = [shortage && `недостача ${shortage} шт`, defect && `брак ${defect} шт`].filter(Boolean)
      push('discrepancy', receivedAt + 60_000, 'Обнаружены расхождения', receiver, parts.join(', '))
    }
  }
  if (reached >= 5) push('placed', placedAt, 'Товар размещён в ячейках', receiver, `Задействовано ${lines.length + r.int(0, 4)} ячеек`)
  if (status === 'cancelled') push('cancelled', sentAt + HOUR, 'Поставка отменена', ctx.sellerActor, 'Отменена клиентом')

  return {
    id,
    number: `ПС-${pad(ctx.seqNumber, 5)}`,
    status,
    sellerId: ctx.sellerId,
    warehouseId: ctx.warehouseId,
    createdAt: iso(createdAt),
    expectedAt: iso(expectedAt),
    packageType,
    packagesCount,
    comment: r.bool(0.2) ? r.pick(INBOUND_COMMENTS) : undefined,
    driverName: reached >= 1 ? personName(r, false) : undefined,
    vehicleNumber: reached >= 1 ? vehicle : undefined,
    lines,
    events,
  }
}

/** Генерирует полный набор демо-данных относительно текущего момента */
export function seed(now = Date.now()): Collections {
  const rng = createRandom(20261002)
  const today = startOfDay(now)
  const c: Collections = {
    organizations: [],
    users: [],
    contracts: [],
    warehouses: [],
    zones: [],
    locations: [],
    products: [],
    stock: [],
    orders: [],
    inbound: [],
    notificationReads: [],
  }

  c.users.push({
    id: 'usr_admin',
    email: 'admin@demo.ru',
    password: DEMO_PASSWORD,
    fullName: 'Алексей Воронцов',
    phone: '+7 900 000-00-01',
    isPlatformAdmin: true,
    memberships: [],
  })

  const fcIds: string[] = []
  const fcWarehouses: string[][] = []
  const fcReceivers = new Map<string, string[]>()

  FULFILLMENTS.forEach((fc, fcIndex) => {
    const id = `org_fc${fcIndex + 1}`
    fcIds.push(id)
    c.organizations.push({
      id,
      type: 'fulfillment',
      name: fc.name,
      inn: fc.inn,
      city: fc.city,
      status: 'active',
      createdAt: iso(today - (420 - fcIndex * 70) * DAY),
    })
    fcWarehouses.push(
      fc.warehouses.map((warehouse) => {
        const warehouseId = `wh_${warehouse.code.toLowerCase()}`
        c.warehouses.push({
          id: warehouseId,
          organizationId: id,
          name: warehouse.name,
          code: warehouse.code,
          city: warehouse.city,
          address: warehouse.address,
          areaSqm: warehouse.areaSqm,
          isActive: true,
        })
        buildTopology(c, warehouseId, warehouse.aisles, warehouse.racks)
        return warehouseId
      }),
    )

    const staff: { fullName: string; email: string; role: Role }[] =
      fcIndex === 0
        ? [
            { fullName: 'Ирина Соколова', email: 'director@demo.ru', role: 'fc_owner' },
            { fullName: 'Дмитрий Орлов', email: 'manager@demo.ru', role: 'fc_manager' },
            { fullName: 'Сергей Кузнецов', email: 'picker@demo.ru', role: 'fc_picker' },
          ]
        : [{ fullName: personName(rng), email: `owner@fc${fcIndex + 1}.demo.ru`, role: 'fc_owner' }]
    const workerRoles: Role[] = ['fc_receiver', 'fc_receiver', 'fc_picker', 'fc_picker', 'fc_picker', 'fc_packer', 'fc_packer', 'fc_supervisor']
    workerRoles.forEach((role, index) =>
      staff.push({ fullName: personName(rng), email: `staff${index + 1}@fc${fcIndex + 1}.demo.ru`, role }),
    )
    staff.forEach((member, index) =>
      c.users.push({
        id: `usr_fc${fcIndex + 1}_${index + 1}`,
        email: member.email,
        password: DEMO_PASSWORD,
        fullName: member.fullName,
        phone: phone(rng),
        isPlatformAdmin: false,
        memberships: [{ organizationId: id, role: member.role }],
      }),
    )
    fcReceivers.set(
      id,
      staff.filter((member) => member.role === 'fc_receiver' || member.role === 'fc_supervisor').map((m) => m.fullName),
    )
  })

  const sellerProducts = new Map<string, ProductRecord[]>()
  const sellerContracts = new Map<string, ContractRecord[]>()
  const sellerActors = new Map<string, string>()

  SELLERS.forEach((seller, sellerIndex) => {
    const id = `org_s${sellerIndex + 1}`
    c.organizations.push({
      id,
      type: 'seller',
      name: seller.name,
      inn: seller.inn,
      city: seller.city,
      status: 'active',
      createdAt: iso(today - (300 - sellerIndex * 20) * DAY),
      marketplaces: ['wb', 'ozon'],
    })
    const ownerName = sellerIndex === 0 ? 'Анна Смирнова' : personName(rng)
    sellerActors.set(id, ownerName)
    c.users.push({
      id: `usr_s${sellerIndex + 1}_1`,
      email: sellerIndex === 0 ? 'seller@demo.ru' : `owner@${seller.prefix.toLowerCase()}.demo.ru`,
      password: DEMO_PASSWORD,
      fullName: ownerName,
      phone: phone(rng),
      isPlatformAdmin: false,
      memberships: [{ organizationId: id, role: 'seller_owner' }],
    })
    c.users.push({
      id: `usr_s${sellerIndex + 1}_2`,
      email: `manager@${seller.prefix.toLowerCase()}.demo.ru`,
      password: DEMO_PASSWORD,
      fullName: personName(rng),
      phone: phone(rng),
      isPlatformAdmin: false,
      memberships: [{ organizationId: id, role: 'seller_manager' }],
    })

    const contracts = seller.contracts.map((contract) => ({
      id: `ctr_s${sellerIndex + 1}_fc${contract.fulfillment + 1}`,
      fulfillmentId: fcIds[contract.fulfillment]!,
      sellerId: id,
      warehouseIds: contract.warehouses.map((w) => fcWarehouses[contract.fulfillment]![w]!),
      startedAt: iso(today - rng.int(60, 280) * DAY),
    }))
    c.contracts.push(...contracts)
    sellerContracts.set(id, contracts)

    const products = buildProducts(rng, seller, id)
    c.products.push(...products)
    sellerProducts.set(id, products)

    for (const warehouseId of contracts.flatMap((contract) => contract.warehouseIds)) {
      const locations = c.locations.filter((location) => location.warehouseId === warehouseId)
      products.forEach((product, rank) => {
        if (!rng.bool(0.85)) return
        const popularity = 1 / Math.pow(rank + 1, 0.6)
        const total = rng.bool(0.08) ? rng.int(0, 8) : Math.round(rng.int(40, 420) * (0.4 + popularity))
        const parts = rng.bool(0.5) ? 2 : 1
        for (let part = 0; part < parts; part++) {
          const location = rng.pick(locations)
          c.stock.push({
            id: `stk_${product.id}_${warehouseId}_${part}`,
            productId: product.id,
            sellerId: id,
            warehouseId,
            locationId: location.id,
            quantity: Math.round(total / parts),
            reserved: 0,
          })
        }
      })
    }
  })

  PENDING_ORGS.forEach((org, index) => {
    const id = `org_p${index + 1}`
    c.organizations.push({
      id,
      type: org.type,
      name: org.name,
      inn: org.inn,
      city: org.city,
      status: 'pending',
      createdAt: iso(now - (index + 1) * 7 * HOUR),
      marketplaces: org.type === 'seller' ? ['wb'] : undefined,
    })
    c.users.push({
      id: `usr_p${index + 1}`,
      email: `owner${index + 1}@pending.demo.ru`,
      password: DEMO_PASSWORD,
      fullName: org.owner,
      phone: phone(rng),
      isPlatformAdmin: false,
      memberships: [{ organizationId: id, role: org.type === 'seller' ? 'seller_owner' : 'fc_owner' }],
    })
  })

  // Заказы: недельная сезонность, рост, промо-пики, популярность товаров по Ципфу
  const weekdayFactor = [0.75, 1.25, 1.15, 1.05, 1.0, 0.95, 0.8]
  let orderSeq = 100000
  for (let day = HISTORY_DAYS - 1; day >= 0; day--) {
    const dayStart = today - day * DAY
    const trend = 0.75 + 0.45 * (1 - day / HISTORY_DAYS)
    const promo = day % 29 === 6 ? 1.9 : day % 29 === 5 ? 1.4 : 1
    const weekday = weekdayFactor[new Date(dayStart).getDay()]!
    SELLERS.forEach((seller, sellerIndex) => {
      const sellerId = `org_s${sellerIndex + 1}`
      const products = sellerProducts.get(sellerId)!
      const weights = products.map((product, rank) => [product, 1 / Math.pow(rank + 1, 0.8)] as const)
      const contracts = sellerContracts.get(sellerId)!
      const count = Math.round(seller.dailyOrders * weekday * trend * promo * rng.float(0.75, 1.25))
      for (let k = 0; k < count; k++) {
        orderSeq++
        const createdAt = dayStart + ((rng.next() + rng.next()) / 2) * DAY
        const contract = rng.pick(contracts)
        const warehouseId = rng.pick(contract.warehouseIds)
        const channel = rng.weighted<SalesChannel>([
          ['wb', 0.5],
          ['ozon', 0.35],
          ['own', 0.15],
        ])
        const lineCount = rng.weighted([
          [1, 0.6],
          [2, 0.25],
          [3, 0.1],
          [4, 0.05],
        ] as const)
        const picked = new Set<ProductRecord>()
        for (let i = 0; i < lineCount; i++) picked.add(rng.weighted(weights))
        const lines = [...picked].map((product) => ({
          productId: product.id,
          quantity: rng.weighted([
            [1, 0.85],
            [2, 0.12],
            [3, 0.03],
          ] as const),
          price: product.price,
        }))
        const externalNumber =
          channel === 'wb' ? rng.digits(10) : channel === 'ozon' ? `${rng.digits(8)}-${rng.digits(4)}-1` : `SITE-${rng.digits(5)}`
        const deliveryCity = rng.pick(DELIVERY_CITIES)
        if (createdAt > now) continue

        const statusRng = createRandom(orderSeq * 2246822519)
        const { status, shippedAt } = resolveOrderStatus(statusRng, createdAt, now)
        const order: OrderRecord = {
          id: `ord_${orderSeq}`,
          number: `ЗК-${orderSeq}`,
          externalNumber,
          channel,
          status,
          sellerId,
          warehouseId,
          createdAt: iso(createdAt),
          shipBy: iso(createdAt + (channel === 'own' ? 48 : 36) * HOUR),
          shippedAt,
          deliveryCity,
          lines,
        }
        c.orders.push(order)
      }
    })
  }

  for (const order of c.orders) {
    if (!['reserved', 'picking', 'picked', 'packed'].includes(order.status)) continue
    for (const line of order.lines) {
      const stock = c.stock.find((s) => s.productId === line.productId && s.warehouseId === order.warehouseId)
      if (stock) stock.reserved = Math.min(stock.quantity, stock.reserved + line.quantity)
    }
  }

  // Поставки на ФЦ: каждые 4–8 дней по каждому складу договора
  let inboundSeq = 1000
  SELLERS.forEach((seller, sellerIndex) => {
    const sellerId = `org_s${sellerIndex + 1}`
    for (const contract of sellerContracts.get(sellerId)!) {
      for (const warehouseId of contract.warehouseIds) {
        let cursor = today - HISTORY_DAYS * DAY + rng.int(0, 5) * DAY
        while (cursor < today + 5 * DAY) {
          inboundSeq++
          const expectedAt = cursor + rng.int(9, 16) * HOUR
          cursor += rng.int(4, 8) * DAY
          c.inbound.push(
            buildInbound({
              seqNumber: inboundSeq,
              sellerId,
              warehouseId,
              products: sellerProducts.get(sellerId)!,
              tracksExpiry: seller.tracksExpiry,
              sellerActor: sellerActors.get(sellerId)!,
              receivers: fcReceivers.get(contract.fulfillmentId)!,
              expectedAt,
              now,
            }),
          )
        }
      }
    }
  })

  return c
}
