import { http, HttpResponse } from 'msw'
import type { Role } from '@/modules/auth/types'
import type { Employee, EmployeeInput, Shift, Skill, StaffResponse } from '@/modules/staff/types'
import { DAY } from '../db/seed'
import { db } from '../db/store'
import { localCollection } from '../lib/collection'
import { readJson, param, problem, withAuth, type MockContext } from '../lib/http'
import { lookups } from '../lib/mappers'
import { createRandom } from '../lib/random'
import { api } from './api'

const MANAGERS: Role[] = ['fc_owner', 'fc_manager', 'fc_supervisor']
const NORMS: Partial<Record<Role, number>> = { fc_receiver: 90, fc_picker: 60, fc_packer: 45 }
const ROLE_SKILLS: Partial<Record<Role, Skill[]>> = {
  fc_receiver: ['receiving', 'inventory', 'returns', 'forklift', 'marking'],
  fc_picker: ['picking', 'inventory', 'forklift'],
  fc_packer: ['packing', 'marking', 'returns'],
  fc_supervisor: ['receiving', 'picking', 'packing', 'inventory'],
}
const ZONE_FOR: Partial<Record<Role, string[]>> = {
  fc_receiver: ['receiving', 'storage', 'defect'],
  fc_picker: ['picking', 'storage'],
  fc_packer: ['packing', 'shipping'],
}

interface StaffRecord {
  id: string
  warehouseId: string
  zoneIds: string[]
  skills: Skill[]
  active: boolean
  roleOverride?: Role
  opsPerHour: number
  accuracy: number
  hiredAt: string
  /** График по дням недели, 0 — понедельник */
  shifts: Shift[]
}

function weekStart() {
  const d = new Date()
  d.setHours(0, 0, 0, 0)
  d.setDate(d.getDate() - ((d.getDay() + 6) % 7))
  return d
}

function members(orgId: string) {
  return db.data.users.flatMap((u) =>
    u.memberships.filter((m) => m.organizationId === orgId).map((m) => ({ user: u, role: m.role })),
  )
}

const profiles = localCollection<StaffRecord>('staff', () => {
  const out: StaffRecord[] = []
  for (const org of db.data.organizations.filter((o) => o.type === 'fulfillment')) {
    const warehouses = db.data.warehouses.filter((w) => w.organizationId === org.id)
    if (!warehouses.length) continue
    members(org.id).forEach(({ user, role }, i) => {
      const rng = createRandom(4000 + i * 13 + user.id.length)
      const wh = warehouses[i % warehouses.length]!
      const zones = db.data.zones.filter(
        (z) => z.warehouseId === wh.id && (ZONE_FOR[role] ?? []).includes(z.type),
      )
      const norm = NORMS[role]
      const pattern = rng.int(0, 3)
      out.push({
        id: user.id,
        warehouseId: wh.id,
        zoneIds: zones.map((z) => z.id),
        skills: rng.sample(ROLE_SKILLS[role] ?? [], rng.int(1, (ROLE_SKILLS[role] ?? []).length || 1)).sort(),
        active: true,
        opsPerHour: norm ? Math.round(norm * rng.float(0.72, 1.22)) : 0,
        accuracy: norm ? Math.round(rng.float(97.2, 99.95) * 10) / 10 : 0,
        hiredAt: new Date(Date.now() - rng.int(40, 900) * DAY).toISOString(),
        shifts: Array.from({ length: 7 }, (_, d) => {
          if (!norm) return d >= 5 ? 'off' : 'day'
          if (i % 9 === 4 && d < 5) return 'vacation'
          // 2/2 с разными сдвигами и ночные смены у части сотрудников
          const work = (d + pattern) % 4 < 2
          return work ? (pattern === 3 ? 'night' : 'day') : 'off'
        }),
      })
    })
  }
  return out
})

function toEmployee(ctx: MockContext, rec: StaffRecord): Employee | null {
  const m = members(ctx.org!.id).find((x) => x.user.id === rec.id)
  if (!m) return null
  const role = rec.roleOverride ?? m.role
  const today = (new Date().getDay() + 6) % 7
  const hour = new Date().getHours()
  const shift = rec.shifts[today]
  return {
    id: rec.id,
    fullName: m.user.fullName,
    email: m.user.email,
    phone: m.user.phone,
    role,
    warehouseId: rec.warehouseId,
    warehouseName: lookups().warehouses.get(rec.warehouseId)?.name ?? '',
    zoneIds: rec.zoneIds,
    skills: rec.skills,
    active: rec.active,
    onShift:
      rec.active &&
      ((shift === 'day' && hour >= 8 && hour < 20) || (shift === 'night' && (hour >= 20 || hour < 8))),
    opsPerHour: rec.opsPerHour,
    norm: NORMS[role] ?? 0,
    accuracy: rec.accuracy,
    hiredAt: rec.hiredAt,
    shifts: rec.shifts,
  }
}

function own(ctx: MockContext, id: string) {
  if (!ctx.org || ctx.org.type !== 'fulfillment') return null
  const rec = profiles.find(id)
  if (!rec || !members(ctx.org.id).some((m) => m.user.id === id)) return null
  return rec
}

export const staffHandlers = [
  http.get(
    api('/staff'),
    withAuth(({ ctx, url }) => {
      if (!ctx.org || ctx.org.type !== 'fulfillment') return problem(403, 'Недоступно')
      const wh = url.searchParams.get('warehouseId')
      const ids = new Set(members(ctx.org.id).map((m) => m.user.id))
      const items = profiles.all
        .filter((r) => ids.has(r.id) && (!wh || r.warehouseId === wh))
        .map((r) => toEmployee(ctx, r))
        .filter((e): e is Employee => !!e)
        .sort((a, b) => a.role.localeCompare(b.role) || a.fullName.localeCompare(b.fullName))
      const body: StaffResponse = { weekStart: weekStart().toISOString(), items }
      return HttpResponse.json(body)
    }),
  ),

  http.put(
    api('/staff/:id'),
    withAuth(
      async ({ ctx, params, request }) => {
        const rec = own(ctx, param(params, 'id'))
        if (!rec) return problem(404, 'Сотрудник не найден')
        const input = (await readJson(request)) as EmployeeInput
        if (!db.data.warehouses.some((w) => w.id === input.warehouseId && w.organizationId === ctx.org!.id))
          return problem(422, 'Выберите склад')
        if (rec.id === ctx.user.id && !input.active) return problem(422, 'Нельзя деактивировать себя')
        const zones = new Set(
          db.data.zones.filter((z) => z.warehouseId === input.warehouseId).map((z) => z.id),
        )
        const next: StaffRecord = {
          ...rec,
          warehouseId: input.warehouseId,
          zoneIds: input.zoneIds.filter((z) => zones.has(z)),
          skills: input.skills,
          active: input.active,
          roleOverride: input.role,
        }
        profiles.upsert(next)
        return HttpResponse.json(toEmployee(ctx, next))
      },
      { roles: ['fc_owner', 'fc_manager'] },
    ),
  ),

  http.post(
    api('/staff/:id/shifts'),
    withAuth(
      async ({ ctx, params, request }) => {
        const rec = own(ctx, param(params, 'id'))
        if (!rec) return problem(404, 'Сотрудник не найден')
        const { day, shift } = (await readJson(request)) as { day: number; shift: Shift }
        if (!(day >= 0 && day < 7)) return problem(422, 'Некорректный день')
        const shifts = [...rec.shifts]
        shifts[day] = shift
        const next = { ...rec, shifts }
        profiles.upsert(next)
        return HttpResponse.json(toEmployee(ctx, next))
      },
      { roles: MANAGERS },
    ),
  ),
]
