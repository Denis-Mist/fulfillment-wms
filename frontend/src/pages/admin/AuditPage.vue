<script setup lang="ts">
import { computed } from 'vue'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import { Lock } from '@lucide/vue'
import { adminApi } from '@/modules/admin/api'
import { AUDIT_CATEGORIES } from '@/modules/admin/labels'
import type { AuditCategory } from '@/modules/admin/types'
import { organizationsApi } from '@/modules/organizations/api'
import { TONE_CLASSES } from '@/shared/config/statuses'
import { useRegistry } from '@/shared/composables/useRegistry'
import { formatDateTime, formatRelative } from '@/shared/lib/format'
import FilterChips from '@/shared/ui/FilterChips.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import RegistryTable from '@/shared/ui/RegistryTable.vue'
import SearchInput from '@/shared/ui/SearchInput.vue'
import type { ChipOption } from '@/shared/ui/types'

const registry = useRegistry({
  filters: { category: undefined as AuditCategory | undefined, organizationId: undefined as string | undefined },
  sortField: 'at',
})
const { state, filters, searchInput } = registry

const { data, isPending } = useQuery({
  queryKey: computed(() => ['admin-audit', registry.query.value]),
  queryFn: () => adminApi.audit(registry.query.value),
  placeholderData: keepPreviousData,
})
const { data: orgs } = useQuery({
  queryKey: ['admin-organizations', 'all-options'],
  queryFn: () => organizationsApi.adminList({ page: 1, pageSize: 200, sortField: 'name', sortOrder: 'asc' }),
})
const orgOptions = computed(() => (orgs.value?.items ?? []).map((o) => ({ value: o.id, label: o.name })))

const chips: ChipOption<AuditCategory>[] = [
  { value: undefined, label: 'Все' },
  ...(Object.keys(AUDIT_CATEGORIES) as AuditCategory[]).map((value) => ({ value, label: AUDIT_CATEGORIES[value].label })),
]
</script>

<template>
  <div>
    <PageHeader title="Журнал действий" description="Неизменяемый журнал операций: записи только добавляются, редактирование и удаление недоступны" />

    <div class="mb-4 space-y-3">
      <FilterChips :model-value="filters.category" :options="chips" @update:model-value="registry.setFilter('category', $event)" />
      <div class="flex flex-wrap items-center gap-2.5">
        <SearchInput v-model="searchInput" placeholder="Действие, документ или исполнитель" />
        <Select
          :model-value="filters.organizationId"
          :options="orgOptions"
          option-label="label"
          option-value="value"
          placeholder="Все организации"
          filter
          show-clear
          class="w-full sm:w-64"
          @update:model-value="registry.setFilter('organizationId', $event ?? undefined)"
        />
      </div>
    </div>

    <RegistryTable
      :items="data?.items ?? []"
      :total="data?.total ?? 0"
      :loading="isPending"
      :first="registry.first.value"
      :rows="state.pageSize"
      :sort-field="state.sortField"
      :sort-order="registry.tableSortOrder.value"
      empty-title="Событий не найдено"
      @page="registry.onPage"
      @sort="registry.onSort"
    >
      <Column field="at" header="Время" sortable class="whitespace-nowrap">
        <template #body="{ data: e }">
          <div class="tabular text-sm text-heading">{{ formatDateTime(e.at) }}</div>
          <div class="text-xs text-secondary">{{ formatRelative(e.at) }}</div>
        </template>
      </Column>
      <Column header="Событие" class="min-w-64">
        <template #body="{ data: e }">
          <div class="flex items-center gap-2">
            <span class="shrink-0 rounded-full px-2 py-0.5 text-xs font-medium" :class="TONE_CLASSES[AUDIT_CATEGORIES[e.category as AuditCategory].tone].badge">
              {{ AUDIT_CATEGORIES[e.category as AuditCategory].label }}
            </span>
            <span class="text-sm font-medium text-heading">{{ e.action }}</span>
          </div>
          <div v-if="e.details" class="mt-0.5 text-xs text-secondary">{{ e.details }}</div>
        </template>
      </Column>
      <Column header="Объект" class="whitespace-nowrap">
        <template #body="{ data: e }"><span class="font-mono text-xs">{{ e.entity ?? '—' }}</span></template>
      </Column>
      <Column header="Исполнитель" class="min-w-44">
        <template #body="{ data: e }">
          <div class="text-sm text-heading">{{ e.actorName }}</div>
          <div class="text-xs text-secondary">{{ e.organizationName ?? 'Платформа' }}</div>
        </template>
      </Column>
      <Column header="" class="w-px">
        <template #body><Lock v-tooltip.left="'Запись защищена от изменения'" class="size-3.5 text-surface-400" /></template>
      </Column>
    </RegistryTable>
  </div>
</template>
