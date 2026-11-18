import { computed } from 'vue'
import { DEMO_ACTION_HINT } from '@/modules/auth/demoPersonas'
import { useSessionStore } from '@/stores/session'

/** Блокировка стоп-лист действий в UI, когда сессия с isDemo */
export function useDemoLock() {
  const session = useSessionStore()
  const isDemo = computed(() => session.isDemo)
  return {
    isDemo,
    hint: DEMO_ACTION_HINT,
    lock: (alreadyDisabled = false) => alreadyDisabled || isDemo.value,
  }
}
