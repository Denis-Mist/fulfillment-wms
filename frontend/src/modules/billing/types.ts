import type { PageQuery } from '@/shared/api/types'

export type BillingService = 'receiving' | 'storage' | 'picking' | 'packing' | 'fbo' | 'returns'
export type ActStatus = 'draft' | 'issued' | 'paid'

export interface Tariff {
  receivingPerUnit: number
  storagePerM3Day: number
  pickPerOrder: number
  packPerUnit: number
  fboPerBox: number
  returnPerUnit: number
}

export interface BillingContract {
  contractId: string
  contractNumber: string
  counterpartyName: string
}

export interface ChargeItem {
  id: string
  contractId: string
  counterpartyName: string
  date: string
  service: BillingService
  quantity: number
  unit: string
  rate: number
  amount: number
}

export interface ChargeQuery extends PageQuery {
  contractId?: string
  service?: BillingService
}

export interface BillingSummary {
  monthAccrued: number
  prevMonthAccrued: number
  unpaid: number
  balance: number
  byService: { service: BillingService; amount: number }[]
  daily: { date: string; amounts: Partial<Record<BillingService, number>> }[]
}

export interface ActItem {
  id: string
  number: string
  contractId: string
  counterpartyName: string
  period: string
  amount: number
  status: ActStatus
  issuedAt?: string
  paidAt?: string
  dueAt?: string
}
