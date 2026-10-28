import { http } from '@/shared/api/http'
import type { CardRules, InviteInput, NotificationPrefs, OrgProfile, OrgSettings, TeamMember } from './types'

export const settingsApi = {
  get: () => http.get<OrgSettings>('/settings'),
  saveProfile: (profile: OrgProfile) => http.put<OrgSettings>('/settings/profile', profile),
  saveCardRules: (rules: CardRules) => http.put<OrgSettings>('/settings/card-rules', rules),
  saveNotifications: (prefs: NotificationPrefs) => http.put<OrgSettings>('/settings/notifications', prefs),
  team: () => http.get<TeamMember[]>('/settings/team'),
  invite: (input: InviteInput) => http.post<TeamMember>('/settings/team/invite', input),
  revoke: (id: string) => http.post<void>(`/settings/team/${id}/revoke`),
}
