import { describe, expect, it } from 'vitest'
import { CABINET_HOME, ROLE_LABELS, cabinetForRole } from '@/modules/auth/roles'
import type { Role } from '@/modules/auth/types'
import { CABINETS, cabinetDef, routeName } from '@/app/sections'

const ALL_ROLES = Object.keys(ROLE_LABELS) as Role[]

describe('роли и кабинеты', () => {
  it.each([
    ['platform_admin', 'admin'],
    ['fc_owner', 'fc'],
    ['fc_manager', 'fc'],
    ['fc_supervisor', 'fc'],
    ['fc_account_manager', 'fc'],
    ['fc_receiver', 'worker'],
    ['fc_picker', 'worker'],
    ['fc_packer', 'worker'],
    ['seller_owner', 'seller'],
    ['seller_manager', 'seller'],
  ] as const)('cabinetForRole(%s) → %s', (role, cabinet) => {
    expect(cabinetForRole(role)).toBe(cabinet)
  })

  it('у каждой роли есть подпись', () => {
    for (const role of ALL_ROLES) expect(ROLE_LABELS[role]).toBeTruthy()
  })

  it('главная каждого кабинета существует в разделах', () => {
    for (const def of CABINETS) {
      const names = def.groups.flatMap((g) => g.sections).map((s) => routeName(def.id, s.name))
      expect(names).toContain(CABINET_HOME[def.id])
    }
  })

  it('главная кабинета доступна всем ролям этого кабинета', () => {
    for (const def of CABINETS) {
      const home = def.groups.flatMap((g) => g.sections).find((s) => routeName(def.id, s.name) === CABINET_HOME[def.id])!
      const roles = ALL_ROLES.filter((r) => cabinetForRole(r) === def.id)
      for (const role of roles) expect(!home.roles || home.roles.includes(role), `${role} → ${def.id}`).toBe(true)
    }
  })

  it('имена маршрутов и пути уникальны, роли разделов валидны и принадлежат кабинету', () => {
    const names = new Set<string>()
    for (const def of CABINETS) {
      const paths = new Set<string>()
      for (const section of def.groups.flatMap((g) => g.sections)) {
        const name = routeName(def.id, section.name)
        expect(names.has(name), name).toBe(false)
        names.add(name)
        expect(paths.has(section.path), section.path).toBe(false)
        paths.add(section.path)
        for (const role of section.roles ?? []) {
          expect(ALL_ROLES).toContain(role)
          expect(cabinetForRole(role), `${name}: ${role}`).toBe(def.id)
        }
      }
    }
  })

  it('cabinetDef бросает на неизвестный кабинет или возвращает определение', () => {
    expect(cabinetDef('fc').basePath).toBe('/fc')
  })
})
