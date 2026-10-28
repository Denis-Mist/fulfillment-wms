import { http, HttpResponse, type PathParams } from 'msw'
import { SELLER_ROLES } from '@/modules/auth/roles'
import type {
  ConnectInput,
  Integration,
  IntegrationChannel,
  IntegrationLogEntry,
  IntegrationSettingsInput,
} from '@/modules/integrations/types'
import { localCollection } from '../lib/collection'
import { param, problem, withAuth } from '../lib/http'
import { createRandom } from '../lib/random'
import { api } from './api'

type Row = Integration & { id: string }
const CHANNELS: IntegrationChannel[] = ['wb', 'ozon']
const HOUR = 3_600_000

function seedLog(channel: IntegrationChannel, seed: number): IntegrationLogEntry[] {
  const rng = createRandom(seed)
  const now = Date.now()
  const log: IntegrationLogEntry[] = []
  for (let i = 0; i < 14; i++) {
    const at = new Date(now - (i * 2 + rng.float(0, 1)) * HOUR).toISOString()
    if (i === 5 && channel === 'ozon') {
      log.push({
        id: `${channel}_${i}`,
        at,
        kind: 'error',
        message: 'Таймаут ответа API маркетплейса, повтор через 5 мин',
      })
      continue
    }
    const stock = i % 3 === 1
    const count = stock ? rng.int(40, 180) : rng.int(0, 12)
    log.push({
      id: `${channel}_${i}`,
      at,
      kind: stock ? 'stock' : 'orders',
      count,
      message: stock ? `Остатки выгружены: ${count} SKU` : `Загружено заказов FBS: ${count}`,
    })
  }
  return log
}

const stores = new Map<string, ReturnType<typeof localCollection<Row>>>()
function storeFor(orgId: string) {
  let s = stores.get(orgId)
  if (!s) {
    const seed = [...orgId].reduce((a, ch) => a * 31 + ch.charCodeAt(0), 11) >>> 0
    s = localCollection<Row>(`integrations.${orgId}`, () =>
      CHANNELS.map((channel, i) => {
        const connected = channel === 'wb' || seed % 2 === 0
        return {
          id: channel,
          channel,
          status: connected ? 'connected' : 'disconnected',
          keyMask: connected ? `••••${(seed + i * 977).toString(36).slice(-4)}` : undefined,
          clientId: connected && channel === 'ozon' ? String(100000 + (seed % 899999)) : undefined,
          connectedAt: connected ? new Date(Date.now() - (40 + i * 11) * 24 * HOUR).toISOString() : undefined,
          lastSyncAt: connected ? new Date(Date.now() - (8 + i * 5) * 60_000).toISOString() : undefined,
          autoImportFbs: connected,
          pushStock: connected && channel === 'wb',
          syncIntervalMin: 15,
          log: connected ? seedLog(channel, seed + i) : [],
        }
      }),
    )
    stores.set(orgId, s)
  }
  return s
}

const strip = (row: Row): Integration => {
  const { id, ...rest } = row
  void id
  return rest
}

function channelOf(params: PathParams) {
  const ch = param(params, 'channel') as IntegrationChannel
  return CHANNELS.includes(ch) ? ch : null
}

const MANAGE = ['seller_owner'] as const

export const integrationsHandlers = [
  http.get(
    api('/integrations'),
    withAuth(({ ctx }) => HttpResponse.json(ctx.org ? storeFor(ctx.org.id).all.map(strip) : []), {
      roles: SELLER_ROLES,
    }),
  ),

  http.post(
    api('/integrations/:channel/connect'),
    withAuth(
      async ({ ctx, params, request }) => {
        const ch = channelOf(params)
        if (!ch || !ctx.org) return problem(404, 'Неизвестный маркетплейс')
        const { apiKey, clientId } = (await request.json()) as ConnectInput
        const key = apiKey?.trim() ?? ''
        if (key.length < 20)
          return problem(422, 'Ключ API слишком короткий', 'Скопируйте ключ целиком из личного кабинета')
        if (ch === 'ozon' && !/^\d{4,10}$/.test(clientId?.trim() ?? ''))
          return problem(422, 'Укажите Client ID', 'Client ID Ozon — число из настроек Seller API')
        const store = storeFor(ctx.org.id)
        const row = store.find(ch)!
        const now = new Date().toISOString()
        return HttpResponse.json(
          strip(
            store.upsert({
              ...row,
              status: 'connected',
              keyMask: `••••${key.slice(-4)}`,
              clientId: ch === 'ozon' ? clientId!.trim() : undefined,
              connectedAt: now,
              lastSyncAt: now,
              autoImportFbs: true,
              log: [
                {
                  id: `${ch}_${Date.now()}`,
                  at: now,
                  kind: 'connect' as const,
                  message: 'Кабинет подключён, ключ проверен',
                },
                ...row.log,
              ].slice(0, 30),
            }),
          ),
        )
      },
      { roles: [...MANAGE] },
    ),
  ),

  http.post(
    api('/integrations/:channel/disconnect'),
    withAuth(
      ({ ctx, params }) => {
        const ch = channelOf(params)
        if (!ch || !ctx.org) return problem(404, 'Неизвестный маркетплейс')
        const store = storeFor(ctx.org.id)
        const row = store.find(ch)!
        return HttpResponse.json(
          strip(
            store.upsert({
              ...row,
              status: 'disconnected',
              keyMask: undefined,
              clientId: undefined,
              autoImportFbs: false,
              pushStock: false,
            }),
          ),
        )
      },
      { roles: [...MANAGE] },
    ),
  ),

  http.post(
    api('/integrations/:channel/sync'),
    withAuth(
      ({ ctx, params }) => {
        const ch = channelOf(params)
        if (!ch || !ctx.org) return problem(404, 'Неизвестный маркетплейс')
        const store = storeFor(ctx.org.id)
        const row = store.find(ch)!
        if (row.status === 'disconnected') return problem(409, 'Кабинет не подключён')
        const now = new Date().toISOString()
        const count = Math.floor(Math.random() * 7)
        const entries: IntegrationLogEntry[] = [
          {
            id: `${ch}_${Date.now()}_o`,
            at: now,
            kind: 'orders',
            count,
            message: `Загружено заказов FBS: ${count}`,
          },
        ]
        if (row.pushStock) {
          const sku = 60 + Math.floor(Math.random() * 100)
          entries.unshift({
            id: `${ch}_${Date.now()}_s`,
            at: now,
            kind: 'stock',
            count: sku,
            message: `Остатки выгружены: ${sku} SKU`,
          })
        }
        return HttpResponse.json(
          strip(
            store.upsert({
              ...row,
              status: 'connected',
              lastSyncAt: now,
              log: [...entries, ...row.log].slice(0, 30),
            }),
          ),
        )
      },
      { roles: SELLER_ROLES },
    ),
  ),

  http.put(
    api('/integrations/:channel'),
    withAuth(
      async ({ ctx, params, request }) => {
        const ch = channelOf(params)
        if (!ch || !ctx.org) return problem(404, 'Неизвестный маркетплейс')
        const input = (await request.json()) as IntegrationSettingsInput
        if (![5, 15, 30, 60].includes(input.syncIntervalMin)) return problem(422, 'Недопустимый интервал')
        const store = storeFor(ctx.org.id)
        const row = store.find(ch)!
        if (row.status === 'disconnected') return problem(409, 'Кабинет не подключён')
        return HttpResponse.json(
          strip(
            store.upsert({
              ...row,
              autoImportFbs: !!input.autoImportFbs,
              pushStock: !!input.pushStock,
              syncIntervalMin: input.syncIntervalMin,
            }),
          ),
        )
      },
      { roles: [...MANAGE] },
    ),
  ),
]
