export interface Paged<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

/** RFC 7807 — формат ошибок ASP.NET Core */
export interface ProblemDetails {
  type?: string
  title: string
  status: number
  detail?: string
  errors?: Record<string, string[]>
}

export type SortOrder = 'asc' | 'desc'

export interface PageQuery {
  page: number
  pageSize: number
  search?: string
  sortField?: string
  sortOrder?: SortOrder
}
