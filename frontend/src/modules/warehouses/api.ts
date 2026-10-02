import { http } from '@/shared/api/http'
import type { Warehouse } from './types'

export const warehousesApi = {
  list: () => http.get<Warehouse[]>('/warehouses'),
}
