import { http } from '@/shared/api/http'
import type { ConnectRequestInput, FulfillmentCard } from './types'

export const partnersApi = {
  list: () => http.get<FulfillmentCard[]>('/seller/fulfillments'),
  connect: (fcId: string, input: ConnectRequestInput) =>
    http.post<FulfillmentCard>(`/seller/fulfillments/${fcId}/connect`, input),
}
