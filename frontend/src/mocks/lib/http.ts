import { delay, HttpResponse, type PathParams } from 'msw'
import type { Role } from '@/modules/auth/types'
import type { Paged, SortOrder } from '@/shared/api/types'
import { db } from '../db/store'
import type { OrgRecord, UserRecord } from '../db/types'

export interface MockContext {
  user: UserRecord
  org: OrgRecord | null
  role: Role
}

export function problem(status: number, title: string, detail?: string) {
  return HttpResponse.json({ type: `https://httpstatuses.io/${status}`, title, status, detail }, { status })
}

const TOKEN_PREFIX = 'mock.'

export const tokenFor = (userId: string) => `${TOKEN_PREFIX}${userId}`

export function resolveContext(request: Request): MockContext | Response {
  const header = request.headers.get('Authorization') ?? ''
  const token = header.replace(/^Bearer\s+/i, '')
  const userId = token.startsWith(TOKEN_PREFIX) ? token.slice(TOKEN_PREFIX.length) : null
  const user = userId ? db.data.users.find((candidate) => candidate.id === userId) : undefined
  if (!user) return problem(401, 'Требуется авторизация')
  if (user.isPlatformAdmin) return { user, org: null, role: 'platform_admin' }

  const organizationId = request.headers.get('X-Organization-Id')
  const membership =
    user.memberships.find((m) => m.organizationId === organizationId) ?? user.memberships[0]
  if (!membership) return problem(403, 'Нет доступа к организации')
  const org = db.data.organizations.find((o) => o.id === membership.organizationId) ?? null
  return { user, org, role: membership.role }
}

export const latency = () => delay(150 + Math.random() * 300)

interface ResolverArgs {
  request: Request
  params: PathParams
  ctx: MockContext
  url: URL
}

export function withAuth(
  resolver: (args: ResolverArgs) => Response | Promise<Response>,
  options: { allowInactive?: boolean; roles?: Role[] } = {},
) {
  return async ({ request, params }: { request: Request; params: PathParams }) => {
    await latency()
    const ctx = resolveContext(request)
    if (ctx instanceof Response) return ctx
    if (ctx.org && ctx.org.status !== 'active' && !options.allowInactive) {
      return problem(403, 'Организация не активна', 'Доступ откроется после проверки администратором.')
    }
    if (options.roles && !options.roles.includes(ctx.role)) return problem(403, 'Недостаточно прав')
    return resolver({ request, params, ctx, url: new URL(request.url) })
  }
}

export function param(params: PathParams, name: string): string {
  const value = params[name]
  return Array.isArray(value) ? String(value[0]) : String(value ?? '')
}

export interface ListParams {
  page: number
  pageSize: number
  search: string
  sortField?: string
  sortOrder: SortOrder
}

export function listParams(url: URL, defaults: { sortField?: string; sortOrder?: SortOrder } = {}): ListParams {
  const get = (key: string) => url.searchParams.get(key) ?? undefined
  return {
    page: Math.max(1, Number(get('page') ?? 1) || 1),
    pageSize: Math.min(200, Math.max(1, Number(get('pageSize') ?? 25) || 25)),
    search: (get('search') ?? '').trim().toLowerCase(),
    sortField: get('sortField') ?? defaults.sortField,
    sortOrder: (get('sortOrder') as SortOrder | undefined) ?? defaults.sortOrder ?? 'desc',
  }
}

export function paginate<T>(items: T[], page: number, pageSize: number): Paged<T> {
  return {
    items: items.slice((page - 1) * pageSize, page * pageSize),
    total: items.length,
    page,
    pageSize,
  }
}

export function sortItems<T>(items: T[], field: string | undefined, order: SortOrder): T[] {
  if (!field) return items
  const direction = order === 'asc' ? 1 : -1
  return [...items].sort((a, b) => {
    const left = (a as Record<string, unknown>)[field]
    const right = (b as Record<string, unknown>)[field]
    if (left === right) return 0
    if (left === undefined || left === null) return 1
    if (right === undefined || right === null) return -1
    if (typeof left === 'number' && typeof right === 'number') return (left - right) * direction
    return String(left).localeCompare(String(right), 'ru') * direction
  })
}

export function countBy<T, K extends string>(items: T[], key: (item: T) => K): Partial<Record<K, number>> {
  const result: Partial<Record<K, number>> = {}
  for (const item of items) {
    const k = key(item)
    result[k] = (result[k] ?? 0) + 1
  }
  return result
}
