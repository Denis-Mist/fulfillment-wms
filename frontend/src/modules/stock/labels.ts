import type { Tone } from '@/shared/config/statuses'
import type { ErrorReason, OperationType, StockStatus } from './types'

export const STOCK_STATUSES: Record<StockStatus, { label: string; tone: Tone }> = {
  available: { label: 'Доступно', tone: 'success' },
  reserved: { label: 'В резерве', tone: 'info' },
  defect: { label: 'Брак', tone: 'danger' },
}

export const OPERATION_TYPES: Record<OperationType, { label: string; tone: Tone }> = {
  move: { label: 'Перемещение', tone: 'info' },
  inventory: { label: 'Инвентаризация', tone: 'progress' },
  writeoff: { label: 'Списание', tone: 'danger' },
}

export const ERROR_REASONS: Record<ErrorReason, string> = {
  defect: 'Брак',
  shortage: 'Недостача',
  missort: 'Пересорт',
  expired: 'Истёк срок годности',
}
