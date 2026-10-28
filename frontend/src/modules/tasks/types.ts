import type { PageQuery } from '@/shared/api/types'
import type { Role } from '@/modules/auth/types'
import type { SalesChannel } from '@/modules/orders/types'

export type TaskType = 'receiving' | 'putaway' | 'picking' | 'packing' | 'move' | 'inventory'
export type TaskStatus = 'new' | 'assigned' | 'in_progress' | 'done' | 'cancelled'
export type TaskPriority = 'normal' | 'high'
export type RouteAlgorithm = 'optimized' | 'baseline'

export interface TaskListItem {
  id: string
  number: string
  type: TaskType
  status: TaskStatus
  priority: TaskPriority
  warehouseId: string
  warehouseName: string
  assigneeId?: string
  assigneeName?: string
  createdAt: string
  startedAt?: string
  finishedAt?: string
  /** Документ-основание: номера заказов или поставки */
  reference: string
  ordersCount: number
  linesCount: number
  units: number
  doneUnits: number
  normMinutes: number
  /** Фактическое/текущее время выполнения, мин */
  elapsedMinutes?: number
  isLate: boolean
}

export interface TaskStep {
  index: number
  locationId: string
  locationCode: string
  zoneCode: string
  productId: string
  productName: string
  sku: string
  barcode: string
  quantity: number
  done: number
  /** Расстояние от предыдущей точки маршрута, м */
  distance: number
}

export interface TaskDetails extends TaskListItem {
  steps: TaskStep[]
  algorithm: RouteAlgorithm
  /** Длина маршрута текущего порядка обхода и базового (по адресам), м */
  routeDistance: number
  baselineDistance: number
  eligibleRoles: Role[]
}

export interface TaskQuery extends PageQuery {
  status?: TaskStatus | 'active' | 'late'
  type?: TaskType
  assigneeId?: string
  warehouseId?: string
}

export interface TaskSummary {
  total: number
  active: number
  late: number
  unassigned: number
  doneToday: number
  avgMinutes: number
  byStatus: Partial<Record<TaskStatus, number>>
  byType: Partial<Record<TaskType, number>>
}

export interface WorkerLoad {
  id: string
  fullName: string
  role: Role
  active: number
  doneToday: number
  unitsToday: number
  avgMinutes: number
}

export interface PickCandidate {
  id: string
  number: string
  channel: SalesChannel
  sellerName: string
  warehouseId: string
  shipBy: string
  isOverdue: boolean
  linesCount: number
  units: number
}

export interface RoutePoint {
  code: string
  x: number
  y: number
}

export interface RoutePreview {
  optimized: { distance: number; points: RoutePoint[] }
  baseline: { distance: number; points: RoutePoint[] }
  stepsCount: number
  units: number
  estimatedMinutes: number
}

export interface ScanResult {
  task: TaskDetails
  message: string
}

export interface LookupResult {
  kind: 'location' | 'product'
  title: string
  subtitle: string
  rows: { id: string; primary: string; secondary: string; quantity: number; reserved: number }[]
}
