import { http } from '@/shared/api/http'
import type { Paged } from '@/shared/api/types'
import type {
  ClientDetails,
  ClientListItem,
  ClientRequest,
  ImportResult,
  ProductDetails,
  ProductFacets,
  ProductInput,
  ProductListItem,
  ProductQuery,
} from './types'

export const catalogApi = {
  products: (query: ProductQuery) => http.get<Paged<ProductListItem>>('/catalog/products', { ...query }),
  facets: (query: Pick<ProductQuery, 'sellerId' | 'search'>) =>
    http.get<ProductFacets>('/catalog/products/facets', { ...query }),
  product: (id: string) => http.get<ProductDetails>(`/catalog/products/${id}`),
  create: (input: ProductInput) => http.post<ProductListItem>('/catalog/products', input),
  update: (id: string, input: ProductInput) => http.put<ProductListItem>(`/catalog/products/${id}`, input),
  setArchived: (id: string, archived: boolean) =>
    http.post<ProductListItem>(`/catalog/products/${id}/${archived ? 'archive' : 'restore'}`),
  import: (rows: ProductInput[]) => http.post<ImportResult>('/catalog/products/import', { rows }),

  clients: (query: { search?: string }) => http.get<ClientListItem[]>('/fc/clients', { ...query }),
  client: (id: string) => http.get<ClientDetails>(`/fc/clients/${id}`),
  requests: () => http.get<ClientRequest[]>('/fc/client-requests'),
  decide: (id: string, decision: 'accept' | 'decline', warehouseIds?: string[]) =>
    http.post<ClientRequest>(`/fc/client-requests/${id}/${decision}`, { warehouseIds }),
}
