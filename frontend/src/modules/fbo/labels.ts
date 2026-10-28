import type { Tone } from '@/shared/config/statuses'
import type { FboMarketplace, FboStatus } from './types'

export const FBO_STATUSES: Record<FboStatus, { label: string; tone: Tone }> = {
  draft: { label: 'Черновик', tone: 'neutral' },
  confirmed: { label: 'Подтверждена', tone: 'info' },
  assembling: { label: 'Сборка', tone: 'progress' },
  ready: { label: 'Готова к отгрузке', tone: 'warn' },
  shipped: { label: 'Отгружена', tone: 'success' },
  cancelled: { label: 'Отменена', tone: 'danger' },
}

export const FBO_DESTINATIONS: Record<FboMarketplace, string[]> = {
  wb: ['WB Коледино', 'WB Электросталь', 'WB Подольск', 'WB Казань', 'WB Краснодар'],
  ozon: ['Ozon Хоругвино', 'Ozon Пушкино', 'Ozon Тверь', 'Ozon Новая Рига'],
}

/** Следующий шаг для сотрудника ФЦ */
export const FBO_NEXT: Partial<Record<FboStatus, { to: FboStatus; label: string }>> = {
  confirmed: { to: 'assembling', label: 'Начать сборку' },
  assembling: { to: 'ready', label: 'Сборка завершена' },
  ready: { to: 'shipped', label: 'Отгружено' },
}
