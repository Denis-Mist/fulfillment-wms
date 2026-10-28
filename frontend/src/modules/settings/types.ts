import type { Role } from '@/modules/auth/types'

export interface OrgProfile {
  name: string
  inn: string
  kpp: string
  ogrn: string
  legalAddress: string
  contactEmail: string
  contactPhone: string
  bankName: string
  bik: string
  account: string
  corrAccount: string
}

export interface CardRules {
  requireBarcode: boolean
  requireDimensions: boolean
  requireWeight: boolean
  requirePhoto: boolean
  requireExpiry: boolean
  requireMarking: boolean
  autoApprove: boolean
}

export interface NotificationPrefs {
  email: boolean
  inboundStatus: boolean
  lowStock: boolean
  slaRisk: boolean
  billing: boolean
}

export interface TeamMember {
  id: string
  fullName: string
  email: string
  role: Role
  status: 'active' | 'invited'
  invitedAt?: string
}

export interface InviteInput {
  email: string
  role: Role
}

export interface OrgSettings {
  id: string
  profile: OrgProfile
  cardRules: CardRules
  notifications: NotificationPrefs
}
