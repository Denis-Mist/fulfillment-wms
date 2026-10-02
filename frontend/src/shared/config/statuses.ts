import type { OrganizationStatus, OrganizationType } from '@/modules/auth/types'
import type { InboundStatus } from '@/modules/inbound/types'
import type { OrderStatus, SalesChannel } from '@/modules/orders/types'

export type Tone = 'neutral' | 'info' | 'progress' | 'warn' | 'success' | 'danger'

export interface StatusMeta {
  label: string
  tone: Tone
}

export const ORDER_STATUSES: Record<OrderStatus, StatusMeta> = {
  new: { label: 'Новый', tone: 'info' },
  reserved: { label: 'В резерве', tone: 'info' },
  out_of_stock: { label: 'Нет в наличии', tone: 'danger' },
  picking: { label: 'В отборе', tone: 'progress' },
  picked: { label: 'Отобран', tone: 'progress' },
  packed: { label: 'Упакован', tone: 'progress' },
  shipped: { label: 'Отгружен', tone: 'success' },
  cancelled: { label: 'Отменён', tone: 'neutral' },
}

export const INBOUND_STATUSES: Record<InboundStatus, StatusMeta> = {
  draft: { label: 'Черновик', tone: 'neutral' },
  in_transit: { label: 'В пути', tone: 'info' },
  arrived: { label: 'Прибыла', tone: 'warn' },
  receiving: { label: 'На приёмке', tone: 'progress' },
  received: { label: 'Принята', tone: 'progress' },
  placed: { label: 'Размещена', tone: 'success' },
  cancelled: { label: 'Отменена', tone: 'neutral' },
}

export const ORGANIZATION_STATUSES: Record<OrganizationStatus, StatusMeta> = {
  pending: { label: 'На проверке', tone: 'warn' },
  active: { label: 'Активна', tone: 'success' },
  rejected: { label: 'Отклонена', tone: 'danger' },
  blocked: { label: 'Заблокирована', tone: 'neutral' },
}

export const ORGANIZATION_TYPES: Record<OrganizationType, string> = {
  fulfillment: 'Фулфилмент-центр',
  seller: 'Селлер',
}

export const STATUS_DOMAINS = {
  order: ORDER_STATUSES,
  inbound: INBOUND_STATUSES,
  organization: ORGANIZATION_STATUSES,
} as const

export type StatusDomain = keyof typeof STATUS_DOMAINS

export interface ChannelMeta {
  label: string
  name: string
  short: string
  color: string
}

export const SALES_CHANNELS: Record<SalesChannel, ChannelMeta> = {
  wb: { label: 'Wildberries FBS', name: 'Wildberries', short: 'WB', color: '#cb11ab' },
  ozon: { label: 'Ozon FBS', name: 'Ozon', short: 'Ozon', color: '#005bff' },
  own: { label: 'Собственный заказ', name: 'Свои', short: 'Свой', color: '#64748b' },
}

export const TONE_CLASSES: Record<Tone, { badge: string; dot: string; text: string; soft: string }> = {
  neutral: {
    badge: 'bg-surface-100 text-surface-600 dark:bg-surface-800 dark:text-surface-300',
    dot: 'bg-surface-400',
    text: 'text-surface-500',
    soft: 'bg-surface-100 dark:bg-surface-800',
  },
  info: {
    badge: 'bg-sky-50 text-sky-700 dark:bg-sky-500/15 dark:text-sky-300',
    dot: 'bg-sky-500',
    text: 'text-sky-600 dark:text-sky-400',
    soft: 'bg-sky-50 dark:bg-sky-500/10',
  },
  progress: {
    badge: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/15 dark:text-indigo-300',
    dot: 'bg-indigo-500',
    text: 'text-indigo-600 dark:text-indigo-400',
    soft: 'bg-indigo-50 dark:bg-indigo-500/10',
  },
  warn: {
    badge: 'bg-amber-50 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300',
    dot: 'bg-amber-500',
    text: 'text-amber-600 dark:text-amber-400',
    soft: 'bg-amber-50 dark:bg-amber-500/10',
  },
  success: {
    badge: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300',
    dot: 'bg-emerald-500',
    text: 'text-emerald-600 dark:text-emerald-400',
    soft: 'bg-emerald-50 dark:bg-emerald-500/10',
  },
  danger: {
    badge: 'bg-rose-50 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300',
    dot: 'bg-rose-500',
    text: 'text-rose-600 dark:text-rose-400',
    soft: 'bg-rose-50 dark:bg-rose-500/10',
  },
}
