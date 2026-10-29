import { http, HttpResponse } from 'msw'
import type { Role } from '@/modules/auth/types'
import { TASK_TYPES } from '@/modules/tasks/labels'
import type {
  LookupResult,
  PickCandidate,
  RouteAlgorithm,
  RoutePoint,
  RoutePreview,
  TaskDetails,
  TaskListItem,
  TaskPriority,
  TaskStatus,
  TaskStep,
  TaskSummary,
  TaskType,
  WorkerLoad,
} from '@/modules/tasks/types'
import { DAY, HOUR } from '../db/seed'
import { db } from '../db/store'
import type { LocationRecord, OrderRecord } from '../db/types'
import { localCollection } from '../lib/collection'
import { readJson, countBy, listParams, paginate, param, problem, sortItems, withAuth, type MockContext } from '../lib/http'
import { lookups } from '../lib/mappers'
import { createRandom } from '../lib/random'
import { visibleWarehouseIds } from '../lib/scope'
import { api } from './api'

const FC_OFFICE: Role[] = ['fc_owner', 'fc_manager', 'fc_supervisor']
const WORKERS: Role[] = ['fc_receiver', 'fc_picker', 'fc_packer', 'fc_supervisor']

interface TaskItem {
  productId: string
  locationId: string
  quantity: number
}

interface TaskRecord {
  id: string
  number: string
  type: TaskType
  status: TaskStatus
  priority: TaskPriority
  warehouseId: string
  organizationId: string
  assigneeId?: string
  createdAt: string
  startedAt?: string
  finishedAt?: string
  orderIds: string[]
  inboundId?: string
  algorithm: RouteAlgorithm
  items: TaskItem[]
  /** Количество, отсканированное по каждой позиции (в порядке items) */
  progress: number[]
}

/* ---------- Геометрия маршрута ---------- */

const dist = (a: { x: number; y: number }, b: { x: number; y: number }) =>
  Math.abs(a.x - b.x) + Math.abs(a.y - b.y)

/** Базовый порядок — по адресам ячеек, как в бумажном листе отбора */
function baselineOrder<T extends { loc: LocationRecord }>(items: T[]): T[] {
  return [...items].sort((a, b) => a.loc.code.localeCompare(b.loc.code))
}

/** Оптимизированный: ближайший сосед от входа + улучшение 2-opt (манхэттенская метрика по проходам) */
function optimizedOrder<T extends { loc: LocationRecord }>(items: T[]): T[] {
  const rest = [...items]
  const route: T[] = []
  let cur = { x: 0, y: 0 }
  while (rest.length) {
    let best = 0
    rest.forEach((it, i) => {
      if (dist(cur, it.loc) < dist(cur, rest[best]!.loc)) best = i
    })
    const [next] = rest.splice(best, 1)
    route.push(next!)
    cur = next!.loc
  }
  const total = (r: T[]) => routeLength(r.map((it) => it.loc))
  let improved = true
  while (improved && route.length > 3) {
    improved = false
    for (let i = 0; i < route.length - 1; i++) {
      for (let k = i + 1; k < route.length; k++) {
        const candidate = [...route.slice(0, i), ...route.slice(i, k + 1).reverse(), ...route.slice(k + 1)]
        if (total(candidate) + 0.01 < total(route)) {
          route.splice(0, route.length, ...candidate)
          improved = true
        }
      }
    }
  }
  return route
}

function routeLength(points: { x: number; y: number }[]): number {
  let cur = { x: 0, y: 0 }
  let sum = 0
  for (const p of points) {
    sum += dist(cur, p)
    cur = p
  }
  return Math.round((sum + dist(cur, { x: 0, y: 0 })) * 10) / 10
}

/* ---------- Генерация заданий ---------- */

function locationsOf(warehouseId: string, zoneCode: string): LocationRecord[] {
  const zone = db.data.zones.find((z) => z.warehouseId === warehouseId && z.code === zoneCode)
  return zone ? db.data.locations.filter((l) => l.zoneId === zone.id) : []
}

function pickItems(orders: OrderRecord[]): TaskItem[] {
  const map = new Map<string, TaskItem>()
  for (const order of orders) {
    for (const line of order.lines) {
      const stock =
        db.data.stock.find(
          (s) => s.productId === line.productId && s.warehouseId === order.warehouseId && s.quantity > 0,
        ) ?? db.data.stock.find((s) => s.productId === line.productId && s.warehouseId === order.warehouseId)
      const locationId = stock?.locationId ?? locationsOf(order.warehouseId, 'PCK')[0]?.id ?? ''
      const key = `${line.productId}|${locationId}`
      const item = map.get(key)
      if (item) item.quantity += line.quantity
      else map.set(key, { productId: line.productId, locationId, quantity: line.quantity })
    }
  }
  return [...map.values()]
}

function sortItemsBy(items: TaskItem[], algorithm: RouteAlgorithm): TaskItem[] {
  const locs = new Map(db.data.locations.map((l) => [l.id, l]))
  const wrapped = items
    .filter((it) => locs.has(it.locationId))
    .map((it) => ({ it, loc: locs.get(it.locationId)! }))
  const ordered = algorithm === 'optimized' ? optimizedOrder(wrapped) : baselineOrder(wrapped)
  return [...ordered.map((w) => w.it), ...items.filter((it) => !locs.has(it.locationId))]
}

function seedTasks(): TaskRecord[] {
  const rng = createRandom(4242)
  const now = Date.now()
  const out: TaskRecord[] = []
  const fcOrgs = db.data.organizations.filter((o) => o.type === 'fulfillment')
  let seq = 1

  for (const org of fcOrgs) {
    const workers = db.data.users.filter((u) =>
      u.memberships.some((m) => m.organizationId === org.id && WORKERS.includes(m.role)),
    )
    const roleOf = (userId: string) =>
      workers.find((w) => w.id === userId)!.memberships.find((m) => m.organizationId === org.id)!.role
    const pickFor = (type: TaskType) => {
      const pool = workers.filter((w) => TASK_TYPES[type].roles.includes(roleOf(w.id)))
      return pool.length ? rng.pick(pool).id : undefined
    }
    const warehouses = db.data.warehouses.filter((w) => w.organizationId === org.id)
    for (const wh of warehouses) {
      const push = (
        rec: Omit<
          TaskRecord,
          'id' | 'number' | 'organizationId' | 'warehouseId' | 'progress' | 'algorithm'
        > & { progress?: number[] },
      ) => {
        const id = `tsk_${seq}`
        out.push({
          id,
          number: `T-${String(100000 + seq * 7).slice(-6)}`,
          organizationId: org.id,
          warehouseId: wh.id,
          algorithm: 'optimized',
          ...rec,
          progress: rec.progress ?? rec.items.map(() => 0),
          items: sortItemsBy(rec.items, 'optimized'),
        })
        seq++
      }
      const orders = db.data.orders.filter((o) => o.warehouseId === wh.id)

      // Отбор в работе — батчами по 2–4 заказа
      const picking = orders.filter((o) => o.status === 'picking')
      for (let i = 0; i < picking.length; i += 3) {
        const batch = picking.slice(i, i + 3)
        const items = pickItems(batch)
        const norm = Math.max(
          3,
          Math.round(items.reduce((a, it) => a + it.quantity, 0) * 0.6 + items.length * 0.7),
        )
        const started = now - Math.round(norm * rng.float(0.3, 1.6) * 60_000)
        push({
          type: 'picking',
          status: 'in_progress',
          priority: batch.some((o) => Date.parse(o.shipBy) < now + 4 * HOUR) ? 'high' : 'normal',
          assigneeId: pickFor('picking'),
          createdAt: new Date(started - rng.int(5, 40) * 60_000).toISOString(),
          startedAt: new Date(started).toISOString(),
          orderIds: batch.map((o) => o.id),
          items,
          progress: items.map((it) => rng.int(0, it.quantity)),
        })
      }
      // Упаковка собранных
      for (const order of orders.filter((o) => o.status === 'picked')) {
        const assigned = rng.bool(0.5)
        push({
          type: 'packing',
          status: assigned ? 'assigned' : 'new',
          priority: Date.parse(order.shipBy) < now + 4 * HOUR ? 'high' : 'normal',
          assigneeId: assigned ? pickFor('packing') : undefined,
          createdAt: new Date(now - rng.int(10, 120) * 60_000).toISOString(),
          orderIds: [order.id],
          items: pickItems([order]).map((it) => ({
            ...it,
            locationId: locationsOf(wh.id, 'PAK')[0]?.id ?? it.locationId,
          })),
        })
      }
      // История отбора: последние отгруженные
      const shipped = orders
        .filter((o) => o.status === 'shipped' || o.status === 'packed')
        .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
        .slice(0, 24)
      for (let i = 0; i < shipped.length; i += 2) {
        const batch = shipped.slice(i, i + 2)
        const items = pickItems(batch)
        const units = items.reduce((s, it) => s + it.quantity, 0)
        const finished = now - rng.int(1, 36) * HOUR + rng.int(0, 59) * 60_000
        const minutes = Math.max(4, Math.round(units * rng.float(0.7, 1.6) + items.length * 0.8))
        push({
          type: 'picking',
          status: 'done',
          priority: 'normal',
          assigneeId: pickFor('picking'),
          createdAt: new Date(finished - (minutes + rng.int(5, 60)) * 60_000).toISOString(),
          startedAt: new Date(finished - minutes * 60_000).toISOString(),
          finishedAt: new Date(finished).toISOString(),
          orderIds: batch.map((o) => o.id),
          items,
          progress: items.map((it) => it.quantity),
        })
      }
      // Приёмка и размещение по поставкам
      const rcv = locationsOf(wh.id, 'RCV')
      const str = locationsOf(wh.id, 'STR')
      for (const inbound of db.data.inbound.filter((r) => r.warehouseId === wh.id)) {
        if (!['arrived', 'receiving', 'received'].includes(inbound.status)) continue
        const isPutaway = inbound.status === 'received'
        const items = inbound.lines.map((line, idx) => ({
          productId: line.productId,
          locationId: isPutaway ? (str[(idx * 7) % Math.max(1, str.length)]?.id ?? '') : (rcv[0]?.id ?? ''),
          quantity: isPutaway ? Math.max(1, line.receivedQty - line.defectQty) : line.expectedQty,
        }))
        const type: TaskType = isPutaway ? 'putaway' : 'receiving'
        const inWork = inbound.status === 'receiving'
        push({
          type,
          status: inWork ? 'in_progress' : rng.bool(0.4) ? 'assigned' : 'new',
          priority: 'normal',
          assigneeId: inWork || rng.bool(0.4) ? pickFor(type) : undefined,
          createdAt: new Date(now - rng.int(20, 300) * 60_000).toISOString(),
          startedAt: inWork ? new Date(now - rng.int(20, 120) * 60_000).toISOString() : undefined,
          orderIds: [],
          inboundId: inbound.id,
          items,
          progress: inWork ? items.map((it) => rng.int(0, it.quantity)) : undefined,
        })
      }
      // Инвентаризация и перемещения
      const stockRows = db.data.stock.filter((s) => s.warehouseId === wh.id && s.quantity > 0)
      if (stockRows.length) {
        const sample = rng.sample(stockRows, 6)
        push({
          type: 'inventory',
          status: 'new',
          priority: 'normal',
          createdAt: new Date(now - rng.int(1, 6) * HOUR).toISOString(),
          orderIds: [],
          items: sample.map((s) => ({
            productId: s.productId,
            locationId: s.locationId,
            quantity: s.quantity,
          })),
        })
        const mv = rng.sample(stockRows, 2)
        push({
          type: 'move',
          status: 'assigned',
          priority: 'normal',
          assigneeId: pickFor('move'),
          createdAt: new Date(now - rng.int(1, 3) * HOUR).toISOString(),
          orderIds: [],
          items: mv.map((s) => ({
            productId: s.productId,
            locationId: s.locationId,
            quantity: Math.min(s.quantity, rng.int(2, 10)),
          })),
        })
      }
    }
  }
  // Демо-комплектовщику (picker@demo.ru) — гарантированно несколько заданий
  const demo = db.data.users.find((u) => u.email === 'picker@demo.ru')
  if (demo) {
    const orgId = demo.memberships[0]?.organizationId
    out
      .filter(
        (t) =>
          t.organizationId === orgId &&
          t.status !== 'done' &&
          (t.type === 'picking' || t.type === 'move' || t.type === 'inventory'),
      )
      .slice(0, 4)
      .forEach((t) => {
        t.assigneeId = demo.id
        if (t.status === 'new') t.status = 'assigned'
      })
  }
  void DAY
  return out
}

const tasks = localCollection<TaskRecord>('tasks', seedTasks)

/* ---------- Представления ---------- */

const NORM_PER_UNIT: Record<TaskType, number> = {
  receiving: 0.25,
  putaway: 0.3,
  picking: 0.6,
  packing: 0.5,
  move: 0.5,
  inventory: 0.4,
}

function normMinutes(t: TaskRecord) {
  if (t.type === 'inventory') return Math.max(5, t.items.length * 4)
  const units = t.items.reduce((s, it) => s + it.quantity, 0)
  return Math.max(3, Math.round(units * NORM_PER_UNIT[t.type] + t.items.length * 0.7))
}

function reference(t: TaskRecord): string {
  if (t.inboundId) return db.data.inbound.find((r) => r.id === t.inboundId)?.number ?? '—'
  if (t.orderIds.length) {
    const nums = t.orderIds.map((id) => db.data.orders.find((o) => o.id === id)?.number ?? id)
    return nums.length > 2 ? `${nums.slice(0, 2).join(', ')} +${nums.length - 2}` : nums.join(', ')
  }
  return t.type === 'inventory' ? 'Плановая' : 'Подпитка зоны отбора'
}

function toItem(t: TaskRecord, now = Date.now()): TaskListItem {
  const l = lookups()
  const user = t.assigneeId ? db.data.users.find((u) => u.id === t.assigneeId) : undefined
  const units = t.items.reduce((s, it) => s + it.quantity, 0)
  const norm = normMinutes(t)
  const end = t.finishedAt ? Date.parse(t.finishedAt) : now
  const elapsed = t.startedAt ? Math.round((end - Date.parse(t.startedAt)) / 60_000) : undefined
  return {
    id: t.id,
    number: t.number,
    type: t.type,
    status: t.status,
    priority: t.priority,
    warehouseId: t.warehouseId,
    warehouseName: l.warehouses.get(t.warehouseId)?.name ?? '',
    assigneeId: t.assigneeId,
    assigneeName: user?.fullName,
    createdAt: t.createdAt,
    startedAt: t.startedAt,
    finishedAt: t.finishedAt,
    reference: reference(t),
    ordersCount: t.orderIds.length,
    linesCount: t.items.length,
    units,
    doneUnits: t.progress.reduce((s, v) => s + v, 0),
    normMinutes: norm,
    elapsedMinutes: elapsed,
    isLate: elapsed !== undefined && elapsed > norm * 1.2,
  }
}

function toDetails(t: TaskRecord): TaskDetails {
  const l = lookups()
  const locs = new Map(db.data.locations.map((loc) => [loc.id, loc]))
  const zones = new Map(db.data.zones.map((z) => [z.id, z]))
  let cur = { x: 0, y: 0 }
  const steps: TaskStep[] = t.items.map((it, index) => {
    const loc = locs.get(it.locationId)
    const product = l.products.get(it.productId)
    const d = loc ? dist(cur, loc) : 0
    if (loc) cur = loc
    return {
      index,
      locationId: it.locationId,
      locationCode: loc?.code ?? '—',
      zoneCode: loc ? (zones.get(loc.zoneId)?.code ?? '') : '',
      productId: it.productId,
      productName: product?.name ?? '—',
      sku: product?.sku ?? '',
      barcode: product?.barcode ?? '',
      quantity: it.quantity,
      done: t.progress[index] ?? 0,
      distance: Math.round(d * 10) / 10,
    }
  })
  const points = t.items.map((it) => locs.get(it.locationId)).filter((x): x is LocationRecord => !!x)
  const wrapped = points.map((loc) => ({ loc }))
  return {
    ...toItem(t),
    steps,
    algorithm: t.algorithm,
    routeDistance: routeLength(points),
    baselineDistance: routeLength(baselineOrder(wrapped).map((w) => w.loc)),
    eligibleRoles: TASK_TYPES[t.type].roles,
  }
}

function scopedTasks(ctx: MockContext, warehouseId?: string | null) {
  const visible = visibleWarehouseIds(ctx)
  return tasks.all.filter(
    (t) => (!visible || visible.has(t.warehouseId)) && (!warehouseId || t.warehouseId === warehouseId),
  )
}

function roleIn(userId: string, orgId: string): Role | undefined {
  return db.data.users.find((u) => u.id === userId)?.memberships.find((m) => m.organizationId === orgId)?.role
}

function workersOf(orgId: string) {
  return db.data.users.filter((u) =>
    u.memberships.some((m) => m.organizationId === orgId && WORKERS.includes(m.role)),
  )
}

const startOfDay = () => {
  const d = new Date()
  d.setHours(0, 0, 0, 0)
  return d.getTime()
}

function workerLoads(orgId: string, list: TaskRecord[]): WorkerLoad[] {
  const sod = startOfDay()
  return workersOf(orgId).map((u) => {
    const mine = list.filter((t) => t.assigneeId === u.id)
    const done = mine.filter((t) => t.status === 'done' && t.finishedAt && Date.parse(t.finishedAt) >= sod)
    const finished = mine.filter((t) => t.status === 'done' && t.startedAt && t.finishedAt)
    const avg = finished.length
      ? finished.reduce((s, t) => s + (Date.parse(t.finishedAt!) - Date.parse(t.startedAt!)) / 60_000, 0) /
        finished.length
      : 0
    return {
      id: u.id,
      fullName: u.fullName,
      role: roleIn(u.id, orgId)!,
      active: mine.filter((t) => t.status === 'assigned' || t.status === 'in_progress').length,
      doneToday: done.length,
      unitsToday: done.reduce((s, t) => s + t.items.reduce((a, it) => a + it.quantity, 0), 0),
      avgMinutes: Math.round(avg),
    }
  })
}

function orderUnits(o: OrderRecord) {
  return o.lines.reduce((s, line) => s + line.quantity, 0)
}

function candidates(ctx: MockContext, warehouseId?: string | null): OrderRecord[] {
  const visible = visibleWarehouseIds(ctx)
  const busy = new Set(tasks.all.filter((t) => t.status !== 'cancelled').flatMap((t) => t.orderIds))
  return db.data.orders.filter(
    (o) =>
      o.status === 'reserved' &&
      !busy.has(o.id) &&
      (!visible || visible.has(o.warehouseId)) &&
      (!warehouseId || o.warehouseId === warehouseId),
  )
}

function preview(items: TaskItem[]): RoutePreview {
  const locs = new Map(db.data.locations.map((l) => [l.id, l]))
  const wrapped = items
    .filter((it) => locs.has(it.locationId))
    .map((it) => ({ loc: locs.get(it.locationId)! }))
  const pts = (list: { loc: LocationRecord }[]): RoutePoint[] =>
    list.map(({ loc }) => ({ code: loc.code, x: loc.x, y: loc.y }))
  const opt = optimizedOrder(wrapped)
  const base = baselineOrder(wrapped)
  const units = items.reduce((s, it) => s + it.quantity, 0)
  const optDist = routeLength(opt.map((w) => w.loc))
  return {
    optimized: { distance: optDist, points: pts(opt) },
    baseline: { distance: routeLength(base.map((w) => w.loc)), points: pts(base) },
    stepsCount: items.length,
    units,
    // 1,2 м/с ходьба + 6 с на единицу + 10 с на точку
    estimatedMinutes: Math.max(1, Math.round((optDist / 1.2 + units * 6 + items.length * 10) / 60)),
  }
}

function ownTask(ctx: MockContext, id: string): TaskRecord | Response {
  const t = tasks.find(id)
  if (!t || t.assigneeId !== ctx.user.id) return problem(404, 'Задание не найдено')
  return t
}

export const taskHandlers = [
  http.get(
    api('/tasks/summary'),
    withAuth(
      ({ ctx, url }) => {
        const q = url.searchParams
        const list = scopedTasks(ctx, q.get('warehouseId')).filter(
          (t) => !q.get('type') || t.type === q.get('type'),
        )
        const items = list.map((t) => toItem(t))
        const sod = startOfDay()
        const done = items.filter(
          (t) => t.status === 'done' && t.finishedAt && Date.parse(t.finishedAt) >= sod,
        )
        const finished = items.filter((t) => t.status === 'done' && t.elapsedMinutes !== undefined)
        const summary: TaskSummary = {
          total: items.length,
          active: items.filter((t) => ['new', 'assigned', 'in_progress'].includes(t.status)).length,
          late: items.filter((t) => t.status === 'in_progress' && t.isLate).length,
          unassigned: items.filter((t) => t.status === 'new').length,
          doneToday: done.length,
          avgMinutes: finished.length
            ? Math.round(finished.reduce((s, t) => s + t.elapsedMinutes!, 0) / finished.length)
            : 0,
          byStatus: countBy(items, (t) => t.status),
          byType: countBy(
            items.filter((t) => t.status !== 'done' && t.status !== 'cancelled'),
            (t) => t.type,
          ),
        }
        return HttpResponse.json(summary)
      },
      { roles: FC_OFFICE },
    ),
  ),

  http.get(
    api('/tasks/workers'),
    withAuth(
      ({ ctx, url }) => {
        if (!ctx.org) return HttpResponse.json([])
        return HttpResponse.json(
          workerLoads(ctx.org.id, scopedTasks(ctx, url.searchParams.get('warehouseId'))),
        )
      },
      { roles: FC_OFFICE },
    ),
  ),

  http.get(
    api('/tasks'),
    withAuth(
      ({ ctx, url }) => {
        const q = url.searchParams
        const params = listParams(url, { sortField: 'createdAt', sortOrder: 'desc' })
        const status = q.get('status')
        const items = scopedTasks(ctx, q.get('warehouseId'))
          .filter(
            (t) =>
              (!q.get('type') || t.type === q.get('type')) &&
              (!q.get('assigneeId') || t.assigneeId === q.get('assigneeId')),
          )
          .map((t) => toItem(t))
          .filter((t) => {
            if (status === 'active' && !['new', 'assigned', 'in_progress'].includes(t.status)) return false
            if (status === 'late' && !(t.status === 'in_progress' && t.isLate)) return false
            if (status && !['active', 'late'].includes(status) && t.status !== status) return false
            const s = params.search
            return (
              !s ||
              t.number.toLowerCase().includes(s) ||
              t.reference.toLowerCase().includes(s) ||
              (t.assigneeName?.toLowerCase().includes(s) ?? false)
            )
          })
        return HttpResponse.json(
          paginate(sortItems(items, params.sortField, params.sortOrder), params.page, params.pageSize),
        )
      },
      { roles: FC_OFFICE },
    ),
  ),

  http.get(
    api('/tasks/:id'),
    withAuth(({ ctx, params }) => {
      const t = tasks.find(param(params, 'id'))
      const visible = visibleWarehouseIds(ctx)
      if (!t || (visible && !visible.has(t.warehouseId))) return problem(404, 'Задание не найдено')
      if (WORKERS.includes(ctx.role) && ctx.role !== 'fc_supervisor' && t.assigneeId !== ctx.user.id)
        return problem(404, 'Задание не найдено')
      return HttpResponse.json(toDetails(t))
    }),
  ),

  http.post(
    api('/tasks/auto-assign'),
    withAuth(
      async ({ ctx, request }) => {
        const body = (await request.json().catch(() => ({}))) as { warehouseId?: string }
        const list = scopedTasks(ctx, body.warehouseId)
        const loads = new Map(workerLoads(ctx.org!.id, list).map((w) => [w.id, w]))
        let assigned = 0
        const queue = list
          .filter((t) => t.status === 'new')
          .sort((a, b) =>
            a.priority === b.priority
              ? a.createdAt.localeCompare(b.createdAt)
              : a.priority === 'high'
                ? -1
                : 1,
          )
        for (const t of queue) {
          const pool = [...loads.values()].filter(
            (w) => TASK_TYPES[t.type].roles.includes(w.role) && w.active < 3,
          )
          if (!pool.length) continue
          // Наименее загруженный, при равенстве — профильный, а не бригадир
          pool.sort(
            (a, b) =>
              a.active - b.active || Number(a.role === 'fc_supervisor') - Number(b.role === 'fc_supervisor'),
          )
          const w = pool[0]!
          tasks.upsert({ ...t, assigneeId: w.id, status: 'assigned' })
          w.active++
          assigned++
        }
        return HttpResponse.json({ assigned })
      },
      { roles: FC_OFFICE },
    ),
  ),

  http.post(
    api('/tasks/:id/assign'),
    withAuth(
      async ({ ctx, params, request }) => {
        const t = tasks.find(param(params, 'id'))
        const visible = visibleWarehouseIds(ctx)
        if (!t || (visible && !visible.has(t.warehouseId))) return problem(404, 'Задание не найдено')
        if (t.status === 'done' || t.status === 'cancelled') return problem(409, 'Задание уже закрыто')
        const { userId } = (await readJson(request)) as { userId: string | null }
        if (userId) {
          const role = roleIn(userId, t.organizationId)
          if (!role || !TASK_TYPES[t.type].roles.includes(role)) {
            return problem(
              422,
              'Сотрудник не может выполнять это задание',
              `Нужна роль: ${TASK_TYPES[t.type].label.toLowerCase()}`,
            )
          }
        } else if (t.status === 'in_progress') {
          return problem(409, 'Нельзя снять исполнителя с задания в работе')
        }
        const next = tasks.upsert({
          ...t,
          assigneeId: userId ?? undefined,
          status: t.status === 'in_progress' ? 'in_progress' : userId ? 'assigned' : 'new',
        })
        return HttpResponse.json(toDetails(next))
      },
      { roles: FC_OFFICE },
    ),
  ),

  http.post(
    api('/tasks/:id/cancel'),
    withAuth(
      ({ ctx, params }) => {
        const t = tasks.find(param(params, 'id'))
        const visible = visibleWarehouseIds(ctx)
        if (!t || (visible && !visible.has(t.warehouseId))) return problem(404, 'Задание не найдено')
        if (t.status === 'done') return problem(409, 'Выполненное задание нельзя отменить')
        if (t.type === 'picking') {
          for (const id of t.orderIds) {
            const o = db.data.orders.find((x) => x.id === id)
            if (o && o.status === 'picking') db.upsert('orders', { ...o, status: 'reserved' })
          }
        }
        return HttpResponse.json(toDetails(tasks.upsert({ ...t, status: 'cancelled' })))
      },
      { roles: FC_OFFICE },
    ),
  ),

  /* ---------- Комплектация ---------- */

  http.get(
    api('/picking/candidates'),
    withAuth(
      ({ ctx, url }) => {
        const l = lookups()
        const now = Date.now()
        const list: PickCandidate[] = candidates(ctx, url.searchParams.get('warehouseId'))
          .sort((a, b) => a.shipBy.localeCompare(b.shipBy))
          .map((o) => ({
            id: o.id,
            number: o.number,
            channel: o.channel,
            sellerName: l.orgs.get(o.sellerId)?.name ?? '',
            warehouseId: o.warehouseId,
            shipBy: o.shipBy,
            isOverdue: Date.parse(o.shipBy) < now,
            linesCount: o.lines.length,
            units: orderUnits(o),
          }))
        return HttpResponse.json(list)
      },
      { roles: FC_OFFICE },
    ),
  ),

  http.post(
    api('/picking/preview'),
    withAuth(
      async ({ ctx, request }) => {
        const { orderIds = [] } = (await readJson(request)) as { orderIds?: string[] }
        const pool = new Map(candidates(ctx).map((o) => [o.id, o]))
        const orders = orderIds.map((id) => pool.get(id)).filter((o): o is OrderRecord => !!o)
        return HttpResponse.json(preview(pickItems(orders)))
      },
      { roles: FC_OFFICE },
    ),
  ),

  http.post(
    api('/picking/batches'),
    withAuth(
      async ({ ctx, request }) => {
        const { orderIds = [], algorithm } = (await readJson(request)) as {
          orderIds?: string[]
          algorithm: RouteAlgorithm
        }
        if (!orderIds.length)
          return problem(422, 'Выберите заказы', 'Отметьте хотя бы один заказ для формирования батча.')
        const pool = new Map(candidates(ctx).map((o) => [o.id, o]))
        const orders = orderIds.map((id) => pool.get(id)).filter((o): o is OrderRecord => !!o)
        if (!orders.length || orders.length !== orderIds.length) {
          return problem(
            409,
            'Часть заказов уже в работе',
            'Обновите список заказов и сформируйте батч заново.',
          )
        }
        if (new Set(orders.map((o) => o.warehouseId)).size > 1)
          return problem(422, 'Заказы с разных складов', 'Батч формируется в пределах одного склада.')
        if (orders.length > 12)
          return problem(422, 'Слишком большой батч', 'Не более 12 заказов в одном листе отбора.')
        const items = sortItemsBy(pickItems(orders), algorithm)
        const n = tasks.all.length + 1
        const now = Date.now()
        const rec: TaskRecord = {
          id: `tsk_b${now}`,
          number: `T-${String(100000 + n * 7 + 3).slice(-6)}`,
          type: 'picking',
          status: 'new',
          priority: orders.some((o) => Date.parse(o.shipBy) < now + 4 * HOUR) ? 'high' : 'normal',
          warehouseId: orders[0]!.warehouseId,
          organizationId: ctx.org!.id,
          createdAt: new Date(now).toISOString(),
          orderIds: orders.map((o) => o.id),
          algorithm,
          items,
          progress: items.map(() => 0),
        }
        tasks.upsert(rec)
        for (const o of orders) db.upsert('orders', { ...o, status: 'picking' })
        return HttpResponse.json(toDetails(rec), { status: 201 })
      },
      { roles: FC_OFFICE },
    ),
  ),

  /* ---------- ТСД сотрудника ---------- */

  http.get(
    api('/work/tasks'),
    withAuth(({ ctx }) => {
      const sod = startOfDay()
      const mine = tasks.all
        .filter((t) => t.assigneeId === ctx.user.id)
        .filter(
          (t) =>
            t.status === 'assigned' ||
            t.status === 'in_progress' ||
            (t.status === 'done' && t.finishedAt && Date.parse(t.finishedAt) >= sod),
        )
        .map((t) => toItem(t))
        .sort((a, b) => {
          const rank = (s: TaskStatus) => (s === 'in_progress' ? 0 : s === 'assigned' ? 1 : 2)
          return (
            rank(a.status) - rank(b.status) ||
            (a.priority === b.priority
              ? a.createdAt.localeCompare(b.createdAt)
              : a.priority === 'high'
                ? -1
                : 1)
          )
        })
      return HttpResponse.json(mine)
    }),
  ),

  http.post(
    api('/work/tasks/:id/start'),
    withAuth(({ ctx, params }) => {
      const t = ownTask(ctx, param(params, 'id'))
      if (t instanceof Response) return t
      if (t.status === 'done' || t.status === 'cancelled') return problem(409, 'Задание закрыто')
      const busy = tasks.all.find(
        (x) => x.assigneeId === ctx.user.id && x.status === 'in_progress' && x.id !== t.id,
      )
      if (busy) return problem(409, 'Сначала завершите текущее задание', busy.number)
      const next =
        t.status === 'in_progress'
          ? t
          : tasks.upsert({ ...t, status: 'in_progress', startedAt: new Date().toISOString() })
      return HttpResponse.json(toDetails(next))
    }),
  ),

  http.post(
    api('/work/tasks/:id/scan'),
    withAuth(async ({ ctx, params, request }) => {
      const t = ownTask(ctx, param(params, 'id'))
      if (t instanceof Response) return t
      if (t.status !== 'in_progress') return problem(409, 'Задание не в работе')
      const { code } = (await readJson(request)) as { code: string }
      const value = code.trim()
      const details = toDetails(t)
      const step = details.steps.find((s) => s.done < s.quantity)
      if (!step) return problem(409, 'Все позиции уже собраны')
      if (value.toUpperCase() === step.locationCode.toUpperCase()) {
        return HttpResponse.json({ task: details, message: `Ячейка ${step.locationCode} подтверждена` })
      }
      if (value !== step.barcode) {
        const other = db.data.products.find((p) => p.barcode === value)
        return problem(
          422,
          other ? 'Не тот товар' : 'Штрихкод не распознан',
          other
            ? `Отсканирован «${other.name}», ожидается «${step.productName}» (${step.sku}).`
            : `Ожидается ${step.sku} в ячейке ${step.locationCode}.`,
        )
      }
      const progress = [...t.progress]
      progress[step.index] = (progress[step.index] ?? 0) + 1
      const next = tasks.upsert({ ...t, progress })
      const left = step.quantity - progress[step.index]!
      return HttpResponse.json({
        task: toDetails(next),
        message: left > 0 ? `${step.sku}: ещё ${left} шт` : `Позиция ${step.sku} собрана`,
      })
    }),
  ),

  http.post(
    api('/work/tasks/:id/complete'),
    withAuth(({ ctx, params }) => {
      const t = ownTask(ctx, param(params, 'id'))
      if (t instanceof Response) return t
      if (t.status !== 'in_progress') return problem(409, 'Задание не в работе')
      const short = t.items.findIndex((it, i) => (t.progress[i] ?? 0) < it.quantity)
      if (short >= 0 && t.type !== 'inventory') {
        return problem(
          422,
          'Не все позиции выполнены',
          'Досканируйте товары или сообщите бригадиру о недостаче.',
        )
      }
      const now = new Date().toISOString()
      const next = tasks.upsert({ ...t, status: 'done', finishedAt: now })
      if (t.type === 'picking') {
        for (const id of t.orderIds) {
          const o = db.data.orders.find((x) => x.id === id)
          if (!o) continue
          db.upsert('orders', { ...o, status: 'picked' })
          const pak = locationsOf(o.warehouseId, 'PAK')[0]?.id
          const items = pickItems([o]).map((it) => ({ ...it, locationId: pak ?? it.locationId }))
          tasks.upsert({
            id: `tsk_p${o.id}`,
            number: `T-${String(Date.now()).slice(-6)}`,
            type: 'packing',
            status: 'new',
            priority: t.priority,
            warehouseId: o.warehouseId,
            organizationId: t.organizationId,
            createdAt: now,
            orderIds: [o.id],
            algorithm: 'optimized',
            items,
            progress: items.map(() => 0),
          })
        }
      }
      return HttpResponse.json(toDetails(next))
    }),
  ),

  http.get(
    api('/work/lookup'),
    withAuth(({ ctx, url }) => {
      const code = (url.searchParams.get('code') ?? '').trim()
      const visible = visibleWarehouseIds(ctx)
      const l = lookups()
      const location = db.data.locations.find(
        (loc) => loc.code.toUpperCase() === code.toUpperCase() && (!visible || visible.has(loc.warehouseId)),
      )
      if (location) {
        const zone = db.data.zones.find((z) => z.id === location.zoneId)
        const rows = db.data.stock.filter((s) => s.locationId === location.id && s.quantity > 0)
        const result: LookupResult = {
          kind: 'location',
          title: `Ячейка ${location.code}`,
          subtitle: `${zone?.name ?? ''} · ${l.warehouses.get(location.warehouseId)?.name ?? ''} · ярус ${location.level}`,
          rows: rows.map((s) => {
            const p = l.products.get(s.productId)
            return {
              id: s.id,
              primary: p?.name ?? '—',
              secondary: `${p?.sku ?? ''} · ${l.orgs.get(s.sellerId)?.name ?? ''}`,
              quantity: s.quantity,
              reserved: s.reserved,
            }
          }),
        }
        return HttpResponse.json(result)
      }
      const product = db.data.products.find(
        (p) => p.barcode === code || p.sku.toUpperCase() === code.toUpperCase(),
      )
      if (product) {
        const locs = new Map(db.data.locations.map((loc) => [loc.id, loc]))
        const rows = db.data.stock.filter(
          (s) => s.productId === product.id && s.quantity > 0 && (!visible || visible.has(s.warehouseId)),
        )
        const result: LookupResult = {
          kind: 'product',
          title: product.name,
          subtitle: `${product.sku} · ${product.barcode} · ${l.orgs.get(product.sellerId)?.name ?? ''}`,
          rows: rows.map((s) => ({
            id: s.id,
            primary: locs.get(s.locationId)?.code ?? '—',
            secondary: l.warehouses.get(s.warehouseId)?.name ?? '',
            quantity: s.quantity,
            reserved: s.reserved,
          })),
        }
        return HttpResponse.json(result)
      }
      return problem(404, 'Ничего не найдено', `Код «${code}» не соответствует ячейке или товару.`)
    }),
  ),
]
