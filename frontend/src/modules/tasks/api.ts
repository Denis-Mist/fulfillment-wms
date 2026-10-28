import { http } from '@/shared/api/http'
import type { Paged } from '@/shared/api/types'
import type {
  LookupResult,
  PickCandidate,
  RouteAlgorithm,
  RoutePreview,
  ScanResult,
  TaskDetails,
  TaskListItem,
  TaskQuery,
  TaskSummary,
  WorkerLoad,
} from './types'

export const tasksApi = {
  list: (query: TaskQuery) => http.get<Paged<TaskListItem>>('/tasks', { ...query }),
  summary: (query: Omit<TaskQuery, 'page' | 'pageSize' | 'status'>) =>
    http.get<TaskSummary>('/tasks/summary', { ...query }),
  get: (id: string) => http.get<TaskDetails>(`/tasks/${id}`),
  assign: (id: string, userId: string | null) => http.post<TaskDetails>(`/tasks/${id}/assign`, { userId }),
  cancel: (id: string) => http.post<TaskDetails>(`/tasks/${id}/cancel`),
  autoAssign: (warehouseId?: string) =>
    http.post<{ assigned: number }>('/tasks/auto-assign', { warehouseId }),
  workers: (warehouseId?: string) => http.get<WorkerLoad[]>('/tasks/workers', { warehouseId }),

  candidates: (warehouseId?: string) => http.get<PickCandidate[]>('/picking/candidates', { warehouseId }),
  preview: (orderIds: string[]) => http.post<RoutePreview>('/picking/preview', { orderIds }),
  createBatch: (orderIds: string[], algorithm: RouteAlgorithm) =>
    http.post<TaskDetails>('/picking/batches', { orderIds, algorithm }),

  myTasks: () => http.get<TaskListItem[]>('/work/tasks'),
  start: (id: string) => http.post<TaskDetails>(`/work/tasks/${id}/start`),
  scan: (id: string, code: string) => http.post<ScanResult>(`/work/tasks/${id}/scan`, { code }),
  complete: (id: string) => http.post<TaskDetails>(`/work/tasks/${id}/complete`),
  lookup: (code: string) => http.get<LookupResult>('/work/lookup', { code }),
}
