import { setupWorker } from 'msw/browser'
import { adminHandlers } from './handlers/admin'
import { authHandlers } from './handlers/auth'
import { dashboardHandlers } from './handlers/dashboard'
import { directoryHandlers } from './handlers/directory'
import { inboundHandlers } from './handlers/inbound'
import { miscHandlers } from './handlers/misc'
import { orderHandlers } from './handlers/orders'
import { db } from './db/store'

export const worker = setupWorker(
  ...authHandlers,
  ...directoryHandlers,
  ...orderHandlers,
  ...inboundHandlers,
  ...dashboardHandlers,
  ...miscHandlers,
  ...adminHandlers,
)

export function resetMockData() {
  db.reset()
}
