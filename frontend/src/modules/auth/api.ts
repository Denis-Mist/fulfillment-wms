import { http } from '@/shared/api/http'
import type { AuthResponse, CurrentUser, LoginRequest, RegisterRequest } from './types'

export type DemoPersona =
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

export const authApi = {
  login: (body: LoginRequest) => http.post<AuthResponse>('/auth/login', body),
  register: (body: RegisterRequest) => http.post<AuthResponse>('/auth/register', body),
  me: () => http.get<CurrentUser>('/auth/me'),
  demoStatus: () => http.get<{ enabled: boolean }>('/auth/demo/status'),
  demo: (persona: DemoPersona) => http.post<AuthResponse>('/auth/demo', { persona }),
}
