import { http, HttpResponse } from 'msw'
import type { ClientOption } from '@/modules/organizations/types'
import { db } from '../db/store'
import { withAuth } from '../lib/http'
import { toWarehouse } from '../lib/mappers'
import { visibleWarehouseIds } from '../lib/scope'
import { api } from './api'

export const directoryHandlers = [
  http.get(
    api('/warehouses'),
    withAuth(({ ctx }) => {
      const visible = visibleWarehouseIds(ctx)
      const warehouses = db.data.warehouses.filter((w) => !visible || visible.has(w.id))
      return HttpResponse.json(warehouses.map(toWarehouse))
    }),
  ),

  http.get(
    api('/clients'),
    withAuth(({ ctx }) => {
      const contracts = db.data.contracts.filter((c) => !ctx.org || c.fulfillmentId === ctx.org.id)
      const clients: ClientOption[] = contracts.map((contract) => ({
        id: contract.sellerId,
        name: db.data.organizations.find((o) => o.id === contract.sellerId)?.name ?? '—',
        contractId: contract.id,
        warehouseIds: contract.warehouseIds,
      }))
      clients.sort((a, b) => a.name.localeCompare(b.name, 'ru'))
      return HttpResponse.json(clients)
    }),
  ),
]
