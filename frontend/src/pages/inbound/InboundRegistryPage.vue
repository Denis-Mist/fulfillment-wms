<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import { FilterX, TriangleAlert } from '@lucide/vue'
import { inboundApi } from '@/modules/inbound/api'
import type { InboundListItem, InboundListQuery, InboundStatus } from '@/modules/inbound/types'
import { organizationsApi } from '@/modules/organizations/api'
import { INBOUND_STATUSES } from '@/shared/config/statuses'
import { useCabinet } from '@/shared/composables/useCabinet'
import { useRegistry } from '@/shared/composables/useRegistry'
import { formatDate, formatNumber, formatTime } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import FilterChips from '@/shared/ui/FilterChips.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import ProgressMeter from '@/shared/ui/ProgressMeter.vue'
import RegistryTable from '@/shared/ui/RegistryTable.vue'
import SearchInput from '@/shared/ui/SearchInput.vue'
import StatusTag from '@/shared/ui/StatusTag.vue'
import type { ChipOption } from '@/shared/ui/types'

const session = useSessionStore()
const router = useRouter()
const { isFulfillment, to } = useCabinet()

const registry = useRegistry({
  filters: {
    status: undefined as InboundStatus | undefined,
    sellerId: undefined as string | undefined,
  },
  sortField: 'expectedAt',
})
const { state, filters, searchInput } = registry

const warehouseId = computed(() => (isFulfillment.value ? session.warehouseId || undefined : undefined))
const listQuery = computed<InboundListQuery>(() => ({ ...registry.query.value, warehouseId: warehouseId.value }))
const summaryQuery = computed(() => ({
  search: state.search || undefined,
  sellerId: filters.sellerId,
  warehouseId: warehouseId.value,
}))

const { data, isPending } = useQuery({
  queryKey: computed(() => ['inbound', session.organizationId, listQuery.value]),
  queryFn: () => inboundApi.list(listQuery.value),
  placeholderData: keepPreviousData,
})

const { data: summary } = useQuery({
  queryKey: computed(() => ['inbound-summary', session.organizationId, summaryQuery.value]),
  queryFn: () => inboundApi.summary(summaryQuery.value),
  placeholderData: keepPreviousData,
})

const { data: clients } = useQuery({
  queryKey: computed(() => ['clients', session.organizationId]),
  queryFn: () => organizationsApi.clients(),
  enabled: isFulfillment,
  staleTime: 5 * 60_000,
})

const STATUS_ORDER: InboundStatus[] = ['draft', 'in_transit', 'arrived', 'receiving', 'received', 'placed', 'cancelled']

const chips = computed<ChipOption<InboundStatus>[]>(() => [
  { value: undefined, label: 'Все', count: summary.value?.total },
  ...STATUS_ORDER.map((status) => ({
    value: status,
    label: INBOUND_STATUSES[status].label,
    count: summary.value?.byStatus[status] ?? 0,
  })),
])

const clientOptions = computed(() => (clients.value ?? []).map((c) => ({ value: c.id, label: c.name })))
const showWarehouse = computed(() => !isFulfillment.value || !session.warehouseId)

const openShipment = (item: InboundListItem) => router.push(to('inbound-detail', { id: item.id }))
</script>

<template>
  <div>
    <PageHeader
      :title="isFulfillment ? 'Поставки' : 'Поставки на фулфилмент'"
      :description="
        isFulfillment
          ? 'Входящие поставки клиентов: приёмка, расхождения и размещение'
          : 'Ваши поставки на склады фулфилмент-центров и результат их приёмки'
      "
    />

    <div class="mb-4 space-y-3">
      <FilterChips
        :model-value="filters.status"
        :options="chips"
        @update:model-value="registry.setFilter('status', $event)"
      />
      <div class="flex flex-wrap items-center gap-2.5">
        <SearchInput v-model="searchInput" placeholder="Номер поставки, клиент, склад…" />
        <Select
          v-if="isFulfillment"
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
        <Button v-if="registry.hasActiveFilters.value" text severity="secondary" @click="registry.resetFilters()">
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
      empty-title="Поставок не найдено"
      @page="registry.onPage"
      @sort="registry.onSort"
      @row-click="openShipment"
    >
      <Column field="number" header="Поставка" sortable class="min-w-36">
        <template #body="{ data: item }">
          <div class="font-semibold text-heading">{{ item.number }}</div>
          <div class="text-xs text-secondary">
            {{ item.packagesCount }} {{ item.packageType === 'pallets' ? 'палл.' : 'кор.' }}
          </div>
        </template>
      </Column>
      <Column v-if="isFulfillment" field="sellerName" header="Клиент" class="min-w-36" />
      <Column v-if="!isFulfillment" field="fulfillmentName" header="Фулфилмент" class="min-w-40" />
      <Column v-if="showWarehouse" field="warehouseName" header="Склад" class="min-w-32" />
      <Column header="Статус" class="min-w-40">
        <template #body="{ data: item }">
          <div class="flex items-center gap-2">
            <StatusTag domain="inbound" :status="item.status" />
            <TriangleAlert
              v-if="item.hasDiscrepancy"
              v-tooltip.top="'Есть расхождения при приёмке'"
              class="size-4 text-amber-500"
            />
          </div>
        </template>
      </Column>
      <Column field="expectedAt" header="Ожидается" sortable class="min-w-32">
        <template #body="{ data: item }">
          <div class="tabular text-sm text-heading">{{ formatDate(item.expectedAt) }}</div>
          <div class="tabular text-xs text-secondary">к {{ formatTime(item.expectedAt) }}</div>
        </template>
      </Column>
      <Column header="Товаров" class="min-w-28">
        <template #body="{ data: item }">
          <div class="tabular text-sm text-heading">{{ formatNumber(item.expectedUnits) }} шт</div>
          <div class="text-xs text-secondary">{{ item.linesCount }} SKU</div>
        </template>
      </Column>
      <Column header="Принято" class="min-w-40">
        <template #body="{ data: item }">
          <template v-if="item.receivedUnits">
            <div class="tabular mb-1.5 text-sm text-heading">
              {{ formatNumber(item.receivedUnits) }}
              <span class="text-secondary">/ {{ formatNumber(item.expectedUnits) }}</span>
            </div>
            <ProgressMeter
              :value="item.receivedUnits"
              :max="item.expectedUnits"
              :tone="item.hasDiscrepancy ? 'warn' : 'success'"
            />
          </template>
          <span v-else class="text-sm text-secondary">—</span>
        </template>
      </Column>
    </RegistryTable>
  </div>
</template>
