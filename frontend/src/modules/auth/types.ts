export type Role =
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

export type OrganizationType = 'fulfillment' | 'seller'

export type OrganizationStatus = 'pending' | 'active' | 'rejected' | 'blocked'

export type Cabinet = 'admin' | 'fc' | 'seller' | 'worker'

export interface Membership {
  organizationId: string
  organizationName: string
  organizationType: OrganizationType
  organizationStatus: OrganizationStatus
  role: Role
}

export interface CurrentUser {
  id: string
  email: string
  fullName: string
  phone?: string
  isPlatformAdmin: boolean
  memberships: Membership[]
}

export interface AuthResponse {
  accessToken: string
  user: CurrentUser
}

export interface LoginRequest {
  email: string
  password: string
}

export type Marketplace = 'wb' | 'ozon' | 'yandex'

export interface RegisterRequest {
  organizationType: OrganizationType
  organization: {
    name: string
    inn: string
    city: string
    warehousesCount?: number
    marketplaces?: Marketplace[]
  }
  user: {
    fullName: string
    email: string
    phone: string
    password: string
  }
}
