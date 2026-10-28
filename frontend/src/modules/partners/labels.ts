import type { FcService, FulfillmentCard } from './types'
import type { Tone } from '@/shared/config/statuses'

export const FC_SERVICE_LABELS: Record<FcService, string> = {
  storage: 'Хранение',
  fbs: 'Сборка FBS',
  fbo: 'Поставки FBO',
  marking: 'Маркировка «Честный знак»',
  returns: 'Обработка возвратов',
  photo: 'Фотосъёмка',
  kits: 'Комплекты',
}

export const CONNECTION_META: Record<FulfillmentCard['connection'], { label: string; tone: Tone }> = {
  connected: { label: 'Договор действует', tone: 'success' },
  pending: { label: 'Заявка на рассмотрении', tone: 'warn' },
  accepted: { label: 'Заявка одобрена', tone: 'info' },
  declined: { label: 'Заявка отклонена', tone: 'danger' },
  none: { label: 'Не подключён', tone: 'neutral' },
}
