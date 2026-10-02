import { http } from '@/shared/api/http'
import type { Paged } from '@/shared/api/types'
import type {
  AdminOrganization,
  AdminOrganizationQuery,
  AdminOrganizationSummary,
  ClientOption,
  ModerationAction,
} from './types'

export const organizationsApi = {
  clients: () => http.get<ClientOption[]>('/clients'),

  adminList: (query: AdminOrganizationQuery) =>
    http.get<Paged<AdminOrganization>>('/admin/organizations', { ...query }),
  adminSummary: (query: Pick<AdminOrganizationQuery, 'search' | 'type'>) =>
    http.get<AdminOrganizationSummary>('/admin/organizations/summary', { ...query }),
  moderate: (id: string, action: ModerationAction) =>
    http.post<AdminOrganization>(`/admin/organizations/${id}/${action}`),
}
