import type { Tone } from '@/shared/config/statuses'
import type { AuditCategory, ReferenceKind } from './types'

export const AUDIT_CATEGORIES: Record<AuditCategory, { label: string; tone: Tone }> = {
  auth: { label: 'Вход', tone: 'neutral' },
  organization: { label: 'Организации', tone: 'warn' },
  inbound: { label: 'Поставки', tone: 'info' },
  order: { label: 'Заказы', tone: 'progress' },
  stock: { label: 'Остатки', tone: 'success' },
  reference: { label: 'Справочники', tone: 'neutral' },
  user: { label: 'Пользователи', tone: 'danger' },
}

export const REFERENCE_KINDS: Record<ReferenceKind, { label: string; description: string }> = {
  units: { label: 'Единицы измерения', description: 'Используются в карточках товаров и документах' },
  cell_types: { label: 'Типы ячеек и тары', description: 'Определяют допустимые размеры и вес при размещении' },
  carriers: { label: 'Перевозчики и маркетплейсы', description: 'Получатели отгрузок и источники заказов' },
  services: { label: 'Виды услуг', description: 'Тарифицируемые операции фулфилмента' },
}
