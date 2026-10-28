<script setup lang="ts" generic="T extends { id: string }">
import type { DataTablePageEvent, DataTableRowClickEvent, DataTableSortEvent } from 'primevue/datatable'
import { SearchX } from '@lucide/vue'
import { computed } from 'vue'
import { useMediaQuery } from '@vueuse/core'
import EmptyState from './EmptyState.vue'

const isMobile = useMediaQuery('(max-width: 639px)')
const paginatorTemplate = computed(() =>
  isMobile.value
    ? 'PrevPageLink CurrentPageReport NextPageLink'
    : 'FirstPageLink PrevPageLink PageLinks NextPageLink LastPageLink RowsPerPageDropdown CurrentPageReport',
)

withDefaults(
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

const emit = defineEmits<{
  page: [event: DataTablePageEvent]
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
  <div class="surface-card overflow-hidden">
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
        <EmptyState v-else :icon="SearchX" :title="emptyTitle" :description="emptyDescription" compact>
          <slot name="empty-action" />
        </EmptyState>
      </template>
    </DataTable>
  </div>
</template>
