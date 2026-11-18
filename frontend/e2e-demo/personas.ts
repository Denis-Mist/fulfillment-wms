/**
 * Персоны демо-режима — те же 11 id, что в v1 (SPEC-v2 §3.6). Email — пользователь МОК-сида
 * (src/mocks/handlers/auth.ts DEMO_PERSONAS), в v2 к серверной БД отношения не имеет.
 */
export type Persona =
  | 'platform_admin'
  | 'fc_owner'
  | 'fc_manager'
  | 'fc_supervisor'
  | 'fc_account_manager'
  | 'fc_receiver'
  | 'fc_picker'
  | 'fc_packer'
  | 'seller_owner'
  | 'seller_manager'
  | 'pending'

export interface PersonaDef {
  /** Заголовок карточки на /demo */
  title: string
  /** Пользователь мок-сида, в которого логинит мок POST /api/auth/demo */
  email: string
  /** Роль в membership (null — платформенный админ без membership) */
  role: string | null
  organizationId: string | null
  /** Куда ведёт вход: домашняя страница кабинета или /pending */
  home: RegExp
  /** Подпись роли в баннере: ROLE_LABELS[role] из src/modules/auth/roles.ts (допускаем короткую и полную форму) */
  bannerRole: RegExp
}

export const PERSONAS: Record<Persona, PersonaDef> = {
  platform_admin: { title: 'Администратор платформы', email: 'admin@demo.ru', role: null, organizationId: null, home: /^\/admin\/?$/, bannerRole: /Администратор платформы/ },
  fc_owner: { title: 'Директор ФЦ', email: 'director@demo.ru', role: 'fc_owner', organizationId: 'org_fc1', home: /^\/fc\/?$/, bannerRole: /Директор( ФЦ)?/ },
  fc_manager: { title: 'Менеджер склада', email: 'manager@demo.ru', role: 'fc_manager', organizationId: 'org_fc1', home: /^\/fc\/?$/, bannerRole: /Менеджер склада/ },
  fc_supervisor: { title: 'Старший смены', email: 'staff8@fc1.demo.ru', role: 'fc_supervisor', organizationId: 'org_fc1', home: /^\/fc\/?$/, bannerRole: /Старший смены/ },
  fc_account_manager: { title: 'Менеджер по клиентам', email: 'am@fc1.demo.ru', role: 'fc_account_manager', organizationId: 'org_fc1', home: /^\/fc\/?$/, bannerRole: /Менеджер по клиентам/ },
  fc_receiver: { title: 'Кладовщик-приёмщик', email: 'staff1@fc1.demo.ru', role: 'fc_receiver', organizationId: 'org_fc1', home: /^\/work\/?$/, bannerRole: /Кладовщик-приёмщик/ },
  fc_picker: { title: 'Комплектовщик', email: 'picker@demo.ru', role: 'fc_picker', organizationId: 'org_fc1', home: /^\/work\/?$/, bannerRole: /Комплектовщик/ },
  fc_packer: { title: 'Упаковщик', email: 'staff6@fc1.demo.ru', role: 'fc_packer', organizationId: 'org_fc1', home: /^\/work\/?$/, bannerRole: /Упаковщик/ },
  seller_owner: { title: 'Владелец селлера', email: 'seller@demo.ru', role: 'seller_owner', organizationId: 'org_s1', home: /^\/seller\/?$/, bannerRole: /Владелец( селлера)?/ },
  seller_manager: { title: 'Менеджер селлера', email: 'manager@ud.demo.ru', role: 'seller_manager', organizationId: 'org_s1', home: /^\/seller\/?$/, bannerRole: /Менеджер( селлера)?/ },
  // owner1@pending.demo.ru — fc_owner в org_p1 «Сибирь Фулфилмент» (status pending)
  pending: { title: 'Организация на проверке', email: 'owner1@pending.demo.ru', role: 'fc_owner', organizationId: 'org_p1', home: /^\/pending\/?$/, bannerRole: /Директор( ФЦ)?/ },
}

export const ALL_PERSONAS = Object.keys(PERSONAS) as Persona[]

/** Ещё один раздел кабинета на персону: баннер виден не только на главной */
export const SECOND_PAGE: Partial<Record<Persona, string>> = {
  platform_admin: '/admin/users',
  fc_owner: '/fc/orders',
  fc_account_manager: '/fc/clients',
  fc_picker: '/work/scan',
  seller_owner: '/seller/products',
}
