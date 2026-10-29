import { http, HttpResponse } from 'msw'
import type { Role } from '@/modules/auth/types'
import { BILLING_SERVICES } from '@/modules/billing/labels'
import type {
  ActItem,
  BillingContract,
  BillingService,
  BillingSummary,
  ChargeItem,
  Tariff,
} from '@/modules/billing/types'
import { DAY } from '../db/seed'
import { db } from '../db/store'
import type { ContractRecord } from '../db/types'
import { localCollection } from '../lib/collection'
import { readJson, listParams, paginate, param, problem, sortItems, withAuth, type MockContext } from '../lib/http'
import { lookups } from '../lib/mappers'
import { createRandom } from '../lib/random'
import { api } from './api'

const FC_FINANCE: Role[] = ['fc_owner', 'fc_manager', 'fc_account_manager']
const HISTORY_DAYS = 120

const contractNumber = (c: ContractRecord) => `Д-${c.id.replace(/\D/g, '').padStart(4, '0')}`
const dayKey = (t: number) => new Date(t).toISOString().slice(0, 10)
const monthKey = (iso: string) => iso.slice(0, 7)

function defaultTariff(contractId: string): Tariff {
  const seed = [...contractId].reduce((s, ch) => s + ch.charCodeAt(0), 0)
  return {
    receivingPerUnit: 6 + (seed % 3),
    storagePerM3Day: 35 + (seed % 5) * 5,
    pickPerOrder: 45 + (seed % 4) * 5,
    packPerUnit: 8 + (seed % 3) * 2,
    fboPerBox: 120 + (seed % 3) * 20,
    returnPerUnit: 35 + (seed % 2) * 10,
  }
}

const tariffs = localCollection<Tariff & { id: string }>('tariffs.v2', () =>
  db.data.contracts.map((c) => ({ id: c.id, ...defaultTariff(c.id) })),
)
const tariffOf = (contractId: string): Tariff => tariffs.find(contractId) ?? defaultTariff(contractId)

function visibleContracts(ctx: MockContext): ContractRecord[] {
  if (!ctx.org) return db.data.contracts
  return db.data.contracts.filter((c) =>
    ctx.org!.type === 'fulfillment' ? c.fulfillmentId === ctx.org!.id : c.sellerId === ctx.org!.id,
  )
}

function counterparty(ctx: MockContext, c: ContractRecord) {
  const l = lookups()
  const id = ctx.org?.type === 'seller' ? c.fulfillmentId : c.sellerId
  return l.orgs.get(id)?.name ?? '—'
}

/** Начисления строятся из операций склада: заказы, приёмки, остатки, плюс синтетика для FBO и возвратов */
const cache = new Map<string, { key: string; at: number; items: Omit<ChargeItem, 'counterpartyName'>[] }>()

function chargesFor(c: ContractRecord): Omit<ChargeItem, 'counterpartyName'>[] {
  const key = JSON.stringify(tariffOf(c.id))
  const hit = cache.get(c.id)
  if (hit && hit.key === key && Date.now() - hit.at < 5 * 60_000) return hit.items
  const items = buildCharges(c)
  cache.set(c.id, { key, at: Date.now(), items })
  return items
}

function buildCharges(c: ContractRecord): Omit<ChargeItem, 'counterpartyName'>[] {
  const t = tariffOf(c.id)
  const rng = createRandom([...c.id].reduce((s, ch) => s * 31 + ch.charCodeAt(0), 7) >>> 0)
  const wh = new Set(c.warehouseIds)
  const start = Math.max(Date.parse(c.startedAt), Date.now() - HISTORY_DAYS * DAY)
  const byDay = new Map<string, Partial<Record<BillingService, number>>>()
  const add = (day: string, s: BillingService, q: number) => {
    const rec = byDay.get(day) ?? {}
    rec[s] = (rec[s] ?? 0) + q
    byDay.set(day, rec)
  }
  for (const o of db.data.orders) {
    if (o.sellerId !== c.sellerId || !wh.has(o.warehouseId)) continue
    if (!['picked', 'packed', 'shipped'].includes(o.status)) continue
    const t0 = Date.parse(o.createdAt)
    if (t0 < start) continue
    const units = o.lines.reduce((a, l) => a + l.quantity, 0)
    add(dayKey(t0), 'picking', 1)
    add(dayKey(t0), 'packing', units)
    if (o.status === 'shipped' && rng.bool(0.08))
      add(dayKey(Math.min(t0 + rng.int(3, 10) * DAY, Date.now())), 'returns', units)
  }
  for (const i of db.data.inbound) {
    if (i.sellerId !== c.sellerId || !wh.has(i.warehouseId)) continue
    const units = i.lines.reduce((a, l) => a + l.receivedQty, 0)
    if (units && Date.parse(i.expectedAt) >= start)
      add(dayKey(Math.min(Date.parse(i.expectedAt), Date.now())), 'receiving', units)
  }
  const l = lookups()
  const liters = db.data.stock
    .filter((s) => s.sellerId === c.sellerId && wh.has(s.warehouseId))
    .reduce((a, s) => a + s.quantity * (l.products.get(s.productId)?.volumeLiters ?? 1), 0)
  for (let d = start; d <= Date.now(); d += DAY) {
    const k = dayKey(d)
    add(k, 'storage', Math.round((liters / 1000) * (0.85 + rng.float(0, 0.3)) * 10) / 10)
    if (new Date(d).getDay() === 2 || rng.bool(0.1)) add(k, 'fbo', rng.int(4, 24))
    // базовый поток заказов вне сгенерированных, чтобы история за 4 месяца была правдоподобной
    const extra = rng.int(2, 14)
    add(k, 'picking', extra)
    add(k, 'packing', extra * rng.int(1, 3))
    if (rng.bool(0.25)) add(k, 'receiving', rng.int(40, 400))
  }
  const rates: Record<BillingService, number> = {
    receiving: t.receivingPerUnit,
    storage: t.storagePerM3Day,
    picking: t.pickPerOrder,
    packing: t.packPerUnit,
    fbo: t.fboPerBox,
    returns: t.returnPerUnit,
  }
  const out: Omit<ChargeItem, 'counterpartyName'>[] = []
  for (const [date, rec] of byDay) {
    for (const s of Object.keys(rec) as BillingService[]) {
      const quantity = rec[s]!
      out.push({
        id: `${c.id}:${date}:${s}`,
        contractId: c.id,
        date,
        service: s,
        quantity,
        unit: BILLING_SERVICES[s].unit,
        rate: rates[s],
        amount: Math.round(quantity * rates[s] * 100) / 100,
      })
    }
  }
  return out
}

function charges(ctx: MockContext, contractId?: string | null): ChargeItem[] {
  return visibleContracts(ctx)
    .filter((c) => !contractId || c.id === contractId)
    .flatMap((c) => chargesFor(c).map((x) => ({ ...x, counterpartyName: counterparty(ctx, c) })))
}

interface ActRecord {
  id: string
  contractId: string
  period: string
  amount: number
  status: 'issued' | 'paid'
  issuedAt: string
  paidAt?: string
}

const acts = localCollection<ActRecord>('acts.v2', () => {
  const now = new Date()
  const out: ActRecord[] = []
  for (const c of db.data.contracts) {
    const sums = new Map<string, number>()
    for (const x of chargesFor(c)) sums.set(monthKey(x.date), (sums.get(monthKey(x.date)) ?? 0) + x.amount)
    for (let m = 3; m >= 1; m--) {
      const d = new Date(now.getFullYear(), now.getMonth() - m, 1)
      const period = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
      const amount = Math.round((sums.get(period) ?? 0) * 100) / 100
      if (!amount) continue
      const issued = new Date(now.getFullYear(), now.getMonth() - m + 1, 3)
      out.push({
        id: `act_${c.id}_${period}`,
        contractId: c.id,
        period,
        amount,
        status: m > 1 ? 'paid' : 'issued',
        issuedAt: issued.toISOString(),
        paidAt: m > 1 ? new Date(issued.getTime() + 6 * DAY).toISOString() : undefined,
      })
    }
  }
  return out
})

let actSeq = 0
function actItem(
  ctx: MockContext,
  a: ActRecord | (Omit<ActRecord, 'status' | 'issuedAt'> & { status: 'draft' }),
): ActItem {
  const c = db.data.contracts.find((x) => x.id === a.contractId)!
  const issuedAt = 'issuedAt' in a ? a.issuedAt : undefined
  return {
    id: a.id,
    number: `АКТ-${a.period.replace('-', '')}-${c.id.replace(/\D/g, '').padStart(3, '0')}`,
    contractId: a.contractId,
    counterpartyName: counterparty(ctx, c),
    period: a.period,
    amount: a.amount,
    status: a.status,
    issuedAt,
    paidAt: 'paidAt' in a ? a.paidAt : undefined,
    dueAt: issuedAt ? new Date(Date.parse(issuedAt) + 10 * DAY).toISOString() : undefined,
  }
}

function actsFor(ctx: MockContext, contractId?: string | null): ActItem[] {
  const current = monthKey(new Date().toISOString())
  const list: ActItem[] = []
  for (const c of visibleContracts(ctx)) {
    if (contractId && c.id !== contractId) continue
    const own = acts.all.filter((a) => a.contractId === c.id)
    list.push(...own.map((a) => actItem(ctx, a)))
    if (!own.some((a) => a.period === current)) {
      const amount = chargesFor(c)
        .filter((x) => monthKey(x.date) === current)
        .reduce((s, x) => s + x.amount, 0)
      list.push(
        actItem(ctx, {
          id: `draft_${c.id}`,
          contractId: c.id,
          period: current,
          amount: Math.round(amount * 100) / 100,
          status: 'draft',
        }),
      )
    }
  }
  return list.sort(
    (a, b) => b.period.localeCompare(a.period) || a.counterpartyName.localeCompare(b.counterpartyName),
  )
}

function ownContract(ctx: MockContext, id: string) {
  return visibleContracts(ctx).find((c) => c.id === id)
}

export const billingHandlers = [
  http.get(
    api('/billing/contracts'),
    withAuth(({ ctx }) =>
      HttpResponse.json(
        visibleContracts(ctx).map<BillingContract>((c) => ({
          contractId: c.id,
          contractNumber: contractNumber(c),
          counterpartyName: counterparty(ctx, c),
        })),
      ),
    ),
  ),

  http.get(
    api('/billing/summary'),
    withAuth(({ ctx, url }) => {
      const contractId = url.searchParams.get('contractId')
      const list = charges(ctx, contractId)
      const now = new Date()
      const cur = monthKey(now.toISOString())
      const prev = monthKey(new Date(now.getFullYear(), now.getMonth() - 1, 15).toISOString())
      const sum = (f: (x: ChargeItem) => boolean) =>
        Math.round(list.filter(f).reduce((s, x) => s + x.amount, 0))
      const byService = (Object.keys(BILLING_SERVICES) as BillingService[]).map((service) => ({
        service,
        amount: sum((x) => x.service === service && monthKey(x.date) === cur),
      }))
      const since = dayKey(Date.now() - 29 * DAY)
      const dailyMap = new Map<string, Partial<Record<BillingService, number>>>()
      for (const x of list) {
        if (x.date < since) continue
        const rec = dailyMap.get(x.date) ?? {}
        rec[x.service] = Math.round((rec[x.service] ?? 0) + x.amount)
        dailyMap.set(x.date, rec)
      }
      const actList = actsFor(ctx, contractId)
      const unpaid = actList.filter((a) => a.status === 'issued').reduce((s, a) => s + a.amount, 0)
      const monthAccrued = sum((x) => monthKey(x.date) === cur)
      const summary: BillingSummary = {
        monthAccrued,
        prevMonthAccrued: sum((x) => monthKey(x.date) === prev),
        unpaid: Math.round(unpaid),
        balance: -Math.round(unpaid + monthAccrued),
        byService,
        daily: [...dailyMap]
          .sort(([a], [b]) => a.localeCompare(b))
          .map(([date, amounts]) => ({ date, amounts })),
      }
      return HttpResponse.json(summary)
    }),
  ),

  http.get(
    api('/billing/charges'),
    withAuth(({ ctx, url }) => {
      const p = listParams(url, { sortField: 'date' })
      const service = url.searchParams.get('service')
      const items = charges(ctx, url.searchParams.get('contractId')).filter(
        (x) => !service || x.service === service,
      )
      return HttpResponse.json(paginate(sortItems(items, p.sortField, p.sortOrder), p.page, p.pageSize))
    }),
  ),

  http.get(
    api('/billing/acts'),
    withAuth(({ ctx, url }) => HttpResponse.json(actsFor(ctx, url.searchParams.get('contractId')))),
  ),

  http.post(
    api('/billing/acts/:id/issue'),
    withAuth(
      ({ ctx, params }) => {
        const id = param(params, 'id')
        const draft = actsFor(ctx).find((a) => a.id === id && a.status === 'draft')
        if (!draft) return problem(404, 'Черновик акта не найден')
        const rec: ActRecord = {
          id: `act_${draft.contractId}_${draft.period}_${++actSeq}`,
          contractId: draft.contractId,
          period: draft.period,
          amount: draft.amount,
          status: 'issued',
          issuedAt: new Date().toISOString(),
        }
        acts.upsert(rec)
        return HttpResponse.json(actItem(ctx, rec))
      },
      { roles: FC_FINANCE },
    ),
  ),

  http.post(
    api('/billing/acts/:id/pay'),
    withAuth(
      ({ ctx, params }) => {
        const rec = acts.find(param(params, 'id'))
        if (!rec || !ownContract(ctx, rec.contractId)) return problem(404, 'Акт не найден')
        if (rec.status !== 'issued') return problem(409, 'Акт уже оплачен')
        const next: ActRecord = { ...rec, status: 'paid', paidAt: new Date().toISOString() }
        acts.upsert(next)
        return HttpResponse.json(actItem(ctx, next))
      },
      { roles: FC_FINANCE },
    ),
  ),

  http.get(
    api('/billing/tariffs/:id'),
    withAuth(({ ctx, params }) => {
      const id = param(params, 'id')
      if (!ownContract(ctx, id)) return problem(404, 'Договор не найден')
      return HttpResponse.json(tariffOf(id))
    }),
  ),

  http.put(
    api('/billing/tariffs/:id'),
    withAuth(
      async ({ ctx, params, request }) => {
        const id = param(params, 'id')
        if (!ownContract(ctx, id)) return problem(404, 'Договор не найден')
        const body = (await readJson(request)) as Tariff
        for (const [k, v] of Object.entries(body)) {
          if (typeof v !== 'number' || !(v >= 0)) return problem(422, 'Некорректный тариф', k)
        }
        tariffs.upsert({ id, ...body })
        return HttpResponse.json(body)
      },
      { roles: ['fc_owner', 'fc_manager'] },
    ),
  ),
]
