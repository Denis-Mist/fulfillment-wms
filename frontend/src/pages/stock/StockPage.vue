<script setup lang="ts">
import { computed, ref } from 'vue'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import { Boxes, FilterX, PackageCheck, PackageX, Lock, Grid3x3 } from '@lucide/vue'
import { FC_OFFICE_ROLES } from '@/modules/auth/roles'
import { organizationsApi } from '@/modules/organizations/api'
import { stockApi } from '@/modules/stock/api'
import { STOCK_STATUSES } from '@/modules/stock/labels'
import type { StockItem, StockQuery, StockStatus } from '@/modules/stock/types'
import { useCabinet } from '@/shared/composables/useCabinet'
import { useRegistry } from '@/shared/composables/useRegistry'
import { formatNumber } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import FilterChips from '@/shared/ui/FilterChips.vue'
import KpiCard from '@/shared/ui/KpiCard.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import RegistryTable from '@/shared/ui/RegistryTable.vue'
import SearchInput from '@/shared/ui/SearchInput.vue'
import type { ChipOption } from '@/shared/ui/types'
import SellerStockView from './SellerStockView.vue'
import StockDrawer from './StockDrawer.vue'
import ToneBadge from './ToneBadge.vue'

const session = useSessionStore()
const { isFulfillment } = useCabinet()

const registry = useRegistry({
  filters: {
    status: undefined as StockStatus | undefined,
    sellerId: undefined as string | undefined,
    zone: undefined as string | undefined,
  },
  sortField: 'locationCode',
  sortOrder: 'asc',
})
const { state, filters, searchInput } = registry

const warehouseId = computed(() => session.warehouseId || undefined)
const listQuery = computed<StockQuery>(() => ({ ...registry.query.value, warehouseId: warehouseId.value }))

const { data, isPending } = useQuery({
  queryKey: computed(() => ['stock', session.organizationId, listQuery.value]),
  queryFn: () => stockApi.list(listQuery.value),
  placeholderData: keepPreviousData,
  enabled: isFulfillment,
})

const summaryQuery = computed(() => ({ warehouseId: warehouseId.value, sellerId: filters.sellerId }))
const { data: summary, isPending: summaryPending } = useQuery({
  queryKey: computed(() => ['stock-summary', session.organizationId, summaryQuery.value]),
  queryFn: () => stockApi.summary(summaryQuery.value),
  placeholderData: keepPreviousData,
  enabled: isFulfillment,
})

const { data: clients } = useQuery({
  queryKey: computed(() => ['clients', session.organizationId]),
  queryFn: () => organizationsApi.clients(),
  enabled: isFulfillment,
  staleTime: 5 * 60_000,
})
const clientOptions = computed(() => (clients.value ?? []).map((c) => ({ value: c.id, label: c.name })))

const ZONES = [
  { value: 'RCV', label: 'Приёмка' },
  { value: 'STR', label: 'Хранение' },
  { value: 'PCK', label: 'Отбор' },
  { value: 'PAK', label: 'Упаковка' },
  { value: 'SHP', label: 'Отгрузка' },
  { value: 'DEF', label: 'Брак и карантин' },
]

const chips = computed<ChipOption<StockStatus>[]>(() => [
  { value: undefined, label: 'Все' },
  { value: 'available', label: 'Доступно' },
  { value: 'reserved', label: 'С резервом' },
  { value: 'defect', label: 'Брак', danger: true },
])

const canOperate = computed(() => !!session.role && FC_OFFICE_ROLES.includes(session.role))
const selected = ref<StockItem | null>(null)
const drawer = ref(false)
function openItem(item: StockItem) {
  selected.value = item
  drawer.value = true
}
</script>

<template>
  <SellerStockView v-if="!isFulfillment" />
  <div v-else>
    <PageHeader
      title="Остатки"
      description="Товар клиентов по ячейкам: доступно, в резерве под заказы и в браке"
    />

    <div class="mb-5 grid grid-cols-2 gap-3 lg:grid-cols-5">
      <KpiCard
        label="Всего на складе"
        :icon="Boxes"
        :value="summary ? `${formatNumber(summary.quantity)} шт` : undefined"
        :hint="summary ? `${formatNumber(summary.skuCount)} SKU` : undefined"
        :loading="summaryPending"
      />
      <KpiCard
        label="Доступно"
        :icon="PackageCheck"
        tone="success"
        :value="summary ? formatNumber(summary.available) : undefined"
        :loading="summaryPending"
      />
      <KpiCard
        label="В резерве"
        :icon="Lock"
        tone="info"
        :value="summary ? formatNumber(summary.reserved) : undefined"
        :loading="summaryPending"
      />
      <KpiCard
        label="Брак и карантин"
        :icon="PackageX"
        tone="danger"
        :value="summary ? formatNumber(summary.defect) : undefined"
        :loading="summaryPending"
      />
      <KpiCard
        class="col-span-2 lg:col-span-1"
        label="Свободных ячеек"
        :icon="Grid3x3"
        :value="summary ? formatNumber(summary.emptyLocations) : undefined"
        :loading="summaryPending"
      />
    </div>

    <div class="mb-4 space-y-3">
      <FilterChips
        :model-value="filters.status"
        :options="chips"
        @update:model-value="registry.setFilter('status', $event)"
      />
      <div class="flex flex-wrap items-center gap-2.5">
        <SearchInput v-model="searchInput" placeholder="Артикул, товар, штрихкод, ячейка…" />
        <Select
          :model-value="filters.sellerId"
          :options="clientOptions"
          option-label="label"
          option-value="value"
          placeholder="Все клиенты"
          filter
          show-clear
          class="w-full sm:w-56"
          @update:model-value="registry.setFilter('sellerId', $event ?? undefined)"
        />
        <Select
          :model-value="filters.zone"
          :options="ZONES"
          option-label="label"
          option-value="value"
          placeholder="Все зоны"
          show-clear
          class="w-full sm:w-44"
          @update:model-value="registry.setFilter('zone', $event ?? undefined)"
        />
        <Button
          v-if="registry.hasActiveFilters.value"
          text
          severity="secondary"
          @click="registry.resetFilters()"
        >
          <FilterX class="size-4" />
          Сбросить
        </Button>
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
      empty-title="Остатков не найдено"
      @page="registry.onPage"
      @sort="registry.onSort"
      @row-click="openItem"
    >
      <Column field="locationCode" header="Ячейка" sortable class="min-w-28">
        <template #body="{ data: item }">
          <div class="font-mono text-sm font-semibold text-heading">{{ item.locationCode }}</div>
          <div class="text-xs text-secondary">зона {{ item.zoneCode }}</div>
        </template>
      </Column>
      <Column field="sku" header="Товар" sortable class="min-w-56">
        <template #body="{ data: item }">
          <div class="line-clamp-1 text-sm font-medium text-heading">{{ item.productName }}</div>
          <div class="text-xs text-secondary">{{ item.sku }}</div>
        </template>
      </Column>
      <Column field="sellerName" header="Клиент" sortable class="min-w-36" />
      <Column v-if="!warehouseId" field="warehouseName" header="Склад" class="min-w-32" />
      <Column field="quantity" header="Всего" sortable class="min-w-20">
        <template #body="{ data: item }"
          ><span class="tabular">{{ formatNumber(item.quantity) }}</span></template
        >
      </Column>
      <Column field="reserved" header="Резерв" sortable class="min-w-20">
        <template #body="{ data: item }">
          <span class="tabular" :class="item.reserved ? 'text-sky-600' : 'text-secondary'">{{
            formatNumber(item.reserved)
          }}</span>
        </template>
      </Column>
      <Column field="available" header="Доступно" sortable class="min-w-24">
        <template #body="{ data: item }"
          ><span class="tabular font-medium text-heading">{{ formatNumber(item.available) }}</span></template
        >
      </Column>
      <Column header="Статус" class="min-w-32">
        <template #body="{ data: item }"
          ><ToneBadge v-bind="STOCK_STATUSES[item.status as StockStatus]"
        /></template>
      </Column>
    </RegistryTable>

    <StockDrawer v-model:visible="drawer" :item="selected" :can-operate="canOperate" />
  </div>
</template>
