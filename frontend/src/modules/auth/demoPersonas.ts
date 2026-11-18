import type { Component } from 'vue'
import {
  Clock,
  Handshake,
  Package,
  PackageCheck,
  ScanBarcode,
  ShieldCheck,
  Store,
  UserRound,
  Users,
  Warehouse,
} from '@lucide/vue'
import type { DemoPersona } from './api'

export interface DemoPersonaCard {
  persona: DemoPersona
  title: string
  description: string
  icon: Component
}

export interface DemoPersonaGroup {
  label: string
  items: DemoPersonaCard[]
}

/** Карточки /demo — тексты из спецификации §1.3 */
export const DEMO_PERSONA_GROUPS: DemoPersonaGroup[] = [
  {
    label: 'Платформа',
    items: [
      {
        persona: 'platform_admin',
        title: 'Администратор платформы',
        description:
          'Обзор платформы, модерация заявок, пользователи, справочники, журнал аудита. Это учебная песочница в браузере.',
        icon: ShieldCheck,
      },
    ],
  },
  {
    label: 'Фулфилмент-центр',
    items: [
      {
        persona: 'fc_owner',
        title: 'Директор ФЦ',
        description: 'Полный кабинет: склады, сотрудники, аналитика, биллинг, клиенты, закупки и отгрузки.',
        icon: Warehouse,
      },
      {
        persona: 'fc_manager',
        title: 'Менеджер склада',
        description: 'Операции склада, сотрудники, тарифы, аналитика, задания и комплектация.',
        icon: Users,
      },
      {
        persona: 'fc_supervisor',
        title: 'Старший смены',
        description: 'Очередь заданий, комплектация, смена, приёмка и отгрузки.',
        icon: UserRound,
      },
      {
        persona: 'fc_account_manager',
        title: 'Менеджер по клиентам',
        description: 'Клиенты и договоры, биллинг, каталог товаров клиентов. Без «Заданий» и «Комплектации».',
        icon: Handshake,
      },
    ],
  },
  {
    label: 'Склад (режим ТСД)',
    items: [
      {
        persona: 'fc_receiver',
        title: 'Кладовщик-приёмщик',
        description: 'Задания приёмки и размещения на ТСД, сканер.',
        icon: PackageCheck,
      },
      {
        persona: 'fc_picker',
        title: 'Комплектовщик',
        description: 'Отбор заказов по ячейкам на ТСД.',
        icon: ScanBarcode,
      },
      {
        persona: 'fc_packer',
        title: 'Упаковщик',
        description: 'Задания упаковки на ТСД.',
        icon: Package,
      },
    ],
  },
  {
    label: 'Селлер',
    items: [
      {
        persona: 'seller_owner',
        title: 'Владелец селлера',
        description: 'Товары, поставки, заказы, FBO, партнёры-ФЦ. Ключи маркетплейсов в демо сменить нельзя.',
        icon: Store,
      },
      {
        persona: 'seller_manager',
        title: 'Менеджер селлера',
        description: 'Операции селлера без подключения интеграций и заявок в новый ФЦ.',
        icon: Store,
      },
    ],
  },
  {
    label: 'Модерация',
    items: [
      {
        persona: 'pending',
        title: 'Организация на проверке',
        description: 'Экран ожидания модерации (заявка «Сибирь Фулфилмент» ещё не одобрена).',
        icon: Clock,
      },
    ],
  },
]

export const DEMO_ACTION_HINT = 'Недоступно в демо'
