import type { Component } from 'vue'
import type { RouteComponent } from 'vue-router'
import {
  ArrowLeftRight,
  BookOpen,
  Boxes,
  BrainCircuit,
  Building2,
  Handshake,
  LayoutDashboard,
  ListChecks,
  PackagePlus,
  Plug,
  ScanBarcode,
  ScanLine,
  ScrollText,
  Settings,
  ShieldCheck,
  ShoppingCart,
  Store,
  Tag,
  Truck,
  Undo2,
  Users,
  Wallet,
  Warehouse,
} from '@lucide/vue'
import type { Cabinet, Role } from '@/modules/auth/types'

export interface SectionDef {
  /** Имя маршрута без префикса кабинета */
  name: string
  path: string
  title: string
  description?: string
  icon?: Component
  roles?: Role[]
  page?: () => Promise<RouteComponent>
  /** Что будет в разделе — показывается на странице-заглушке */
  planned?: string[]
  hidden?: boolean
  /** Подсветка пункта меню для вложенных страниц */
  navParent?: string
  badge?: string
}

export interface NavGroup {
  label?: string
  sections: SectionDef[]
}

export interface CabinetDef {
  id: Cabinet
  basePath: string
  groups: NavGroup[]
}

const pages = {
  fcDashboard: () => import('@/pages/dashboard/FcDashboardPage.vue'),
  sellerDashboard: () => import('@/pages/dashboard/SellerDashboardPage.vue'),
  orders: () => import('@/pages/orders/OrdersRegistryPage.vue'),
  inbound: () => import('@/pages/inbound/InboundRegistryPage.vue'),
  inboundDetail: () => import('@/pages/inbound/InboundDetailPage.vue'),
  moderation: () => import('@/pages/admin/ModerationPage.vue'),
}

const FC_MANAGEMENT: Role[] = ['fc_owner', 'fc_manager']
const FC_COMMERCE: Role[] = ['fc_owner', 'fc_account_manager']

export const CABINETS: CabinetDef[] = [
  {
    id: 'fc',
    basePath: '/fc',
    groups: [
      {
        sections: [
          {
            name: 'dashboard',
            path: '',
            title: 'Обзор',
            icon: LayoutDashboard,
            page: pages.fcDashboard,
          },
          {
            name: 'tasks',
            page: () => import('@/pages/tasks/TasksPage.vue'),
            path: 'tasks',
            title: 'Задания',
            description: 'Очередь складских заданий и их распределение между сотрудниками.',
            icon: ListChecks,
            planned: [
              'Очередь заданий: приёмка, размещение, отбор, упаковка, перемещение, инвентаризация',
              'Назначение исполнителей вручную и автоматически',
              'Контроль выполнения в реальном времени и нормативы',
              'История заданий с фактическим временем выполнения',
            ],
          },
        ],
      },
      {
        label: 'Входящие',
        sections: [
          {
            name: 'inbound',
            path: 'inbound',
            title: 'Поставки',
            icon: PackagePlus,
            page: pages.inbound,
          },
          {
            name: 'inbound-detail',
            path: 'inbound/:id',
            title: 'Поставка',
            page: pages.inboundDetail,
            hidden: true,
            navParent: 'inbound',
          },
          {
            name: 'returns',
            path: 'returns',
            title: 'Возвраты',
            description: 'Возвраты от маркетплейсов и покупателей.',
            icon: Undo2,
            planned: [
              'Приёмка возвратов и невыкупов от Wildberries и Ozon',
              'Оценка состояния: в сток, брак, на утилизацию',
              'Фотофиксация и уведомление клиента',
            ],
          },
        ],
      },
      {
        label: 'Склад',
        sections: [
          {
            name: 'stock',
            path: 'stock',
            title: 'Остатки',
            description: 'Остатки по ячейкам, клиентам, партиям и статусам.',
            icon: Boxes,
            planned: [
              'Остатки в разрезе ячейка × товар × партия × статус × владелец',
              'Фильтры по клиенту, зоне, сроку годности',
              'История движений по каждой позиции',
            ],
          },
          {
            name: 'operations',
            path: 'operations',
            title: 'Внутренние операции',
            description: 'Перемещения, пополнение, инвентаризация и списания.',
            icon: ArrowLeftRight,
            planned: [
              'Перемещение товара между ячейками',
              'Пополнение зоны отбора из зоны хранения',
              'Полная и выборочная инвентаризация с актами расхождений',
              'Списание брака',
            ],
          },
          {
            name: 'warehouses',
            path: 'warehouses',
            title: 'Склады и ячейки',
            description: 'Топология складов: зоны и ячейки с координатами.',
            icon: Warehouse,
            roles: FC_MANAGEMENT,
            planned: [
              'Склады организации и их параметры',
              'Зоны: приёмка, хранение, отбор, упаковка, отгрузка, брак',
              'Ячейки с типом, вместимостью и координатами — основа для оптимизации маршрутов',
              'Массовая генерация ячеек и печать этикеток',
            ],
          },
        ],
      },
      {
        label: 'Исходящие',
        sections: [
          {
            name: 'orders',
            path: 'orders',
            title: 'Заказы',
            icon: ShoppingCart,
            page: pages.orders,
          },
          {
            name: 'picking',
            page: () => import('@/pages/picking/PickingPage.vue'),
            path: 'picking',
            title: 'Комплектация',
            description: 'Волны, отбор, упаковка и отгрузка заказов.',
            icon: ScanLine,
            planned: [
              'Формирование волн и батчей отбора',
              'Листы отбора с оптимальным маршрутом по складу',
              'Упаковка: подбор коробки, печать этикеток маркетплейсов',
              'Отгрузка перевозчику и реестры передачи',
            ],
          },
          {
            name: 'fbo',
            path: 'fbo',
            title: 'Отгрузки на маркетплейсы',
            description: 'Поставки FBO на склады Wildberries и Ozon.',
            icon: Truck,
            planned: [
              'Заявки клиентов на поставку на склады маркетплейсов',
              'Сборка коробов и паллет, этикетки коробов и поставки',
              'Слоты и даты отгрузки, реестры',
            ],
          },
        ],
      },
      {
        label: 'Клиенты и финансы',
        sections: [
          {
            name: 'clients',
            path: 'clients',
            title: 'Клиенты и договоры',
            description: 'Селлеры, договоры, заявки на подключение.',
            icon: Handshake,
            roles: FC_COMMERCE,
            planned: [
              'Заявки селлеров на подключение из каталога ФЦ',
              'Договоры: склады, тарифы, сроки',
              'Карточка клиента со статистикой',
            ],
          },
          {
            name: 'products',
            path: 'products',
            title: 'Товары клиентов',
            description: 'Каталог товаров всех клиентов.',
            icon: Tag,
            planned: [
              'SKU клиентов, штрихкоды и ШК маркетплейсов',
              'Габариты и вес, партии, сроки годности, серийные номера',
              'Маркировка «Честный знак»',
            ],
          },
          {
            name: 'billing',
            path: 'billing',
            title: 'Биллинг',
            description: 'Тарифы, начисления и акты.',
            icon: Wallet,
            roles: FC_COMMERCE,
            planned: [
              'Тарифы на хранение, операции и дополнительные услуги',
              'Автоматические начисления по факту операций',
              'Акты за период и баланс клиентов',
            ],
          },
          {
            name: 'analytics',
            path: 'analytics',
            title: 'Аналитика и прогнозы',
            description: 'Интеллектуальные модули оптимизации и прогнозирования.',
            icon: BrainCircuit,
            badge: 'AI',
            roles: FC_MANAGEMENT,
            planned: [
              'Прогноз объёма заказов и нагрузки на склад',
              'Рекомендации по численности персонала на смену',
              'Оптимизация размещения товаров (ABC/XYZ, слоттинг)',
              'Оптимизация маршрутов комплектации и распределения задач',
            ],
          },
        ],
      },
      {
        label: 'Команда',
        sections: [
          {
            name: 'staff',
            path: 'staff',
            title: 'Сотрудники и смены',
            icon: Users,
            roles: ['fc_owner', 'fc_manager', 'fc_supervisor'],
            planned: [
              'Сотрудники, роли, навыки и закрепление за зонами',
              'График смен',
              'Производительность: операций в час, точность',
            ],
          },
          {
            name: 'settings',
            path: 'settings',
            title: 'Настройки',
            icon: Settings,
            roles: FC_MANAGEMENT,
            planned: [
              'Профиль организации и реквизиты',
              'Карточка в каталоге ФЦ',
              'Пользователи и приглашения',
            ],
          },
        ],
      },
    ],
  },
  {
    id: 'seller',
    basePath: '/seller',
    groups: [
      {
        sections: [
          {
            name: 'dashboard',
            path: '',
            title: 'Обзор',
            icon: LayoutDashboard,
            page: pages.sellerDashboard,
          },
        ],
      },
      {
        label: 'Товары',
        sections: [
          {
            name: 'products',
            path: 'products',
            title: 'Мои товары',
            icon: Tag,
            planned: [
              'Карточки товаров: SKU, штрихкоды, ШК маркетплейсов',
              'Габариты, вес, учёт партий и сроков годности',
              'Маркировка «Честный знак»',
              'Импорт из Excel и маркетплейсов',
            ],
          },
          {
            name: 'stock',
            path: 'stock',
            title: 'Остатки',
            icon: Boxes,
            planned: [
              'Остатки по фулфилмент-центрам и складам',
              'Доступно / в резерве / брак',
              'Прогноз, на сколько дней хватит товара',
            ],
          },
        ],
      },
      {
        label: 'Логистика',
        sections: [
          {
            name: 'inbound',
            path: 'inbound',
            title: 'Поставки на ФЦ',
            icon: PackagePlus,
            page: pages.inbound,
          },
          {
            name: 'inbound-detail',
            path: 'inbound/:id',
            title: 'Поставка',
            page: pages.inboundDetail,
            hidden: true,
            navParent: 'inbound',
          },
          {
            name: 'orders',
            path: 'orders',
            title: 'Заказы',
            icon: ShoppingCart,
            page: pages.orders,
          },
          {
            name: 'fbo',
            path: 'fbo',
            title: 'Отгрузки на маркетплейсы',
            icon: Truck,
            planned: [
              'Создание заявки на поставку FBO на склад Wildberries или Ozon',
              'Отслеживание сборки и отгрузки',
              'Документы и этикетки',
            ],
          },
          {
            name: 'returns',
            path: 'returns',
            title: 'Возвраты',
            icon: Undo2,
            planned: ['Возвраты и невыкупы', 'Результат оценки состояния товара', 'Фото и акты'],
          },
        ],
      },
      {
        label: 'Партнёры и финансы',
        sections: [
          {
            name: 'fulfillments',
            path: 'fulfillments',
            title: 'Фулфилмент-центры',
            icon: Store,
            planned: [
              'Каталог фулфилмент-центров: города, услуги, тарифы',
              'Заявки на подключение',
              'Мои договоры',
            ],
          },
          {
            name: 'billing',
            path: 'billing',
            title: 'Счета и начисления',
            icon: Wallet,
            roles: ['seller_owner'],
            planned: ['Начисления по каждому ФЦ', 'Акты и счета', 'Баланс'],
          },
        ],
      },
      {
        label: 'Настройки',
        sections: [
          {
            name: 'integrations',
            path: 'integrations',
            title: 'Интеграции',
            icon: Plug,
            planned: [
              'Подключение кабинетов Wildberries и Ozon по API-ключу',
              'Автоматическая загрузка заказов FBS',
            ],
          },
          {
            name: 'settings',
            path: 'settings',
            title: 'Организация',
            icon: Settings,
            planned: ['Реквизиты', 'Пользователи и роли'],
          },
        ],
      },
    ],
  },
  {
    id: 'admin',
    basePath: '/admin',
    groups: [
      {
        sections: [
          {
            name: 'dashboard',
            path: '',
            title: 'Обзор платформы',
            icon: LayoutDashboard,
            planned: [
              'Количество организаций и пользователей',
              'Суммарные объёмы операций по платформе',
              'Активность и ошибки',
            ],
          },
          {
            name: 'moderation',
            path: 'moderation',
            title: 'Модерация',
            icon: ShieldCheck,
            page: pages.moderation,
          },
        ],
      },
      {
        label: 'Управление',
        sections: [
          {
            name: 'organizations',
            path: 'organizations',
            title: 'Организации',
            icon: Building2,
            planned: [
              'Все ФЦ и селлеры',
              'Вход от имени организации для поддержки',
              'Договоры между организациями',
            ],
          },
          {
            name: 'users',
            path: 'users',
            title: 'Пользователи',
            icon: Users,
            planned: ['Все пользователи платформы', 'Блокировка и сброс доступа'],
          },
          {
            name: 'references',
            path: 'references',
            title: 'Справочники',
            icon: BookOpen,
            planned: ['Единицы измерения', 'Типы ячеек и тары', 'Перевозчики и маркетплейсы', 'Виды услуг'],
          },
          {
            name: 'audit',
            path: 'audit',
            title: 'Журнал действий',
            icon: ScrollText,
            planned: [
              'Аудит всех действий пользователей',
              'Фильтры по организации, пользователю, типу события',
            ],
          },
        ],
      },
    ],
  },
  {
    id: 'worker',
    basePath: '/work',
    groups: [
      {
        sections: [
          {
            name: 'tasks',
            page: () => import('@/pages/worker/WorkerTasksPage.vue'),
            path: '',
            title: 'Мои задания',
            icon: ListChecks,
            planned: [
              'Список назначенных заданий',
              'Пошаговое выполнение: одна операция на экран',
              'Ввод со сканера штрихкодов',
              'Маршрут отбора по ячейкам',
            ],
          },
          {
            name: 'scan',
            page: () => import('@/pages/worker/WorkerScanPage.vue'),
            path: 'scan',
            title: 'Сканер',
            icon: ScanBarcode,
            planned: ['Информация по ячейке или товару по штрихкоду', 'Быстрое перемещение'],
          },
        ],
      },
    ],
  },
]

export function cabinetDef(id: Cabinet): CabinetDef {
  const def = CABINETS.find((cabinet) => cabinet.id === id)
  if (!def) throw new Error(`Unknown cabinet ${id}`)
  return def
}

export function routeName(cabinet: Cabinet, section: string): string {
  return `${cabinet}-${section}`
}
