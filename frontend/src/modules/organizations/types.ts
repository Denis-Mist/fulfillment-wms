import type { OrganizationStatus, OrganizationType } from '@/modules/auth/types'
import type { PageQuery } from '@/shared/api/types'

/** Клиент ФЦ (селлер с действующим договором) */
export interface ClientOption {
  id: string
  name: string
  contractId: string
  warehouseIds: string[]
}

export interface AdminOrganization {
  id: string
  name: string
  type: OrganizationType
  inn: string
  city: string
  status: OrganizationStatus
  createdAt: string
  ownerName: string
  ownerEmail: string
}

export interface AdminOrganizationQuery extends PageQuery {
  status?: OrganizationStatus
  type?: OrganizationType
}

export interface AdminOrganizationSummary {
  total: number
  byStatus: Partial<Record<OrganizationStatus, number>>
}

export type ModerationAction = 'approve' | 'reject' | 'block' | 'unblock'
