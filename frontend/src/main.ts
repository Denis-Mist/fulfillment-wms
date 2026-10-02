import '@/styles/main.css'
import { createApp } from 'vue'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'
import ToastService from 'primevue/toastservice'
import ConfirmationService from 'primevue/confirmationservice'
import Tooltip from 'primevue/tooltip'
import { VueQueryPlugin } from '@tanstack/vue-query'
import App from './App.vue'
import { primeLocaleRu } from './app/locale'
import { queryClient } from './app/query'
import { router } from './app/router'
import { AppPreset, DARK_MODE_CLASS } from './app/theme'
import { configureHttp } from './shared/api/http'
import { useSessionStore } from './stores/session'

async function bootstrap() {
  if (import.meta.env.VITE_USE_MOCKS === 'true') {
    const { worker } = await import('./mocks/browser')
    await worker.start({ onUnhandledFrame: 'bypass', quiet: true })
  }

  const app = createApp(App)
  const pinia = createPinia()
  app.use(pinia)

  const session = useSessionStore(pinia)
  configureHttp({
    getToken: () => session.token || null,
    getOrganizationId: () => session.organizationId || null,
    onUnauthorized: () => {
      session.logout()
      void router.push({ name: 'login' })
    },
  })

  app.use(PrimeVue, {
    ripple: false,
    locale: primeLocaleRu,
    theme: {
      preset: AppPreset,
      options: {
        darkModeSelector: `.${DARK_MODE_CLASS}`,
        cssLayer: { name: 'primevue', order: 'theme, base, primevue' },
      },
    },
  })
  app.use(ToastService)
  app.use(ConfirmationService)
  app.directive('tooltip', Tooltip)
  app.use(VueQueryPlugin, { queryClient })
  app.use(router)
  app.mount('#app')
}

void bootstrap()
