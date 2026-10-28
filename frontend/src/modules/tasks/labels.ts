import type { Tone } from '@/shared/config/statuses'
import type { Role } from '@/modules/auth/types'
import type { TaskStatus, TaskType } from './types'

export const TASK_TYPES: Record<TaskType, { label: string; roles: Role[] }> = {
  receiving: { label: 'Приёмка', roles: ['fc_receiver', 'fc_supervisor'] },
  putaway: { label: 'Размещение', roles: ['fc_receiver', 'fc_supervisor'] },
  picking: { label: 'Отбор', roles: ['fc_picker', 'fc_supervisor'] },
  packing: { label: 'Упаковка', roles: ['fc_packer', 'fc_supervisor'] },
  move: { label: 'Перемещение', roles: ['fc_picker', 'fc_receiver', 'fc_supervisor'] },
  inventory: { label: 'Инвентаризация', roles: ['fc_picker', 'fc_supervisor'] },
}

export const TASK_STATUSES: Record<TaskStatus, { label: string; tone: Tone }> = {
  new: { label: 'Новое', tone: 'info' },
  assigned: { label: 'Назначено', tone: 'progress' },
  in_progress: { label: 'В работе', tone: 'warn' },
  done: { label: 'Выполнено', tone: 'success' },
  cancelled: { label: 'Отменено', tone: 'neutral' },
}

export const ROLE_SHORT: Partial<Record<Role, string>> = {
  fc_receiver: 'Приёмщик',
  fc_picker: 'Комплектовщик',
  fc_packer: 'Упаковщик',
  fc_supervisor: 'Бригадир',
}
