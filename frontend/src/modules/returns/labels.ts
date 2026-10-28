import type { Tone } from '@/shared/config/statuses'
import type { ReturnGrade, ReturnReason, ReturnStatus } from './types'

export const RETURN_STATUSES: Record<ReturnStatus, { label: string; tone: Tone }> = {
  expected: { label: 'Ожидается', tone: 'neutral' },
  received: { label: 'Принят, на оценке', tone: 'warn' },
  inspected: { label: 'Оценён', tone: 'info' },
  closed: { label: 'Закрыт', tone: 'success' },
}

export const RETURN_REASONS: Record<ReturnReason, string> = {
  not_bought: 'Невыкуп',
  defect: 'Брак по заявлению покупателя',
  size: 'Не подошёл',
  damaged: 'Повреждён при доставке',
  wrong_item: 'Пересорт',
}

export const RETURN_GRADES: Record<ReturnGrade, { label: string; tone: Tone; restock: boolean }> = {
  as_new: { label: 'Как новый', tone: 'success', restock: true },
  resale: { label: 'Годен к продаже', tone: 'info', restock: true },
  defect: { label: 'Брак', tone: 'danger', restock: false },
  dispose: { label: 'Утилизация', tone: 'neutral', restock: false },
}
