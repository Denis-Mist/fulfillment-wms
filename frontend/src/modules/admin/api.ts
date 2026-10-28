import { http } from '@/shared/api/http'
import type { Paged } from '@/shared/api/types'
import type {
  AuditEvent,
  AuditQuery,
  OrganizationDetails,
  PlatformOverview,
  PlatformUser,
  PlatformUserQuery,
  ReferenceInput,
  ReferenceItem,
  ReferenceKind,
} from './types'

export const adminApi = {
  overview: () => http.get<PlatformOverview>('/admin/overview'),
  organization: (id: string) => http.get<OrganizationDetails>(`/admin/organizations/${id}`),
  users: (query: PlatformUserQuery) => http.get<Paged<PlatformUser>>('/admin/users', { ...query }),
  setUserBlocked: (id: string, blocked: boolean) =>
    http.post<PlatformUser>(`/admin/users/${id}/${blocked ? 'block' : 'unblock'}`),
  resetPassword: (id: string) => http.post<{ temporaryPassword: string }>(`/admin/users/${id}/reset-password`),
  audit: (query: AuditQuery) => http.get<Paged<AuditEvent>>('/admin/audit', { ...query }),
  references: (kind: ReferenceKind) => http.get<ReferenceItem[]>('/admin/references', { kind }),
  createReference: (input: ReferenceInput) => http.post<ReferenceItem>('/admin/references', input),
  toggleReference: (id: string) => http.post<ReferenceItem>(`/admin/references/${id}/toggle`),
}
