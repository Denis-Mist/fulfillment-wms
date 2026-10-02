import { http } from '@/shared/api/http'
import type { AuthResponse, CurrentUser, LoginRequest, RegisterRequest } from './types'

export const authApi = {
  login: (body: LoginRequest) => http.post<AuthResponse>('/auth/login', body),
  register: (body: RegisterRequest) => http.post<AuthResponse>('/auth/register', body),
  me: () => http.get<CurrentUser>('/auth/me'),
}
