import { computed } from 'vue'

/**
 * v2: демо — личная песочница в браузере, учебные мутации разрешены.
 * Композабл оставлен, чтобы не трогать десятки шаблонов: кнопки больше не блокируются.
 */
export function useDemoLock() {
  const isDemo = computed(() => false)
  return {
    isDemo,
    hint: undefined as string | undefined,
    lock: (alreadyDisabled = false) => alreadyDisabled,
  }
}
