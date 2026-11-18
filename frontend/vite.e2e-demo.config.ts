import { defineConfig, mergeConfig } from 'vite'
import base from './vite.config'

/**
 * Только для e2e демо-режима v2 (playwright.demo.config.ts). Сборка — та же, что на проде (основной vite.config.ts),
 * здесь добавлен лишь прокси `vite preview` для `/api` на реальный API стенда, чтобы фронт ходил в API с того же
 * origin, как за nginx на проде (`VITE_API_BASE_URL=/api`).
 *
 * E2E_DEMO_API_TARGET — адрес API (по умолчанию http://127.0.0.1:5080). Каждый проксируемый запрос получает свой
 * X-Forwarded-For (E2E_DEMO_XFF=0 — выключить), чтобы rate limit login/register не мешал прогону.
 */
const target = (process.env.E2E_DEMO_API_TARGET ?? 'http://127.0.0.1:5080').replace(/\/$/, '')
const useXff = process.env.E2E_DEMO_XFF !== '0'
let seq = 0

export default mergeConfig(
  base,
  defineConfig({
    preview: {
      proxy: {
        '/api': {
          target,
          changeOrigin: true,
          configure: (proxy) => {
            if (!useXff) return
            proxy.on('proxyReq', (proxyReq) => {
              seq++
              proxyReq.setHeader('X-Forwarded-For', `10.78.${Math.floor(seq / 250) % 250}.${(seq % 250) + 1}`)
            })
          },
        },
        '/health': { target, changeOrigin: true },
      },
    },
  }),
)
