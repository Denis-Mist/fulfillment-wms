import { http } from '@/shared/api/http'
import type { AttentionTone } from '@/modules/dashboard/types'

export interface AppNotification {
  id: string
  title: string
  text: string
  at: string
  read: boolean
  tone: AttentionTone
  link?: { type: 'order' | 'inbound'; id: string }
}

export const notificationsApi = {
  list: () => http.get<AppNotification[]>('/notifications'),
  markAllRead: () => http.post<void>('/notifications/read-all'),
}
