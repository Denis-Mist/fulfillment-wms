import { http } from '@/shared/api/http'
import type { Paged } from '@/shared/api/types'
import type {
  FboDetails,
  FboInput,
  FboListItem,
  FboQuery,
  FboStatus,
  FboStockOption,
  FboSummary,
} from './types'

export const fboApi = {
  list: (query: FboQuery) => http.get<Paged<FboListItem>>('/fbo', { ...query }),
  summary: (query: Pick<FboQuery, 'warehouseId'>) => http.get<FboSummary>('/fbo/summary', { ...query }),
  get: (id: string) => http.get<FboDetails>(`/fbo/${id}`),
  create: (input: FboInput) => http.post<FboDetails>('/fbo', input),
  submit: (id: string) => http.post<FboDetails>(`/fbo/${id}/submit`),
  cancel: (id: string) => http.post<FboDetails>(`/fbo/${id}/cancel`),
  advance: (id: string, to: FboStatus) => http.post<FboDetails>(`/fbo/${id}/advance`, { to }),
  stock: () => http.get<FboStockOption[]>('/fbo/stock-options'),
}
