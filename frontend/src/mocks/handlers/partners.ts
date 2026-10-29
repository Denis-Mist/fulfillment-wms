import { http, HttpResponse } from 'msw'
import { SELLER_ROLES } from '@/modules/auth/roles'
import type { ConnectRequestInput, FcService, FulfillmentCard } from '@/modules/partners/types'
import { db } from '../db/store'
import type { OrgRecord } from '../db/types'
import { readJson, param, problem, withAuth } from '../lib/http'
import { createRandom } from '../lib/random'
import { api } from './api'
import { requests } from './catalog'

const SERVICES: FcService[] = ['storage', 'fbs', 'fbo', 'marking', 'returns', 'photo', 'kits']

function seedOf(id: string) {
  return [...id].reduce((s, ch) => s * 31 + ch.charCodeAt(0), 7) >>> 0
}

function cardFor(fc: OrgRecord, sellerId: string): FulfillmentCard {
  const rng = createRandom(seedOf(fc.id))
  const warehouses = db.data.warehouses
    .filter((w) => w.organizationId === fc.id && w.isActive)
    .map((w) => ({ id: w.id, name: w.name, city: w.city, address: w.address, areaSqm: w.areaSqm }))
  const contract = db.data.contracts.find((c) => c.fulfillmentId === fc.id && c.sellerId === sellerId)
  const mine = requests(fc.id)
    .all.filter((r) => r.sellerId === sellerId)
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt))[0]
  const services = SERVICES.filter((_, i) => i < 3 || rng.bool(0.6))
  return {
    id: fc.id,
    name: fc.name,
    city: fc.city,
    cities: [...new Set(warehouses.map((w) => w.city))],
    rating: Math.round(rng.float(4.2, 4.95) * 10) / 10,
    clientsCount: db.data.contracts.filter((c) => c.fulfillmentId === fc.id).length + rng.int(8, 60),
    services,
    marketplaces: rng.bool(0.7) ? ['wb', 'ozon', 'yandex'] : ['wb', 'ozon'],
    warehouses,
    rates: {
      receivingPerUnit: rng.int(3, 8),
      storagePerM3Day: 30 + rng.int(0, 5) * 5,
      pickingPerOrder: rng.int(35, 60),
      packingPerUnit: rng.int(6, 14),
    },
    slaHours: rng.pick([12, 24, 24, 48]),
    connection: contract ? 'connected' : (mine?.status ?? 'none'),
    contract: contract
      ? { id: contract.id, startedAt: contract.startedAt, warehouseIds: contract.warehouseIds }
      : undefined,
    requestedAt: mine?.createdAt,
  }
}

const fulfillments = () =>
  db.data.organizations.filter((o) => o.type === 'fulfillment' && o.status === 'active')

export const partnersHandlers = [
  http.get(
    api('/seller/fulfillments'),
    withAuth(
      ({ ctx }) => {
        if (!ctx.org) return HttpResponse.json([])
        const cards = fulfillments().map((fc) => cardFor(fc, ctx.org!.id))
        const order = { connected: 0, accepted: 1, pending: 2, none: 3, declined: 4 }
        cards.sort((a, b) => order[a.connection] - order[b.connection] || b.rating - a.rating)
        return HttpResponse.json(cards)
      },
      { roles: SELLER_ROLES },
    ),
  ),

  http.post(
    api('/seller/fulfillments/:id/connect'),
    withAuth(
      async ({ ctx, params, request }) => {
        const fc = fulfillments().find((o) => o.id === param(params, 'id'))
        if (!fc || !ctx.org) return problem(404, 'Фулфилмент-центр не найден')
        const current = cardFor(fc, ctx.org.id)
        if (current.connection === 'connected') return problem(409, 'Договор уже заключён')
        if (current.connection === 'pending') return problem(409, 'Заявка уже на рассмотрении')
        const input = (await readJson(request)) as ConnectRequestInput
        if (!(input.skuEstimate > 0) || !(input.ordersPerDay >= 0))
          return problem(422, 'Укажите ассортимент и объём заказов')
        if (!input.marketplaces?.length) return problem(422, 'Выберите хотя бы один маркетплейс')
        const store = requests(fc.id)
        store.upsert({
          id: `creq_${Date.now().toString(36)}`,
          companyName: ctx.org.name,
          inn: ctx.org.inn,
          city: ctx.org.city,
          contactName: ctx.user.fullName,
          contactPhone: ctx.user.phone,
          skuEstimate: Math.round(input.skuEstimate),
          ordersPerDay: Math.round(input.ordersPerDay),
          marketplaces: input.marketplaces,
          message: input.message?.trim() ?? '',
          createdAt: new Date().toISOString(),
          status: 'pending',
          sellerId: ctx.org.id,
        })
        return HttpResponse.json(cardFor(fc, ctx.org.id))
      },
      { roles: ['seller_owner'] },
    ),
  ),
]
