import { fileURLToPath } from 'node:url'
import { defineConfig, mergeConfig } from 'vitest/config'
import viteConfig from './vite.config'

export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      env: { VITE_USE_MOCKS: 'true' },
      environment: 'jsdom',
      environmentOptions: { jsdom: { url: 'http://localhost/' } },
      include: ['tests/unit/**/*.test.ts'],
      root: fileURLToPath(new URL('./', import.meta.url)),
      restoreMocks: true,
    },
  }),
)
