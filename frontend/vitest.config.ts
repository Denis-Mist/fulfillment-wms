import { fileURLToPath } from 'node:url'
import { defineConfig, mergeConfig } from 'vitest/config'
import viteConfig from './vite.config'

export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      environment: 'jsdom',
      environmentOptions: { jsdom: { url: 'http://localhost/' } },
      include: ['tests/unit/**/*.test.ts'],
      root: fileURLToPath(new URL('./', import.meta.url)),
      restoreMocks: true,
    },
  }),
)
