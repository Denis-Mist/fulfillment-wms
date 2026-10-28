import type { Tone } from '@/shared/config/statuses'
import type { ActStatus, BillingService, Tariff } from './types'

export const BILLING_SERVICES: Record<BillingService, { label: string; unit: string; color: string }> = {
  receiving: { label: 'Приёмка', unit: 'шт', color: '#6366f1' },
  storage: { label: 'Хранение', unit: 'м³·сут', color: '#0ea5e9' },
  picking: { label: 'Сборка заказов', unit: 'заказ', color: '#22c55e' },
  packing: { label: 'Упаковка', unit: 'шт', color: '#f59e0b' },
  fbo: { label: 'Поставки FBO', unit: 'короб', color: '#ec4899' },
  returns: { label: 'Возвраты', unit: 'шт', color: '#94a3b8' },
}

export const ACT_STATUSES: Record<ActStatus, { label: string; tone: Tone }> = {
  draft: { label: 'Формируется', tone: 'neutral' },
  issued: { label: 'Выставлен', tone: 'warn' },
  paid: { label: 'Оплачен', tone: 'success' },
}

export const TARIFF_FIELDS: { key: keyof Tariff; label: string; unit: string; step: number }[] = [
  { key: 'receivingPerUnit', label: 'Приёмка', unit: '₽ / шт', step: 0.5 },
  { key: 'storagePerM3Day', label: 'Хранение', unit: '₽ / м³ в сутки', step: 1 },
  { key: 'pickPerOrder', label: 'Сборка заказа', unit: '₽ / заказ', step: 1 },
  { key: 'packPerUnit', label: 'Упаковка', unit: '₽ / шт', step: 0.5 },
  { key: 'fboPerBox', label: 'Подготовка FBO', unit: '₽ / короб', step: 5 },
  { key: 'returnPerUnit', label: 'Обработка возврата', unit: '₽ / шт', step: 1 },
]
