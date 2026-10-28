import type { Role } from '@/modules/auth/types'

export type Shift = 'day' | 'night' | 'off' | 'vacation'
export type Skill = 'receiving' | 'picking' | 'packing' | 'marking' | 'forklift' | 'inventory' | 'returns'

export interface Employee {
  id: string
  fullName: string
  email: string
  phone: string
  role: Role
  warehouseId: string
  warehouseName: string
  zoneIds: string[]
  skills: Skill[]
  active: boolean
  onShift: boolean
  opsPerHour: number
  norm: number
  accuracy: number
  hiredAt: string
  /** График на 7 дней начиная с `weekStart` */
  shifts: Shift[]
}

export interface StaffResponse {
  weekStart: string
  items: Employee[]
}

export interface EmployeeInput {
  role: Role
  warehouseId: string
  zoneIds: string[]
  skills: Skill[]
  active: boolean
}
