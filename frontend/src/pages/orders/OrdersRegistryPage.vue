<script setup lang="ts">
import { computed, ref } from 'vue'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import { FilterX, TriangleAlert } from '@lucide/vue'
import { ordersApi } from '@/modules/orders/api'
import type { OrderListItem, OrderListQuery, OrderStatus, SalesChannel } from '@/modules/orders/types'
import { organizationsApi } from '@/modules/organizations/api'
import { ORDER_STATUSES, SALES_CHANNELS } from '@/shared/config/statuses'
import { useCabinet } from '@/shared/composables/useCabinet'
import { useRegistry } from '@/shared/composables/useRegistry'
import { formatDateTime, formatMoney, formatNumber, formatRelative } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import ChannelBadge from '@/shared/ui/ChannelBadge.vue'
import FilterChips from '@/shared/ui/FilterChips.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import RegistryTable from '@/shared/ui/RegistryTable.vue'
import SearchInput from '@/shared/ui/SearchInput.vue'
import StatusTag from '@/shared/ui/StatusTag.vue'
import type { ChipOption } from '@/shared/ui/types'
import OrderPreviewDrawer from './OrderPreviewDrawer.vue'

type StatusFilter = OrderStatus | 'overdue'

const session = useSessionStore()
const { isFulfillment } = useCabinet()

const registry = useRegistry({
  filters: {
    status: undefined as StatusFilter | undefined,
    channel: undefined as SalesChannel | undefined,
    sellerId: undefined as string | undefined,
  },
  sortField: 'createdAt',
})
const { state, filters, searchInput } = registry

const warehouseId = computed(() => (isFulfillment.value ? session.warehouseId || undefined : undefined))

const listQuery = computed<OrderListQuery>(() => ({
  ...registry.query.value,
  warehouseId: warehouseId.value,
}))
const summaryQuery = computed(() => ({
  search: state.search || undefined,
  channel: filters.channel,
  sellerId: filters.sellerId,
  warehouseId: warehouseId.value,
}))

const { data, isPending } = useQuery({
  queryKey: computed(() => ['orders', session.organizationId, listQuery.value]),
  queryFn: () => ordersApi.list(listQuery.value),
  placeholderData: keepPreviousData,
})

const { data: summary } = useQuery({
  queryKey: computed(() => ['orders-summary', session.organizationId, summaryQuery.value]),
  queryFn: () => ordersApi.summary(summaryQuery.value),
  placeholderData: keepPreviousData,
})

const { data: clients } = useQuery({
  queryKey: computed(() => ['clients', session.organizationId]),
  queryFn: () => organizationsApi.clients(),
  enabled: isFulfillment,
  staleTime: 5 * 60_000,
})

const STATUS_ORDER: OrderStatus[] = [
  'new',
  'reserved',
  'out_of_stock',
  'picking',
  'picked',
  'packed',
  'shipped',
  'cancelled',
]

const chips = computed<ChipOption<StatusFilter>[]>(() => [
  { value: undefined, label: 'Все', count: summary.value?.total },
  { value: 'overdue', label: 'Просрочены', count: summary.value?.overdue, danger: true },
  ...STATUS_ORDER.map((status) => ({
    value: status,
    label: ORDER_STATUSES[status].label,
    count: summary.value?.byStatus[status] ?? 0,
    danger: status === 'out_of_stock',
  })),
])

const channelOptions = Object.entries(SALES_CHANNELS).map(([value, meta]) => ({ value, label: meta.label }))
const clientOptions = computed(() => (clients.value ?? []).map((c) => ({ value: c.id, label: c.name })))
const showWarehouse = computed(() => !isFulfillment.value || !session.warehouseId)

const selectedId = ref<string | null>(null)
const openOrder = (order: OrderListItem) => (selectedId.value = order.id)
</script>

<template>
  <div>
    <PageHeader
      title="Заказы"
      :description="
        isFulfillment
          ? 'FBS-заказы маркетплейсов и собственные заказы клиентов'
          : 'Заказы ваших покупателей, которые собирает фулфилмент-центр'
      "
    />

    <div class="mb-4 space-y-3">
      <FilterChips
        :model-value="filters.status"
        :options="chips"
        @update:model-value="registry.setFilter('status', $event)"
      />
      <div class="flex flex-wrap items-center gap-2.5">
        <SearchInput v-model="searchInput" placeholder="Номер заказа, номер МП, город…" />
        <Select
          :model-value="filters.channel"
          :options="channelOptions"
          option-label="label"
          option-value="value"
          placeholder="Все каналы"
          show-clear
          class="w-full sm:w-52"
          @update:model-value="registry.setFilter('channel', $event ?? undefined)"
        />
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
      empty-title="Заказов не найдено"
      @page="registry.onPage"
      @sort="registry.onSort"
      @row-click="openOrder"
    >
      <Column field="number" header="Заказ" sortable>
        <template #body="{ data: order }">
          <div class="font-semibold text-heading">{{ order.number }}</div>
          <div class="text-xs text-secondary">{{ order.externalNumber }}</div>
        </template>
      </Column>
      <Column header="Канал" class="w-24">
        <template #body="{ data: order }">
          <ChannelBadge :channel="order.channel" />
        </template>
      </Column>
      <Column v-if="isFulfillment" field="sellerName" header="Клиент" />
      <Column v-if="showWarehouse" field="warehouseName" header="Склад" />
      <Column header="Статус">
        <template #body="{ data: order }">
          <StatusTag domain="order" :status="order.status" />
        </template>
      </Column>
      <Column field="createdAt" header="Создан" sortable>
        <template #body="{ data: order }">
          <div class="tabular text-sm text-heading">{{ formatDateTime(order.createdAt) }}</div>
          <div class="text-xs text-secondary">{{ formatRelative(order.createdAt) }}</div>
        </template>
      </Column>
      <Column field="shipBy" header="Отгрузить до" sortable>
        <template #body="{ data: order }">
          <div
            v-if="order.isOverdue"
            class="tabular inline-flex items-center gap-1.5 text-sm font-medium text-rose-600 dark:text-rose-400"
          >
            <TriangleAlert class="size-4" />
            {{ formatDateTime(order.shipBy) }}
          </div>
          <div
            v-else
            class="tabular text-sm"
            :class="
              order.status === 'shipped' || order.status === 'cancelled' ? 'text-secondary' : 'text-heading'
            "
          >
            {{ formatDateTime(order.shipBy) }}
          </div>
          <div v-if="order.isOverdue" class="text-xs text-rose-500">просрочен</div>
          <div v-else-if="order.shippedAt" class="text-xs text-secondary">
            отгружен {{ formatDateTime(order.shippedAt) }}
          </div>
        </template>
      </Column>
      <Column field="amount" header="Сумма" sortable class="text-right" header-class="[&>div]:justify-end">
        <template #body="{ data: order }">
          <div class="tabular font-medium text-heading">{{ formatMoney(order.amount) }}</div>
          <div class="tabular text-xs text-secondary">
            {{ order.linesCount }} поз. · {{ formatNumber(order.unitsCount) }} шт
          </div>
        </template>
      </Column>
      <template #card="{ item: order }">
        <div class="flex items-start justify-between gap-3">
          <div class="min-w-0">
            <div class="font-semibold text-heading">{{ order.number }}</div>
            <div class="truncate text-xs text-secondary">
              {{ order.externalNumber }}<template v-if="isFulfillment"> · {{ order.sellerName }}</template>
            </div>
          </div>
          <StatusTag domain="order" :status="order.status" />
        </div>
        <div class="mt-3 flex items-end justify-between gap-3 text-sm">
          <div class="min-w-0">
            <div class="flex items-center gap-2">
              <ChannelBadge :channel="order.channel" />
              <span v-if="showWarehouse" class="truncate text-xs text-secondary">{{
                order.warehouseName
              }}</span>
            </div>
            <div
              class="tabular mt-1.5 text-xs"
              :class="order.isOverdue ? 'font-medium text-rose-600 dark:text-rose-400' : 'text-secondary'"
            >
              {{ order.isOverdue ? 'Просрочен · ' : 'Отгрузить до ' }}{{ formatDateTime(order.shipBy) }}
            </div>
          </div>
          <div class="shrink-0 text-right">
            <div class="tabular font-medium text-heading">{{ formatMoney(order.amount) }}</div>
            <div class="tabular text-xs text-secondary">
              {{ order.linesCount }} поз. · {{ formatNumber(order.unitsCount) }} шт
            </div>
          </div>
        </div>
      </template>
    </RegistryTable>

    <OrderPreviewDrawer v-model:order-id="selectedId" :show-seller="isFulfillment" />
  </div>
</template>
