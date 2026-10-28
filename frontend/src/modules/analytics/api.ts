import { http } from '@/shared/api/http'
import type { ForecastAlgorithm, ForecastResult, KpiReport, SlottingAlgorithm, SlottingResult } from './types'

export const analyticsApi = {
  forecast: (params: { warehouseId?: string; horizon: number; algorithm: ForecastAlgorithm }) =>
    http.get<ForecastResult>('/analytics/forecast', { ...params }),
  approvePlan: (body: { warehouseId?: string; algorithm: ForecastAlgorithm }) =>
    http.post<ForecastResult['plan']>('/analytics/forecast/approve', body),
  slotting: (params: { warehouseId?: string; algorithm: SlottingAlgorithm }) =>
    http.get<SlottingResult>('/analytics/slotting', { ...params }),
  applySlotting: (body: { warehouseId?: string; algorithm: SlottingAlgorithm; ids: string[] }) =>
    http.post<{ applied: number }>('/analytics/slotting/apply', body),
  kpi: (params: { warehouseId?: string; sellerId?: string; period: number }) =>
    http.get<KpiReport>('/analytics/kpi', { ...params }),
}
