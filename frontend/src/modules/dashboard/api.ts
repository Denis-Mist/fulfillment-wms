import { http } from '@/shared/api/http'
import type { DashboardPeriod, FcDashboard, SellerDashboard } from './types'

export const dashboardApi = {
  fulfillment: (params: { warehouseId?: string; period: DashboardPeriod }) =>
    http.get<FcDashboard>('/dashboard/fulfillment', params),
  seller: (params: { period: DashboardPeriod }) => http.get<SellerDashboard>('/dashboard/seller', params),
}
