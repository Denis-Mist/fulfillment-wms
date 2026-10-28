import { http } from '@/shared/api/http'
import type { Employee, EmployeeInput, Shift, StaffResponse } from './types'

export const staffApi = {
  list: (warehouseId?: string) => http.get<StaffResponse>('/staff', { warehouseId }),
  update: (id: string, input: EmployeeInput) => http.put<Employee>(`/staff/${id}`, input),
  setShift: (id: string, day: number, shift: Shift) =>
    http.post<Employee>(`/staff/${id}/shifts`, { day, shift }),
}
