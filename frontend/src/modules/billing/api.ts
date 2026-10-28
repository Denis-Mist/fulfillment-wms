import { http } from '@/shared/api/http'
import type { Paged } from '@/shared/api/types'
import type { ActItem, BillingContract, BillingSummary, ChargeItem, ChargeQuery, Tariff } from './types'

export const billingApi = {
  contracts: () => http.get<BillingContract[]>('/billing/contracts'),
  summary: (contractId?: string) => http.get<BillingSummary>('/billing/summary', { contractId }),
  charges: (query: ChargeQuery) => http.get<Paged<ChargeItem>>('/billing/charges', { ...query }),
  acts: (contractId?: string) => http.get<ActItem[]>('/billing/acts', { contractId }),
  issueAct: (id: string) => http.post<ActItem>(`/billing/acts/${id}/issue`),
  payAct: (id: string) => http.post<ActItem>(`/billing/acts/${id}/pay`),
  tariff: (contractId: string) => http.get<Tariff>(`/billing/tariffs/${contractId}`),
  saveTariff: (contractId: string, tariff: Tariff) =>
    http.put<Tariff>(`/billing/tariffs/${contractId}`, tariff),
}
