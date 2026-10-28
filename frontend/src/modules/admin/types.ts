import type { OrganizationStatus, OrganizationType, Role } from '@/modules/auth/types'
import type { SalesChannel } from '@/modules/orders/types'
import type { PageQuery } from '@/shared/api/types'

export interface PlatformOverview {
  organizations: { total: number; fulfillment: number; seller: number; pending: number; blocked: number }
  users: { total: number; blocked: number }
  warehouses: number
  orders30d: number
  ordersPrev30d: number
  shipped30d: number
  inbound30d: number
  onTimeRate: number
  ordersByDay: { date: string; channels: Record<SalesChannel, number> }[]
  topFulfillments: { id: string; name: string; warehouses: number; clients: number; orders30d: number }[]
  recentEvents: AuditEvent[]
}

export interface OrganizationDetails {
  id: string
  name: string
  type: OrganizationType
  inn: string
  city: string
  status: OrganizationStatus
  createdAt: string
  users: { id: string; fullName: string; email: string; role: Role }[]
  warehouses: { id: string; name: string; code: string; city: string }[]
  partners: { id: string; name: string; startedAt: string; warehouses: number }[]
  orders30d: number
  products: number
}

export interface PlatformUser {
  id: string
  fullName: string
  email: string
  phone: string
  isPlatformAdmin: boolean
  blocked: boolean
  memberships: { organizationId: string; organizationName: string; role: Role }[]
}

export interface PlatformUserQuery extends PageQuery {
  role?: Role
  state?: 'active' | 'blocked'
}

export type AuditCategory = 'auth' | 'organization' | 'inbound' | 'order' | 'stock' | 'reference' | 'user'

export interface AuditEvent {
  id: string
  at: string
  category: AuditCategory
  action: string
  details?: string
  actorName: string
  organizationId?: string
  organizationName?: string
  entity?: string
}

export interface AuditQuery extends PageQuery {
  category?: AuditCategory
  organizationId?: string
}

export type ReferenceKind = 'units' | 'cell_types' | 'carriers' | 'services'

export interface ReferenceItem {
  id: string
  kind: ReferenceKind
  code: string
  name: string
  note?: string
  isActive: boolean
}

export interface ReferenceInput {
  kind: ReferenceKind
  code: string
  name: string
  note?: string
}
