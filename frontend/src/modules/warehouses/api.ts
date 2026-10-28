import { http } from '@/shared/api/http'
import type { Paged } from '@/shared/api/types'
import type {
  LocationInput,
  LocationQuery,
  PutawayCheckInput,
  PutawayCheckResult,
  StorageLocation,
  Warehouse,
  WarehouseMapCell,
  WarehouseProductOption,
  Zone,
  ZoneInput,
} from './types'

export const warehousesApi = {
  list: () => http.get<Warehouse[]>('/warehouses'),
  zones: (warehouseId: string) => http.get<Zone[]>(`/warehouses/${warehouseId}/zones`),
  createZone: (warehouseId: string, input: ZoneInput) => http.post<Zone>(`/warehouses/${warehouseId}/zones`, input),
  locations: (warehouseId: string, query: LocationQuery) =>
    http.get<Paged<StorageLocation>>(`/warehouses/${warehouseId}/locations`, { ...query }),
  createLocation: (warehouseId: string, input: LocationInput) =>
    http.post<StorageLocation>(`/warehouses/${warehouseId}/locations`, input),
  map: (warehouseId: string, level: number) =>
    http.get<WarehouseMapCell[]>(`/warehouses/${warehouseId}/map`, { level }),
  products: (warehouseId: string, search: string) =>
    http.get<WarehouseProductOption[]>(`/warehouses/${warehouseId}/products`, { search }),
  checkPutaway: (warehouseId: string, input: PutawayCheckInput) =>
    http.post<PutawayCheckResult>(`/warehouses/${warehouseId}/putaway-check`, input),
}
