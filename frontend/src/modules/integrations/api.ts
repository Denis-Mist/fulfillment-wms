import { http } from '@/shared/api/http'
import type { ConnectInput, Integration, IntegrationChannel, IntegrationSettingsInput } from './types'

export const integrationsApi = {
  list: () => http.get<Integration[]>('/integrations'),
  connect: (channel: IntegrationChannel, input: ConnectInput) =>
    http.post<Integration>(`/integrations/${channel}/connect`, input),
  disconnect: (channel: IntegrationChannel) => http.post<Integration>(`/integrations/${channel}/disconnect`),
  sync: (channel: IntegrationChannel) => http.post<Integration>(`/integrations/${channel}/sync`),
  save: (channel: IntegrationChannel, input: IntegrationSettingsInput) =>
    http.put<Integration>(`/integrations/${channel}`, input),
}
