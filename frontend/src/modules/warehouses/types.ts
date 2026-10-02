export interface Warehouse {
  id: string
  organizationId: string
  name: string
  code: string
  city: string
  address: string
  areaSqm: number
  locationsCount: number
  occupiedLocations: number
  isActive: boolean
}
