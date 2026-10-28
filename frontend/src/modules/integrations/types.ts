export type IntegrationChannel = 'wb' | 'ozon'
export type IntegrationStatus = 'connected' | 'error' | 'disconnected'

export interface IntegrationLogEntry {
  id: string
  at: string
  kind: 'orders' | 'stock' | 'connect' | 'error'
  message: string
  count?: number
}

export interface Integration {
  channel: IntegrationChannel
  status: IntegrationStatus
  /** Маска ключа: последние 4 символа */
  keyMask?: string
  clientId?: string
  connectedAt?: string
  lastSyncAt?: string
  autoImportFbs: boolean
  pushStock: boolean
  syncIntervalMin: number
  log: IntegrationLogEntry[]
}

export interface ConnectInput {
  apiKey: string
  clientId?: string
}

export interface IntegrationSettingsInput {
  autoImportFbs: boolean
  pushStock: boolean
  syncIntervalMin: number
}
