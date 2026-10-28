<script setup lang="ts">
import { computed, ref } from 'vue'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import { Store, Warehouse } from '@lucide/vue'
import type { OrganizationStatus, OrganizationType } from '@/modules/auth/types'
import { organizationsApi } from '@/modules/organizations/api'
import { ORGANIZATION_STATUSES, ORGANIZATION_TYPES } from '@/shared/config/statuses'
import { useRegistry } from '@/shared/composables/useRegistry'
import { formatDate } from '@/shared/lib/format'
import FilterChips from '@/shared/ui/FilterChips.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import RegistryTable from '@/shared/ui/RegistryTable.vue'
import SearchInput from '@/shared/ui/SearchInput.vue'
import StatusTag from '@/shared/ui/StatusTag.vue'
import type { ChipOption } from '@/shared/ui/types'
import OrganizationDrawer from './OrganizationDrawer.vue'

const registry = useRegistry({
  filters: {
    type: undefined as OrganizationType | undefined,
    status: undefined as OrganizationStatus | undefined,
  },
  sortField: 'name',
  sortOrder: 'asc',
})
const { state, filters, searchInput } = registry

const { data, isPending } = useQuery({
  queryKey: computed(() => ['admin-organizations', registry.query.value]),
  queryFn: () => organizationsApi.adminList(registry.query.value),
  placeholderData: keepPreviousData,
})
const { data: summary } = useQuery({
  queryKey: computed(() => ['admin-organizations-summary', state.search, filters.type]),
  queryFn: () => organizationsApi.adminSummary({ search: state.search || undefined, type: filters.type }),
  placeholderData: keepPreviousData,
})

const typeChips = computed<ChipOption<OrganizationType>[]>(() => [
  { value: undefined, label: 'Все' },
  { value: 'fulfillment', label: 'Фулфилмент-центры' },
  { value: 'seller', label: 'Селлеры' },
])
const statusOptions = computed(() =>
  (Object.keys(ORGANIZATION_STATUSES) as OrganizationStatus[]).map((s) => ({
    value: s,
    label: `${ORGANIZATION_STATUSES[s].label} (${summary.value?.byStatus[s] ?? 0})`,
  })),
)

const selected = ref<string | null>(null)
</script>

<template>
  <div>
    <PageHeader title="Организации" description="Все фулфилмент-центры и селлеры платформы, их пользователи, склады и договоры" />

    <div class="mb-4 space-y-3">
      <FilterChips :model-value="filters.type" :options="typeChips" @update:model-value="registry.setFilter('type', $event)" />
      <div class="flex flex-wrap items-center gap-2.5">
        <SearchInput v-model="searchInput" placeholder="Название, ИНН или email владельца" />
        <Select
          :model-value="filters.status"
          :options="statusOptions"
          option-label="label"
          option-value="value"
          placeholder="Все статусы"
          show-clear
          class="w-full sm:w-60"
          @update:model-value="registry.setFilter('status', $event ?? undefined)"
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
      clickable
      empty-title="Организаций не найдено"
      @page="registry.onPage"
      @sort="registry.onSort"
      @row-click="selected = $event.id"
    >
      <Column field="name" header="Организация" sortable class="min-w-56">
        <template #body="{ data: org }">
          <div class="flex items-center gap-3">
            <span class="grid size-9 shrink-0 place-items-center rounded-lg bg-surface-100 text-surface-500 dark:bg-surface-800">
              <component :is="org.type === 'fulfillment' ? Warehouse : Store" class="size-[18px]" />
            </span>
            <div>
              <div class="font-semibold text-heading">{{ org.name }}</div>
              <div class="text-xs text-secondary">{{ ORGANIZATION_TYPES[org.type as OrganizationType] }} · ИНН {{ org.inn }}</div>
            </div>
          </div>
        </template>
      </Column>
      <Column field="city" header="Город" sortable />
      <Column header="Владелец" class="min-w-48">
        <template #body="{ data: org }">
          <div class="text-sm text-heading">{{ org.ownerName }}</div>
          <div class="text-xs text-secondary">{{ org.ownerEmail }}</div>
        </template>
      </Column>
      <Column field="createdAt" header="С нами с" sortable class="whitespace-nowrap">
        <template #body="{ data: org }"><span class="tabular text-sm">{{ formatDate(org.createdAt) }}</span></template>
      </Column>
      <Column header="Статус">
        <template #body="{ data: org }"><StatusTag domain="organization" :status="org.status" /></template>
      </Column>
    </RegistryTable>

    <OrganizationDrawer v-model:org-id="selected" />
  </div>
</template>
