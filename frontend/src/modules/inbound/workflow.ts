import type { InboundAction, InboundStatus } from './types'

export const INBOUND_STEPS: { status: InboundStatus; label: string }[] = [
  { status: 'draft', label: 'Создана' },
  { status: 'in_transit', label: 'В пути' },
  { status: 'arrived', label: 'Прибыла' },
  { status: 'receiving', label: 'Приёмка' },
  { status: 'received', label: 'Принята' },
  { status: 'placed', label: 'Размещена' },
]

export interface InboundNextAction {
  action: InboundAction
  label: string
  confirm: string
}

/** Следующий шаг процесса, доступный складу ФЦ */
export const INBOUND_NEXT_ACTION: Partial<Record<InboundStatus, InboundNextAction>> = {
  in_transit: {
    action: 'arrive',
    label: 'Отметить прибытие',
    confirm: 'Подтвердите, что машина с поставкой прибыла на склад.',
  },
  arrived: {
    action: 'start_receiving',
    label: 'Начать приёмку',
    confirm: 'Будут созданы задания на приёмку для кладовщиков.',
  },
  receiving: {
    action: 'complete_receiving',
    label: 'Завершить приёмку',
    confirm: 'Фактические количества будут зафиксированы, расхождения — переданы клиенту.',
  },
  received: {
    action: 'complete_placement',
    label: 'Завершить размещение',
    confirm: 'Товар станет доступен для заказов клиента.',
  },
}

export const INBOUND_CANCELLABLE: InboundStatus[] = ['draft', 'in_transit']
