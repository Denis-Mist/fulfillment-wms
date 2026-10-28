import { http } from '@/shared/api/http'
import type { Paged } from '@/shared/api/types'
import type {
  InventoryInput,
  MoveInput,
  OperationQuery,
  OperationSummary,
  ProductStock,
  StockItem,
  StockLocationOption,
  StockOperation,
  StockQuery,
  StockSummary,
  WriteoffInput,
} from './types'

export const stockApi = {
  list: (query: StockQuery) => http.get<Paged<StockItem>>('/stock', { ...query }),
  summary: (query: Pick<StockQuery, 'warehouseId' | 'sellerId'>) =>
    http.get<StockSummary>('/stock/summary', { ...query }),
  get: (id: string) => http.get<StockItem>(`/stock/${id}`),
  products: (query: PageQueryWithSearch) => http.get<Paged<ProductStock>>('/stock/products', { ...query }),

  locations: (warehouseId: string, search?: string) =>
    http.get<StockLocationOption[]>('/stock/locations', { warehouseId, search }),

  operations: (query: OperationQuery) => http.get<Paged<StockOperation>>('/stock/operations', { ...query }),
  operationSummary: (warehouseId?: string) =>
    http.get<OperationSummary>('/stock/operations/summary', { warehouseId }),
  move: (input: MoveInput) => http.post<StockOperation>('/stock/move', input),
  inventory: (input: InventoryInput) => http.post<StockOperation>('/stock/inventory', input),
  writeoff: (input: WriteoffInput) => http.post<StockOperation>('/stock/writeoff', input),
}

type PageQueryWithSearch = {
  page: number
  pageSize: number
  search?: string
  sortField?: string
  sortOrder?: 'asc' | 'desc'
  lowStock?: string
}
