import type { RequestHandler } from 'msw'
import { setupWorker } from 'msw/browser'
import { db } from './db/store'

/** Все экспортируемые массивы обработчиков из handlers/* — новый модуль подключается без правки этого файла */
const modules = import.meta.glob<Record<string, unknown>>('./handlers/*.ts', { eager: true })
const handlers = Object.values(modules)
  .flatMap((module) => Object.values(module))
  .filter((value): value is RequestHandler[] => Array.isArray(value))
  .flat()

export const worker = setupWorker(...handlers)

export function resetMockData() {
  db.reset()
  // Дополнительные коллекции модулей (mocks/lib/collection.ts)
  for (const key of Object.keys(localStorage)) {
    if (key.startsWith('wms.mock.')) localStorage.removeItem(key)
  }
}
