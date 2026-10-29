<script setup lang="ts" generic="T extends { id: string }">
import type { DataTablePageEvent, DataTableRowClickEvent, DataTableSortEvent } from 'primevue/datatable'
import { SearchX } from '@lucide/vue'
import { computed } from 'vue'
import { useMediaQuery } from '@vueuse/core'
import { useRoute } from 'vue-router'
import EmptyState from './EmptyState.vue'

const isMobile = useMediaQuery('(max-width: 639px)')
// Карточный вид на <768px, если страница передала слот #card
const isNarrow = useMediaQuery('(max-width: 767px)')
const cardMode = computed(() => isNarrow.value && !!slots.card)
const paginatorTemplate = computed(() =>
  isMobile.value
    ? 'PrevPageLink CurrentPageReport NextPageLink'
    : 'FirstPageLink PrevPageLink PageLinks NextPageLink LastPageLink RowsPerPageDropdown CurrentPageReport',
)

const props = withDefaults(
  defineProps<{
    items: T[]
    total: number
    loading: boolean
    first: number
    rows: number
    sortField?: string
    sortOrder?: 1 | -1
    clickable?: boolean
    emptyTitle?: string
    emptyDescription?: string
  }>(),
  {
    sortOrder: -1,
    clickable: false,
    emptyTitle: 'Ничего не найдено',
    emptyDescription: 'Попробуйте изменить фильтры или поисковый запрос.',
  },
)

// FE-S2-5: при активном поиске (useRegistry держит его в ?search=) пустой список — «Ничего не найдено»
const route = useRoute()
const searchQuery = computed(() => (typeof route.query.search === 'string' ? route.query.search.trim() : ''))
const shownTitle = computed(() => (searchQuery.value ? 'Ничего не найдено' : props.emptyTitle))
const shownDescription = computed(() =>
  searchQuery.value ? `По запросу «${searchQuery.value}» ничего нет. Измените запрос или сбросьте фильтры.` : props.emptyDescription,
)

const slots = defineSlots<{
  default?: () => unknown
  card?: (props: { item: T }) => unknown
  'empty-action'?: () => unknown
}>()

const emit = defineEmits<{
  page: [event: DataTablePageEvent | { page: number; rows: number; first: number }]
  sort: [event: DataTableSortEvent]
  rowClick: [item: T]
}>()

function onRowClick(event: DataTableRowClickEvent) {
  const target = event.originalEvent.target as HTMLElement | null
  if (target?.closest('a, button')) return
  emit('rowClick', event.data as T)
}
</script>

<template>
  <div v-if="cardMode" data-testid="registry-cards">
    <div v-if="loading && !items.length" class="space-y-3">
      <Skeleton v-for="i in 6" :key="i" height="6rem" border-radius="1rem" />
    </div>
    <div v-else-if="!items.length" class="surface-card">
      <EmptyState :icon="SearchX" :title="shownTitle" :description="shownDescription" compact>
        <slot name="empty-action" />
      </EmptyState>
    </div>
    <ul v-else class="space-y-3" :class="loading && 'opacity-60'">
      <li v-for="item in items" :key="item.id">
        <component
          :is="clickable ? 'button' : 'div'"
          :type="clickable ? 'button' : undefined"
          class="surface-card block w-full p-4 text-left"
          :class="
            clickable &&
            'transition-colors hover:border-primary-300 active:bg-surface-50 dark:active:bg-surface-800'
          "
          @click="clickable && emit('rowClick', item)"
        >
          <slot name="card" :item="item" />
        </component>
      </li>
    </ul>
    <Paginator
      v-if="total > rows"
      class="mt-3 !rounded-2xl"
      :first="first"
      :rows="rows"
      :total-records="total"
      template="PrevPageLink CurrentPageReport NextPageLink"
      current-page-report-template="{first}–{last} из {totalRecords}"
      @page="emit('page', $event)"
    />
  </div>
  <div v-else class="surface-card overflow-hidden">
    <DataTable
      :value="items"
      lazy
      :paginator="total > 0"
      :first="first"
      :rows="rows"
      :total-records="total"
      :loading="loading && items.length > 0"
      :sort-field="sortField"
      :sort-order="sortOrder"
      data-key="id"
      :rows-per-page-options="[25, 50, 100]"
      :paginator-template="paginatorTemplate"
      current-page-report-template="{first}–{last} из {totalRecords}"
      row-hover
      scrollable
      :class="clickable && 'registry-table'"
      @page="emit('page', $event)"
      @sort="emit('sort', $event)"
      @row-click="clickable && onRowClick($event)"
    >
      <slot />
      <template #empty>
        <div v-if="loading" class="space-y-3 py-2">
          <Skeleton v-for="i in 8" :key="i" height="2.25rem" />
        </div>
        <EmptyState v-else :icon="SearchX" :title="shownTitle" :description="shownDescription" compact>
          <slot name="empty-action" />
        </EmptyState>
      </template>
    </DataTable>
  </div>
</template>
