import type { RequestHandler } from 'msw'
import { isMockIntercept } from '@/app/demoMode'

const modules = import.meta.glob<Record<string, unknown>>('./handlers/*.ts', { eager: true })

export const handlers: RequestHandler[] = Object.values(modules)
  .flatMap((module) => Object.values(module))
  .filter((value): value is RequestHandler[] => Array.isArray(value))
  .flat()

/** В браузере: отвечать только при X-WMS-Demo: 1 (или при VITE_USE_MOCKS=true). Иначе запрос идёт на сеть. */
export function gateHandlers(list: RequestHandler[]): RequestHandler[] {
  for (const handler of list) {
    const originalRun = handler.run.bind(handler)
    handler.run = ((args: Parameters<RequestHandler['run']>[0]) => {
      if (!isMockIntercept(args.request)) return Promise.resolve(null)
      return originalRun(args)
    }) as RequestHandler['run']
  }
  return list
}
