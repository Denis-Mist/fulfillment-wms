import type { ZoneType } from './types'

export const ZONE_TYPES: Record<ZoneType, string> = {
  receiving: 'Приёмка',
  storage: 'Хранение',
  picking: 'Отбор',
  packing: 'Упаковка',
  shipping: 'Отгрузка',
  defect: 'Брак / карантин',
}
