import { isAlwaysMocks } from '@/app/demoMode'
import { resetMockData } from './reset'

let started = false
let startPromise: Promise<void> | null = null

function isUnitTestEnv(): boolean {
  if (typeof window === 'undefined') return true
  const ua = typeof navigator !== 'undefined' ? navigator.userAgent : ''
  return /jsdom|HappyDOM/i.test(ua)
}

function canUseServiceWorker(): boolean {
  return typeof navigator !== 'undefined' && 'serviceWorker' in navigator
}

export async function startMockWorker(): Promise<void> {
  if (started) return
  if (isUnitTestEnv()) {
    // Node/jsdom (unit-тесты): работает сервер MSW, SW нет
    started = true
    return
  }
  if (!canUseServiceWorker()) {
    throw new Error('Не удалось запустить демо в этом браузере')
  }
  if (!startPromise) {
    startPromise = (async () => {
      const { worker } = await import('./browser')
      await worker.start({
        onUnhandledFrame: 'bypass',
        quiet: true,
        serviceWorker: { url: '/mockServiceWorker.js' },
      })
      started = true
    })().catch((error) => {
      startPromise = null
      started = false
      const wrapped = new Error('Не удалось запустить демо в этом браузере')
      wrapped.cause = error
      throw wrapped
    })
  }
  await startPromise
}

export async function stopMockWorker(): Promise<void> {
  if (isAlwaysMocks()) return
  if (!started) return
  if (canUseServiceWorker()) {
    const { worker } = await import('./browser')
    worker.stop()
  }
  started = false
  startPromise = null
}

export { resetMockData }

export function isMockWorkerStarted(): boolean {
  return started
}
