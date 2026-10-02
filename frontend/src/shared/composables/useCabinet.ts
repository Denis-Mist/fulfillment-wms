import { computed } from 'vue'
import { useRoute, type RouteLocationRaw } from 'vue-router'
import { routeName } from '@/app/sections'
import type { Cabinet } from '@/modules/auth/types'
import { useSessionStore } from '@/stores/session'

/** Текущий кабинет и построение ссылок внутри него — страницы переиспользуются между кабинетами */
export function useCabinet() {
  const route = useRoute()
  const session = useSessionStore()
  const cabinet = computed<Cabinet>(() => route.meta.cabinet ?? session.cabinet ?? 'fc')

  function to(section: string, params?: Record<string, string>, query?: Record<string, string>): RouteLocationRaw {
    return { name: routeName(cabinet.value, section), params, query }
  }

  return {
    cabinet,
    isFulfillment: computed(() => cabinet.value === 'fc'),
    isSeller: computed(() => cabinet.value === 'seller'),
    to,
  }
}
