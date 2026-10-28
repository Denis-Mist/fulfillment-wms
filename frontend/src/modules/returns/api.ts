import { http } from '@/shared/api/http'
import type { Paged } from '@/shared/api/types'
import type { InspectInput, ReturnDetails, ReturnListItem, ReturnQuery, ReturnSummary } from './types'

export const returnsApi = {
  list: (query: ReturnQuery) => http.get<Paged<ReturnListItem>>('/returns', { ...query }),
  summary: (query: Pick<ReturnQuery, 'warehouseId' | 'sellerId'>) =>
    http.get<ReturnSummary>('/returns/summary', { ...query }),
  get: (id: string) => http.get<ReturnDetails>(`/returns/${id}`),
  receive: (id: string) => http.post<ReturnDetails>(`/returns/${id}/receive`),
  inspect: (id: string, input: InspectInput) => http.post<ReturnDetails>(`/returns/${id}/inspect`, input),
  close: (id: string) => http.post<ReturnDetails>(`/returns/${id}/close`),
}
