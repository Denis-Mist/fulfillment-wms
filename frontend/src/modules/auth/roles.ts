import type { Cabinet, Role } from './types'

export const ROLE_LABELS: Record<Role, string> = {
  platform_admin: 'Администратор платформы',
  fc_owner: 'Директор',
  fc_manager: 'Менеджер склада',
  fc_supervisor: 'Старший смены',
  fc_account_manager: 'Менеджер по клиентам',
  fc_receiver: 'Кладовщик-приёмщик',
  fc_picker: 'Комплектовщик',
  fc_packer: 'Упаковщик',
  seller_owner: 'Владелец',
  seller_manager: 'Менеджер',
}

export const WORKER_ROLES: Role[] = ['fc_receiver', 'fc_picker', 'fc_packer']

export const FC_OFFICE_ROLES: Role[] = ['fc_owner', 'fc_manager', 'fc_supervisor', 'fc_account_manager']

export const SELLER_ROLES: Role[] = ['seller_owner', 'seller_manager']

export function cabinetForRole(role: Role): Cabinet {
  if (role === 'platform_admin') return 'admin'
  if (WORKER_ROLES.includes(role)) return 'worker'
  if (SELLER_ROLES.includes(role)) return 'seller'
  return 'fc'
}

export const CABINET_HOME: Record<Cabinet, string> = {
  admin: 'admin-dashboard',
  fc: 'fc-dashboard',
  seller: 'seller-dashboard',
  worker: 'worker-tasks',
}

export const CABINET_LABELS: Record<Cabinet, string> = {
  admin: 'Администрирование',
  fc: 'Фулфилмент-центр',
  seller: 'Кабинет селлера',
  worker: 'Мои задания',
}
