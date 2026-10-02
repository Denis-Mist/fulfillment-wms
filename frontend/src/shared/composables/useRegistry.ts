import { computed, reactive, ref, watch } from 'vue'
import { useRoute, useRouter, type LocationQuery } from 'vue-router'
import { watchDebounced } from '@vueuse/core'
import type { SortOrder } from '@/shared/api/types'

type Filters = Record<string, string | undefined>

interface RegistryOptions<F extends Filters> {
  filters: F
  pageSize?: number
  sortField?: string
  sortOrder?: SortOrder
}

const str = (value: LocationQuery[string]) => (Array.isArray(value) ? value[0] : value) ?? undefined

/**
 * Состояние реестра (поиск, фильтры, сортировка, пагинация) с синхронизацией в URL:
 * ссылкой на отфильтрованный список можно поделиться, «назад» возвращает к тем же фильтрам.
 */
export function useRegistry<F extends Filters>(options: RegistryOptions<F>) {
  const route = useRoute()
  const router = useRouter()
  const defaults = { pageSize: options.pageSize ?? 25, sortField: options.sortField, sortOrder: options.sortOrder ?? 'desc' }

  const state = reactive({
    page: Number(str(route.query.page)) || 1,
    pageSize: Number(str(route.query.pageSize)) || defaults.pageSize,
    search: str(route.query.search) ?? '',
    sortField: str(route.query.sortField) ?? defaults.sortField,
    sortOrder: (str(route.query.sortOrder) as SortOrder | undefined) ?? defaults.sortOrder,
  })

  const filters = reactive({ ...options.filters }) as F
  for (const key of Object.keys(options.filters)) {
    const value = str(route.query[key])
    if (value) (filters as Filters)[key] = value
  }

  const searchInput = ref(state.search)
  watchDebounced(
    searchInput,
    (value) => {
      state.search = value.trim()
      state.page = 1
    },
    { debounce: 300 },
  )

  function setFilter<K extends keyof F>(key: K, value: F[K]) {
    filters[key] = value
    state.page = 1
  }

  function resetFilters() {
    Object.assign(filters, options.filters)
    searchInput.value = ''
    state.search = ''
    state.page = 1
  }

  const hasActiveFilters = computed(
    () => !!state.search || Object.entries(filters).some(([key, value]) => value !== options.filters[key]),
  )

  function onPage(event: { page: number; rows: number }) {
    state.page = event.page + 1
    state.pageSize = event.rows
  }

  function onSort(event: { sortField?: unknown; sortOrder?: number | null }) {
    state.sortField = typeof event.sortField === 'string' ? event.sortField : defaults.sortField
    state.sortOrder = event.sortOrder === 1 ? 'asc' : 'desc'
    state.page = 1
  }

  const query = computed(() => ({
    page: state.page,
    pageSize: state.pageSize,
    search: state.search || undefined,
    sortField: state.sortField,
    sortOrder: state.sortOrder,
    ...filters,
  }))

  watch(
    query,
    (value) => {
      const next: Record<string, string> = {}
      for (const [key, raw] of Object.entries(value)) {
        if (raw === undefined || raw === '') continue
        if (key === 'page' && raw === 1) continue
        if (key === 'pageSize' && raw === defaults.pageSize) continue
        if (key === 'sortField' && raw === defaults.sortField) continue
        if (key === 'sortOrder' && raw === defaults.sortOrder) continue
        next[key] = String(raw)
      }
      void router.replace({ query: next })
    },
    { deep: true },
  )

  return {
    state,
    filters,
    searchInput,
    query,
    hasActiveFilters,
    setFilter,
    resetFilters,
    onPage,
    onSort,
    first: computed(() => (state.page - 1) * state.pageSize),
    tableSortOrder: computed(() => (state.sortOrder === 'asc' ? 1 : -1)),
  }
}
