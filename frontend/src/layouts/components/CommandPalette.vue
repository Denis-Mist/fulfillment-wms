<script setup lang="ts">
import { computed, nextTick, ref, watch, type Component } from 'vue'
import { useRouter, type RouteLocationRaw } from 'vue-router'
import { refDebounced } from '@vueuse/core'
import { useQuery } from '@tanstack/vue-query'
import { CornerDownLeft, PackagePlus, Search, ShoppingCart, Tag } from '@lucide/vue'
import { cabinetDef, routeName } from '@/app/sections'
import { searchApi, type SearchResult, type SearchResultType } from '@/modules/search/api'
import { useCabinet } from '@/shared/composables/useCabinet'
import { useSessionStore } from '@/stores/session'

const visible = defineModel<boolean>('visible', { default: false })

const session = useSessionStore()
const router = useRouter()
const { cabinet, to } = useCabinet()

const input = ref<HTMLInputElement>()
const query = ref('')
const debounced = refDebounced(query, 250)
const activeIndex = ref(0)

const { data, isFetching } = useQuery({
  queryKey: computed(() => ['search', session.organizationId, debounced.value]),
  queryFn: ({ signal }) => searchApi.search(debounced.value, signal),
  enabled: computed(() => visible.value && debounced.value.trim().length >= 2),
  staleTime: 10_000,
})

const TYPE_META: Record<SearchResultType, { label: string; icon: Component }> = {
  order: { label: 'Заказы', icon: ShoppingCart },
  inbound: { label: 'Поставки', icon: PackagePlus },
  product: { label: 'Товары', icon: Tag },
}

interface PaletteItem {
  key: string
  group: string
  icon: Component
  title: string
  subtitle?: string
  target: RouteLocationRaw
}

const TARGET_SECTION: Record<SearchResultType, string> = {
  inbound: 'inbound-detail',
  order: 'orders',
  product: 'products',
}

function targetOf(result: SearchResult): RouteLocationRaw | null {
  if (!router.hasRoute(routeName(cabinet.value, TARGET_SECTION[result.type]))) return null
  if (result.type === 'inbound') return to('inbound-detail', { id: result.id })
  if (result.type === 'order') return to('orders', undefined, { search: result.title })
  return to('products')
}

const navigationItems = computed<PaletteItem[]>(() =>
  cabinetDef(cabinet.value)
    .groups.flatMap((g) => g.sections)
    .filter((s) => !s.hidden && (!s.roles || (session.role && s.roles.includes(session.role))))
    .map((s) => ({
      key: `nav-${s.name}`,
      group: 'Перейти в раздел',
      icon: s.icon ?? Search,
      title: s.title,
      target: { name: routeName(cabinet.value, s.name) },
    })),
)

const items = computed<PaletteItem[]>(() => {
  const q = query.value.trim().toLowerCase()
  if (q.length < 2) return navigationItems.value
  const sections = navigationItems.value.filter((item) => item.title.toLowerCase().includes(q))
  const results = (data.value ?? []).flatMap((r) => {
    const target = targetOf(r)
    if (!target) return []
    return [
      {
        key: `${r.type}-${r.id}`,
        group: TYPE_META[r.type].label,
        icon: TYPE_META[r.type].icon,
        title: r.title,
        subtitle: r.subtitle,
        target,
      },
    ]
  })
  return [...sections, ...results]
})

const grouped = computed(() => {
  const map = new Map<string, { item: PaletteItem; index: number }[]>()
  items.value.forEach((item, index) => {
    const list = map.get(item.group) ?? []
    list.push({ item, index })
    map.set(item.group, list)
  })
  return [...map.entries()]
})

watch(items, () => (activeIndex.value = 0))
watch(visible, async (open) => {
  if (!open) return
  query.value = ''
  await nextTick()
  input.value?.focus()
})

function select(item: PaletteItem | undefined) {
  if (!item) return
  visible.value = false
  void router.push(item.target)
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'ArrowDown') {
    event.preventDefault()
    activeIndex.value = Math.min(items.value.length - 1, activeIndex.value + 1)
  } else if (event.key === 'ArrowUp') {
    event.preventDefault()
    activeIndex.value = Math.max(0, activeIndex.value - 1)
  } else if (event.key === 'Enter') {
    event.preventDefault()
    select(items.value[activeIndex.value])
  }
}
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    dismissable-mask
    :show-header="false"
    position="top"
    :draggable="false"
    class="mt-[10vh] w-[640px] max-w-[calc(100vw-2rem)]"
    :pt="{ content: { class: '!p-0' } }"
  >
    <div class="flex items-center gap-3 border-b border-surface-100 px-4 dark:border-surface-800">
      <Search class="size-5 text-surface-400" />
      <input
        ref="input"
        v-model="query"
        placeholder="Номер заказа, поставки, SKU, штрихкод или раздел…"
        class="h-14 flex-1 bg-transparent text-[15px] text-heading outline-none placeholder:text-surface-400"
        @keydown="onKeydown"
      />
      <ProgressSpinner v-if="isFetching" style="width: 18px; height: 18px" stroke-width="5" />
      <kbd class="rounded-md border border-surface-200 px-1.5 py-0.5 text-[11px] text-surface-400 dark:border-surface-700">
        Esc
      </kbd>
    </div>

    <div class="max-h-[420px] overflow-y-auto p-2">
      <div
        v-if="!items.length && query.trim().length >= 2 && !isFetching"
        class="py-10 text-center text-sm text-secondary"
      >
        Ничего не найдено по запросу «{{ query }}»
      </div>
      <div v-for="[group, entries] in grouped" :key="group" class="mb-2 last:mb-0">
        <div class="px-2.5 pt-2 pb-1 text-[11px] font-semibold tracking-wider text-surface-400 uppercase">
          {{ group }}
        </div>
        <button
          v-for="{ item, index } in entries"
          :key="item.key"
          type="button"
          class="flex w-full items-center gap-3 rounded-lg px-2.5 py-2 text-left"
          :class="index === activeIndex ? 'bg-primary-50 dark:bg-primary-500/15' : ''"
          @mouseenter="activeIndex = index"
          @click="select(item)"
        >
          <span
            class="grid size-8 shrink-0 place-items-center rounded-lg"
            :class="
              index === activeIndex
                ? 'bg-white text-primary dark:bg-surface-900'
                : 'bg-surface-100 text-surface-500 dark:bg-surface-800'
            "
          >
            <component :is="item.icon" class="size-4" />
          </span>
          <span class="min-w-0 flex-1">
            <span class="block truncate text-sm font-medium text-heading">{{ item.title }}</span>
            <span v-if="item.subtitle" class="block truncate text-xs text-secondary">{{ item.subtitle }}</span>
          </span>
          <CornerDownLeft v-if="index === activeIndex" class="size-4 text-primary" />
        </button>
      </div>
    </div>

    <div
      class="flex items-center gap-4 border-t border-surface-100 px-4 py-2.5 text-[11px] text-surface-400 dark:border-surface-800"
    >
      <span><kbd class="font-sans">↑↓</kbd> выбрать</span>
      <span><kbd class="font-sans">Enter</kbd> открыть</span>
      <span class="ml-auto">Поиск работает по вашей организации</span>
    </div>
  </Dialog>
</template>
