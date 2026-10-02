import { http } from '@/shared/api/http'
import type { Paged } from '@/shared/api/types'
import type { InboundAction, InboundListItem, InboundListQuery, InboundShipment, InboundSummary } from './types'

export const inboundApi = {
  list: (query: InboundListQuery) => http.get<Paged<InboundListItem>>('/inbound-shipments', { ...query }),
  summary: (query: Omit<InboundListQuery, 'page' | 'pageSize' | 'status'>) =>
    http.get<InboundSummary>('/inbound-shipments/summary', { ...query }),
  get: (id: string) => http.get<InboundShipment>(`/inbound-shipments/${id}`),
  action: (id: string, action: InboundAction) =>
    http.post<InboundShipment>(`/inbound-shipments/${id}/actions/${action}`),
}
