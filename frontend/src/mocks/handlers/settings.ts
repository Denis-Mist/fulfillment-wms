import { http, HttpResponse } from 'msw'
import { cabinetForRole } from '@/modules/auth/roles'
import type { Role } from '@/modules/auth/types'
import type {
  CardRules,
  InviteInput,
  NotificationPrefs,
  OrgProfile,
  OrgSettings,
  TeamMember,
} from '@/modules/settings/types'
import { db } from '../db/store'
import { localCollection } from '../lib/collection'
import { parseBody, param, problem, withAuth, type MockContext } from '../lib/http'
import { cardRulesSchema, inviteSchema, notificationPrefsSchema, orgProfileSchema } from '../lib/schemas'
import { createRandom } from '../lib/random'
import { api } from './api'

const OWNERS: Role[] = ['fc_owner', 'fc_manager', 'seller_owner']
const BANKS = ['ПАО Сбербанк', 'АО «Т-Банк»', 'АО «Альфа-Банк»', 'Банк ВТБ (ПАО)', 'ПАО «Точка»']
const digits = (rng: ReturnType<typeof createRandom>, n: number) =>
  Array.from({ length: n }, () => rng.int(0, 9)).join('')

const settings = localCollection<OrgSettings>('settings', () =>
  db.data.organizations.map((o, i) => {
    const rng = createRandom(7000 + i)
    return {
      id: o.id,
      profile: {
        name: o.name,
        inn: o.inn,
        kpp: o.inn.length === 10 ? `${o.inn.slice(0, 4)}01001` : '',
        ogrn: `1${digits(rng, 12)}`,
        legalAddress: `г. ${o.city}, ул. ${rng.pick(['Складская', 'Промышленная', 'Логистическая', 'Индустриальная'])}, д. ${rng.int(1, 60)}`,
        contactEmail: `office@${o.type === 'fulfillment' ? 'fc' : 'shop'}${i + 1}.demo.ru`,
        contactPhone: `+7 9${digits(rng, 2)} ${digits(rng, 3)}-${digits(rng, 2)}-${digits(rng, 2)}`,
        bankName: rng.pick(BANKS),
        bik: `04452${digits(rng, 4)}`,
        account: `40702810${digits(rng, 12)}`,
        corrAccount: `30101810${digits(rng, 12)}`,
      },
      cardRules: {
        requireBarcode: true,
        requireDimensions: true,
        requireWeight: true,
        requirePhoto: false,
        requireExpiry: true,
        requireMarking: true,
        autoApprove: false,
      },
      notifications: {
        email: true,
        inboundStatus: true,
        lowStock: true,
        slaRisk: o.type === 'fulfillment',
        billing: true,
      },
    }
  }),
)

interface InviteRecord {
  id: string
  organizationId: string
  email: string
  role: Role
  invitedAt: string
  revoked?: boolean
}
const invites = localCollection<InviteRecord>('invites', () => [])

function current(ctx: MockContext) {
  if (!ctx.org) return null
  return settings.find(ctx.org.id) ?? null
}

function save(ctx: MockContext, patch: Partial<OrgSettings>) {
  const cur = current(ctx)
  if (!cur) return problem(404, 'Организация не найдена')
  const next = { ...cur, ...patch }
  settings.upsert(next)
  return HttpResponse.json(next)
}

export const settingsHandlers = [
  http.get(
    api('/settings'),
    withAuth(({ ctx }) => {
      const s = current(ctx)
      return s ? HttpResponse.json(s) : problem(404, 'Организация не найдена')
    }),
  ),

  http.put(
    api('/settings/profile'),
    withAuth(
      async ({ ctx, request }) => {
        const p = (await parseBody(request, orgProfileSchema)) as unknown as OrgProfile
        if (!p.name.trim()) return problem(422, 'Укажите название организации')
        if (!/^\d{9}$/.test(p.bik)) return problem(422, 'БИК должен состоять из 9 цифр')
        if (!/^\d{20}$/.test(p.account)) return problem(422, 'Расчётный счёт должен состоять из 20 цифр')
        if (!/^\d{20}$/.test(p.corrAccount)) return problem(422, 'Корр. счёт должен состоять из 20 цифр')
        if (!/^\S+@\S+\.\S+$/.test(p.contactEmail)) return problem(422, 'Некорректный email')
        return save(ctx, { profile: { ...p, inn: current(ctx)!.profile.inn } })
      },
      { roles: OWNERS },
    ),
  ),

  http.put(
    api('/settings/card-rules'),
    withAuth(async ({ ctx, request }) => save(ctx, { cardRules: (await parseBody(request, cardRulesSchema)) as unknown as CardRules }), {
      roles: ['fc_owner', 'fc_manager'],
    }),
  ),

  http.put(
    api('/settings/notifications'),
    withAuth(async ({ ctx, request }) =>
      save(ctx, { notifications: (await parseBody(request, notificationPrefsSchema)) as unknown as NotificationPrefs }),
    ),
  ),

  http.get(
    api('/settings/team'),
    withAuth(({ ctx }) => {
      if (!ctx.org) return HttpResponse.json([])
      const members: TeamMember[] = db.data.users.flatMap((u) =>
        u.memberships
          .filter((m) => m.organizationId === ctx.org!.id)
          .map((m) => ({
            id: u.id,
            fullName: u.fullName,
            email: u.email,
            role: m.role,
            status: 'active' as const,
          })),
      )
      const pending: TeamMember[] = invites.all
        .filter((i) => i.organizationId === ctx.org!.id && !i.revoked)
        .map((i) => ({
          id: i.id,
          fullName: '',
          email: i.email,
          role: i.role,
          status: 'invited',
          invitedAt: i.invitedAt,
        }))
      return HttpResponse.json([...pending, ...members])
    }),
  ),

  http.post(
    api('/settings/team/invite'),
    withAuth(
      async ({ ctx, request }) => {
        const input = (await parseBody(request, inviteSchema)) as unknown as InviteInput
        const email = input.email.trim().toLowerCase()
        if (!/^\S+@\S+\.\S+$/.test(email)) return problem(422, 'Некорректный email')
        const expected = ctx.org!.type === 'fulfillment' ? ['fc', 'worker'] : ['seller']
        if (!expected.includes(cabinetForRole(input.role)))
          return problem(422, 'Роль недоступна для организации')
        const taken =
          db.data.users.some(
            (u) => u.email === email && u.memberships.some((m) => m.organizationId === ctx.org!.id),
          ) || invites.all.some((i) => !i.revoked && i.email === email && i.organizationId === ctx.org!.id)
        if (taken) return problem(409, 'Пользователь уже в команде или приглашён')
        const rec: InviteRecord = {
          id: `inv_${Date.now()}`,
          organizationId: ctx.org!.id,
          email,
          role: input.role,
          invitedAt: new Date().toISOString(),
        }
        invites.upsert(rec)
        const member: TeamMember = {
          id: rec.id,
          fullName: '',
          email,
          role: rec.role,
          status: 'invited',
          invitedAt: rec.invitedAt,
        }
        return HttpResponse.json(member, { status: 201 })
      },
      { roles: OWNERS },
    ),
  ),

  http.post(
    api('/settings/team/:id/revoke'),
    withAuth(
      ({ ctx, params }) => {
        const rec = invites.find(param(params, 'id'))
        if (!rec || rec.revoked || rec.organizationId !== ctx.org?.id)
          return problem(404, 'Приглашение не найдено')
        invites.upsert({ ...rec, revoked: true })
        return new HttpResponse(null, { status: 204 })
      },
      { roles: OWNERS },
    ),
  ),
]
