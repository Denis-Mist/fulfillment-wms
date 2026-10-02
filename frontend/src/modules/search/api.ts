import { http } from '@/shared/api/http'

export type SearchResultType = 'order' | 'inbound' | 'product'

export interface SearchResult {
  type: SearchResultType
  id: string
  title: string
  subtitle: string
}

export const searchApi = {
  search: (q: string, signal?: AbortSignal) => http.get<SearchResult[]>('/search', { q }, signal),
}
