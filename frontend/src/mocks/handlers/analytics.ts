import { http, HttpResponse } from 'msw'
import type { Role } from '@/modules/auth/types'
import type {
  AbcClass,
  CalcMeta,
  ForecastAlgorithm,
  ForecastPoint,
  ForecastResult,
  KpiReport,
  SlottingAlgorithm,
  SlottingMove,
  SlottingResult,
  StaffingDay,
} from '@/modules/analytics/types'
import { DAY } from '../db/seed'
import { db } from '../db/store'
import type { LocationRecord } from '../db/types'
import { localCollection } from '../lib/collection'
import { readJson, problem, withAuth, type MockContext } from '../lib/http'
import { lookups } from '../lib/mappers'
import { createRandom } from '../lib/random'
import { visibleWarehouseIds } from '../lib/scope'
import { api } from './api'

const MANAGEMENT: Role[] = ['fc_owner', 'fc_manager']
const HISTORY_DAYS = 56
const BACKTEST_DAYS = 14
/** Нормативы производительности, единиц в смену на сотрудника */
const RATE = { picker: 420, packer: 260, receiver: 900 }

interface PlanRecord {
  id: string
  approvedAt: string
  approvedBy: string
  algorithm: ForecastAlgorithm
}
interface AppliedRecord {
  id: string
  appliedAt: string
}
const plans = localCollection<PlanRecord>('staffPlans', () => [])
const applied = localCollection<AppliedRecord>('slottingApplied', () => [])

const hash = (s: string) => [...s].reduce((h, ch) => (h * 31 + ch.charCodeAt(0)) >>> 0, 7)
const startOfDay = (t: number) => {
  const d = new Date(t)
  d.setHours(0, 0, 0, 0)
  return d.getTime()
}

function scopeWarehouses(ctx: MockContext, warehouseId: string | null) {
  const visible = visibleWarehouseIds(ctx)
  const all = db.data.warehouses.filter((w) => !visible || visible.has(w.id))
  return warehouseId ? all.filter((w) => w.id === warehouseId) : all
}

function meta(algorithm: string, seed: number, started: number): CalcMeta {
  return {
    algorithm,
    version: algorithm === 'holt_winters' || algorithm === 'abc_affinity' ? '1.3.0' : '1.0.0',
    seed,
    dataVersion: `snap-${new Date(startOfDay(Date.now())).toISOString().slice(0, 10)}`,
    computedAt: new Date().toISOString(),
    durationMs: Math.max(4, Math.round(performance.now() - started)),
  }
}

/** Синтетическая история заказов в день: тренд + недельная сезонность + шум */
function history(seed: number, scale: number) {
  const rng = createRandom(seed)
  const today = startOfDay(Date.now())
  const weekly = [1.18, 1.1, 1.04, 1.0, 1.08, 0.82, 0.78]
  return Array.from({ length: HISTORY_DAYS }, (_, i) => {
    const date = today - (HISTORY_DAYS - i) * DAY
    const dow = (new Date(date).getDay() + 6) % 7
    const trend = 1 + i * 0.004
    const promo = i === 30 || i === 31 ? 1.35 : 1
    return { date, value: Math.round(scale * trend * weekly[dow]! * promo * rng.float(0.9, 1.1)) }
  })
}

/** Аддитивная модель Хольта–Уинтерса с недельным сезоном */
function holtWinters(values: number[], horizon: number) {
  const m = 7
  const alpha = 0.35
  const beta = 0.05
  const gamma = 0.3
  let level = values.slice(0, m).reduce((a, b) => a + b, 0) / m
  let trend = (values.slice(m, 2 * m).reduce((a, b) => a + b, 0) / m - level) / m
  const season = values.slice(0, m).map((v) => v - level)
  for (let i = m; i < values.length; i++) {
    const s = season[i % m]!
    const prevLevel = level
    level = alpha * (values[i]! - s) + (1 - alpha) * (level + trend)
    trend = beta * (level - prevLevel) + (1 - beta) * trend
    season[i % m] = gamma * (values[i]! - level) + (1 - gamma) * s
  }
  return Array.from({ length: horizon }, (_, h) => level + (h + 1) * trend + season[(values.length + h) % m]!)
}

const movingAverage = (values: number[], horizon: number) => {
  const avg = values.slice(-7).reduce((a, b) => a + b, 0) / 7
  return Array.from({ length: horizon }, () => avg)
}

const predict = (algorithm: ForecastAlgorithm, values: number[], horizon: number) =>
  algorithm === 'holt_winters' ? holtWinters(values, horizon) : movingAverage(values, horizon)

function errors(actual: number[], predicted: number[]) {
  let ape = 0
  let ae = 0
  actual.forEach((a, i) => {
    ae += Math.abs(a - predicted[i]!)
    ape += Math.abs(a - predicted[i]!) / Math.max(1, a)
  })
  return { mape: Math.round((ape / actual.length) * 1000) / 10, mae: Math.round(ae / actual.length) }
}

function staffAvailable(warehouseIds: string[]) {
  const orgIds = new Set(
    db.data.warehouses.filter((w) => warehouseIds.includes(w.id)).map((w) => w.organizationId),
  )
  return db.data.users.filter((u) =>
    u.memberships.some(
      (m) => orgIds.has(m.organizationId) && ['fc_picker', 'fc_packer', 'fc_receiver'].includes(m.role),
    ),
  ).length
}

const planKey = (ctx: MockContext, warehouseId: string | null) =>
  `${ctx.org?.id ?? 'all'}:${warehouseId ?? 'all'}`

function locDistance(l: LocationRecord) {
  return Math.round((l.x + l.y + (l.level - 1) * 1.5) * 10) / 10
}

export const analyticsHandlers = [
  http.get(
    api('/analytics/forecast'),
    withAuth(
      ({ ctx, url }) => {
        const started = performance.now()
        const warehouseId = url.searchParams.get('warehouseId')
        const horizon = Math.min(28, Math.max(7, Number(url.searchParams.get('horizon') ?? 14)))
        const algorithm = (url.searchParams.get('algorithm') ?? 'holt_winters') as ForecastAlgorithm
        const warehouses = scopeWarehouses(ctx, warehouseId)
        if (!warehouses.length) return problem(404, 'Склад не найден')
        const seed = hash(warehouses.map((w) => w.id).join(','))
        const available = staffAvailable(warehouses.map((w) => w.id))
        // Масштаб демо-истории согласован со штатом, чтобы рекомендации были правдоподобны
        const scale = Math.max(60, available * 36)
        const hist = history(seed, scale)
        const values = hist.map((h) => h.value)

        const train = values.slice(0, -BACKTEST_DAYS)
        const test = values.slice(-BACKTEST_DAYS)
        const back = predict(algorithm, train, BACKTEST_DAYS)
        const err = errors(test, back)
        const baseErr = errors(test, movingAverage(train, BACKTEST_DAYS))
        const future = predict(algorithm, values, horizon)
        const spread = 1.28 * err.mae

        const series: ForecastPoint[] = hist.map((h, i) => ({
          date: new Date(h.date).toISOString(),
          actual: h.value,
          predicted:
            i >= HISTORY_DAYS - BACKTEST_DAYS
              ? Math.round(back[i - (HISTORY_DAYS - BACKTEST_DAYS)]!)
              : undefined,
        }))
        const today = startOfDay(Date.now())
        const staffing: StaffingDay[] = []
        future.forEach((v, h) => {
          const date = new Date(today + h * DAY).toISOString()
          const orders = Math.max(0, Math.round(v))
          series.push({
            date,
            predicted: orders,
            low: Math.max(0, Math.round(v - spread)),
            high: Math.round(v + spread),
          })
          const units = Math.round(orders * 2.6)
          const pickers = Math.ceil(units / RATE.picker)
          const packers = Math.ceil(units / RATE.packer)
          const receivers = Math.ceil((units * 0.9) / RATE.receiver)
          staffing.push({
            date,
            orders,
            units,
            pickers,
            packers,
            receivers,
            total: pickers + packers + receivers,
            available,
          })
        })
        const plan = plans.find(planKey(ctx, warehouseId))
        const result: ForecastResult = {
          meta: meta(algorithm, seed, started),
          horizon,
          series,
          backtest: { ...err, baselineMape: baseErr.mape, days: BACKTEST_DAYS },
          staffing,
          plan: plan
            ? { approvedAt: plan.approvedAt, approvedBy: plan.approvedBy, algorithm: plan.algorithm }
            : undefined,
        }
        return HttpResponse.json(result)
      },
      { roles: MANAGEMENT },
    ),
  ),

  http.post(
    api('/analytics/forecast/approve'),
    withAuth(
      async ({ ctx, request }) => {
        const body = (await readJson(request)) as { warehouseId?: string; algorithm?: ForecastAlgorithm }
        if (body.algorithm && !['holt_winters', 'moving_average'].includes(body.algorithm))
          return problem(422, 'Неизвестный алгоритм', 'algorithm')
        const record = plans.upsert({
          id: planKey(ctx, body.warehouseId ?? null),
          approvedAt: new Date().toISOString(),
          approvedBy: ctx.user.fullName,
          algorithm: body.algorithm || 'holt_winters',
        })
        return HttpResponse.json({
          approvedAt: record.approvedAt,
          approvedBy: record.approvedBy,
          algorithm: record.algorithm,
        })
      },
      { roles: MANAGEMENT },
    ),
  ),

  http.get(
    api('/analytics/slotting'),
    withAuth(
      ({ ctx, url }) => {
        const started = performance.now()
        const warehouseId = url.searchParams.get('warehouseId')
        const algorithm = (url.searchParams.get('algorithm') ?? 'abc_affinity') as SlottingAlgorithm
        const warehouses = scopeWarehouses(ctx, warehouseId)
        const whIds = new Set(warehouses.map((w) => w.id))
        const l = lookups()
        const locs = new Map(db.data.locations.map((loc) => [loc.id, loc]))
        const seed = hash([...whIds].join(','))
        const rng = createRandom(seed)

        // Частота отборов: из заказов + детерминированная добавка
        const picks = new Map<string, number>()
        const pairs = new Map<string, Map<string, number>>()
        for (const o of db.data.orders) {
          if (!whIds.has(o.warehouseId)) continue
          for (const line of o.lines) picks.set(line.productId, (picks.get(line.productId) ?? 0) + 1)
          for (const a of o.lines)
            for (const b of o.lines) {
              if (a.productId === b.productId) continue
              const m = pairs.get(a.productId) ?? new Map<string, number>()
              m.set(b.productId, (m.get(b.productId) ?? 0) + 1)
              pairs.set(a.productId, m)
            }
        }
        const stock = db.data.stock.filter((s) => whIds.has(s.warehouseId) && s.quantity > 0)
        const byProduct = new Map<string, (typeof stock)[number]>()
        for (const s of stock) if (!byProduct.has(s.productId)) byProduct.set(s.productId, s)
        const items = [...byProduct.values()].map((s) => ({
          stock: s,
          picks: Math.round(
            (picks.get(s.productId) ?? 0) * 6 +
              rng.weighted([
                [1, 6],
                [4, 3],
                [14, 1],
              ]) *
                rng.int(1, 6),
          ),
        }))
        items.sort((a, b) => b.picks - a.picks)
        const totalPicks = items.reduce((a, b) => a + b.picks, 0) || 1
        let cum = 0
        const cls = new Map<string, AbcClass>()
        for (const it of items) {
          cum += it.picks
          cls.set(it.stock.productId, cum / totalPicks <= 0.8 ? 'A' : cum / totalPicks <= 0.95 ? 'B' : 'C')
        }

        // Свободные близкие ячейки отбора
        const occupied = new Set(stock.map((s) => s.locationId))
        const free = db.data.locations
          .filter((loc) => whIds.has(loc.warehouseId) && !occupied.has(loc.id))
          .sort((a, b) => locDistance(a) - locDistance(b))

        const appliedIds = new Set(applied.all.map((a) => a.id))
        const moves: SlottingMove[] = []
        let current = 0
        let projected = 0
        const usedFree = new Set<string>()
        const placed = new Map<string, LocationRecord>()
        for (const it of items) {
          const from = locs.get(it.stock.locationId)
          if (!from) continue
          const d = locDistance(from)
          current += it.picks * d * 2
          const abc = cls.get(it.stock.productId)!
          let target: LocationRecord | undefined
          if (abc === 'A' && d > 12) {
            const partner =
              algorithm === 'abc_affinity'
                ? [...(pairs.get(it.stock.productId)?.entries() ?? [])]
                    .sort((a, b) => b[1] - a[1])
                    .find(([p]) => placed.has(p))
                : undefined
            const anchor = partner ? placed.get(partner[0]) : undefined
            const candidates = free.filter(
              (loc) =>
                loc.warehouseId === from.warehouseId && !usedFree.has(loc.id) && locDistance(loc) < d - 4,
            )
            target = anchor
              ? candidates.sort(
                  (a, b) =>
                    Math.abs(a.x - anchor.x) +
                    Math.abs(a.y - anchor.y) -
                    (Math.abs(b.x - anchor.x) + Math.abs(b.y - anchor.y)),
                )[0]
              : candidates[0]
            if (target) {
              usedFree.add(target.id)
              const product = l.products.get(it.stock.productId)
              const id = `slot_${algorithm}_${it.stock.id}`
              const affinityBonus = anchor ? 0.12 : 0
              const gain = Math.round(it.picks * (d - locDistance(target)) * 2 * (1 + affinityBonus))
              moves.push({
                id,
                productId: it.stock.productId,
                sku: product?.sku ?? '',
                name: product?.name ?? '',
                sellerName: l.orgs.get(it.stock.sellerId)?.name ?? '',
                abc,
                picks30d: it.picks,
                affinitySku: partner ? l.products.get(partner[0])?.sku : undefined,
                fromCell: from.code,
                fromDistance: d,
                toCell: target.code,
                toDistance: locDistance(target),
                gain,
                applied: appliedIds.has(id),
              })
            }
          }
          const final = target ?? from
          placed.set(it.stock.productId, final)
          projected += it.picks * locDistance(final) * 2
        }
        const gainTotal = moves.reduce((a, m) => a + m.gain, 0)
        projected = Math.min(projected, current - gainTotal)
        const classes = (['A', 'B', 'C'] as AbcClass[]).reduce(
          (acc, c) => {
            const list = items.filter((it) => cls.get(it.stock.productId) === c)
            acc[c] = {
              sku: list.length,
              picksShare: Math.round((list.reduce((a, b) => a + b.picks, 0) / totalPicks) * 100),
            }
            return acc
          },
          {} as SlottingResult['classes'],
        )
        moves.sort((a, b) => b.gain - a.gain)
        const result: SlottingResult = {
          meta: meta(algorithm, seed, started),
          classes,
          currentDistance: Math.round(current),
          projectedDistance: Math.round(projected),
          reductionPct: current ? Math.round(((current - projected) / current) * 1000) / 10 : 0,
          moves: moves.slice(0, 20),
        }
        return HttpResponse.json(result)
      },
      { roles: MANAGEMENT },
    ),
  ),

  http.post(
    api('/analytics/slotting/apply'),
    withAuth(
      async ({ request }) => {
        const body = (await readJson(request)) as { ids?: string[] }
        if (!body.ids?.length) return problem(422, 'Не выбраны рекомендации', 'ids')
        const at = new Date().toISOString()
        for (const id of body.ids) applied.upsert({ id, appliedAt: at })
        return HttpResponse.json({ applied: body.ids.length })
      },
      { roles: MANAGEMENT },
    ),
  ),

  http.get(
    api('/analytics/kpi'),
    withAuth(
      ({ ctx, url }) => {
        const warehouseId = url.searchParams.get('warehouseId')
        const sellerId = url.searchParams.get('sellerId')
        const period = Number(url.searchParams.get('period') ?? 30)
        const warehouses = scopeWarehouses(ctx, warehouseId)
        const whIds = new Set(warehouses.map((w) => w.id))
        const l = lookups()
        const seed = hash([...whIds].join(',') + (sellerId ?? ''))
        const rng = createRandom(seed)
        const scale = (warehouses.length || 1) * 140 * (sellerId ? 0.3 : 1)
        const hist = history(seed, scale).slice(-period)
        // Алгоритм маршрутизации внедрён в середине периода — для сравнения «до/после»
        const switchAt = Math.floor(hist.length / 2)
        const series = hist.map((h, i) => {
          const baseline = Math.round(rng.float(180, 230))
          const optimized = i >= switchAt ? Math.round(baseline * rng.float(0.68, 0.78)) : baseline
          return {
            date: new Date(h.date).toISOString(),
            orders: h.value,
            processingHours:
              Math.round(rng.float(i >= switchAt ? 3.1 : 4.2, i >= switchAt ? 4.4 : 5.8) * 10) / 10,
            routeBaseline: baseline,
            routeOptimized: optimized,
            utilization: Math.round(rng.float(62, 91)),
            errors: rng.int(0, Math.max(1, Math.round(h.value / 120))),
          }
        })
        const sum = (f: (p: (typeof series)[number]) => number) => series.reduce((a, p) => a + f(p), 0)
        const orders = sum((p) => p.orders)
        const avgBase = sum((p) => p.routeBaseline) / series.length
        const avgOpt = sum((p) => p.routeOptimized) / series.length
        const sellerIds = new Set(
          db.data.contracts.filter((c) => c.warehouseIds.some((w) => whIds.has(w))).map((c) => c.sellerId),
        )
        const bySeller = [...sellerIds]
          .filter((id) => !sellerId || id === sellerId)
          .map((id) => ({
            sellerId: id,
            name: l.orgs.get(id)?.name ?? id,
            orders: Math.round(orders * rng.float(0.08, 0.35)),
            avgProcessingHours: Math.round(rng.float(3, 5.5) * 10) / 10,
            errorRate: Math.round(rng.float(0.1, 1.4) * 10) / 10,
          }))
          .sort((a, b) => b.orders - a.orders)
        const report: KpiReport = {
          period,
          totals: {
            orders,
            throughputPerDay: Math.round(orders / series.length),
            avgProcessingHours: Math.round((sum((p) => p.processingHours) / series.length) * 10) / 10,
            avgRouteBaseline: Math.round(avgBase),
            avgRouteOptimized: Math.round(avgOpt),
            routeReductionPct: Math.round((1 - avgOpt / avgBase) * 1000) / 10,
            utilization: Math.round(sum((p) => p.utilization) / series.length),
            errorRate: Math.round((sum((p) => p.errors) / Math.max(1, orders)) * 1000) / 10,
          },
          series,
          bySeller,
        }
        return HttpResponse.json(report)
      },
      { roles: MANAGEMENT },
    ),
  ),
]
