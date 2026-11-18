import { setupWorker } from 'msw/browser'
import { gateHandlers, handlers } from './handlers'
import { resetMockData } from './reset'

export const worker = setupWorker(...gateHandlers(handlers))
export { resetMockData }
