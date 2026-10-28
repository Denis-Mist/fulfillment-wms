import type { Shift, Skill } from './types'

export const SKILLS: Record<Skill, string> = {
  receiving: 'Приёмка',
  picking: 'Отбор',
  packing: 'Упаковка',
  marking: 'Маркировка «Честный знак»',
  forklift: 'Погрузчик',
  inventory: 'Инвентаризация',
  returns: 'Возвраты',
}

export const SHIFTS: Record<Shift, { label: string; short: string; cls: string }> = {
  day: {
    label: 'Дневная 08–20',
    short: 'Д',
    cls: 'bg-primary-50 text-primary-700 dark:bg-primary-500/15 dark:text-primary-300',
  },
  night: {
    label: 'Ночная 20–08',
    short: 'Н',
    cls: 'bg-indigo-100 text-indigo-700 dark:bg-indigo-500/20 dark:text-indigo-300',
  },
  off: { label: 'Выходной', short: '—', cls: 'bg-surface-50 text-secondary dark:bg-surface-800' },
  vacation: {
    label: 'Отпуск',
    short: 'О',
    cls: 'bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300',
  },
}

export const SHIFT_CYCLE: Shift[] = ['day', 'night', 'off', 'vacation']
