import { http, HttpResponse } from 'msw'
import type { OrganizationStatus } from '@/modules/auth/types'
import type { AdminOrganization, AdminOrganizationSummary, ModerationAction } from '@/modules/organizations/types'
import { db } from '../db/store'
import type { OrgRecord } from '../db/types'
import { countBy, listParams, paginate, param, problem, sortItems, withAuth } from '../lib/http'
import { api } from './api'

function toAdminOrganization(org: OrgRecord): AdminOrganization {
  const owner = db.data.users.find((u) =>
    u.memberships.some((m) => m.organizationId === org.id && (m.role === 'fc_owner' || m.role === 'seller_owner')),
  )
  return {
    id: org.id,
    name: org.name,
    type: org.type,
    inn: org.inn,
    city: org.city,
    status: org.status,
    createdAt: org.createdAt,
    ownerName: owner?.fullName ?? '—',
    ownerEmail: owner?.email ?? '—',
  }
}

function filtered(url: URL, withStatus: boolean) {
  const { search } = listParams(url)
  const type = url.searchParams.get('type')
  const status = withStatus ? url.searchParams.get('status') : null
  return db.data.organizations
    .filter((o) => (!type || o.type === type) && (!status || o.status === status))
    .map(toAdminOrganization)
    .filter(
      (o) =>
        !search ||
        o.name.toLowerCase().includes(search) ||
        o.inn.includes(search) ||
        o.ownerEmail.toLowerCase().includes(search),
    )
}

const MODERATION: Record<ModerationAction, { from: OrganizationStatus[]; to: OrganizationStatus }> = {
  approve: { from: ['pending', 'rejected'], to: 'active' },
  reject: { from: ['pending'], to: 'rejected' },
  block: { from: ['active'], to: 'blocked' },
  unblock: { from: ['blocked'], to: 'active' },
}

const adminOnly = { roles: ['platform_admin' as const] }

export const adminHandlers = [
  http.get(
    api('/admin/organizations/summary'),
    withAuth(({ url }) => {
      const items = filtered(url, false)
      const summary: AdminOrganizationSummary = { total: items.length, byStatus: countBy(items, (o) => o.status) }
      return HttpResponse.json(summary)
    }, adminOnly),
  ),

  http.get(
    api('/admin/organizations'),
    withAuth(({ url }) => {
      const params = listParams(url, { sortField: 'createdAt', sortOrder: 'desc' })
      return HttpResponse.json(paginate(sortItems(filtered(url, true), params.sortField, params.sortOrder), params.page, params.pageSize))
    }, adminOnly),
  ),

  http.post(
    api('/admin/organizations/:id/:action'),
    withAuth(({ params }) => {
      const org = db.data.organizations.find((o) => o.id === param(params, 'id'))
      if (!org) return problem(404, 'Организация не найдена')
      const rule = MODERATION[param(params, 'action') as ModerationAction]
      if (!rule) return problem(400, 'Неизвестное действие')
      if (!rule.from.includes(org.status)) return problem(409, 'Действие недоступно для текущего статуса')
      const updated = { ...org, status: rule.to }
      db.upsert('organizations', updated)
      return HttpResponse.json(toAdminOrganization(updated))
    }, adminOnly),
  ),
]
