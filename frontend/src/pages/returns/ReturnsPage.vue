<script setup lang="ts">
import { computed, ref } from 'vue'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import { FilterX, PackageCheck, PackageX, Truck, Undo2 } from '@lucide/vue'
import { organizationsApi } from '@/modules/organizations/api'
import { returnsApi } from '@/modules/returns/api'
import { RETURN_GRADES, RETURN_REASONS, RETURN_STATUSES } from '@/modules/returns/labels'
import type { ReturnListItem, ReturnQuery, ReturnReason, ReturnStatus } from '@/modules/returns/types'
import { useCabinet } from '@/shared/composables/useCabinet'
import { useRegistry } from '@/shared/composables/useRegistry'
import { formatNumber, formatRelative } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import ChannelBadge from '@/shared/ui/ChannelBadge.vue'
import FilterChips from '@/shared/ui/FilterChips.vue'
import KpiCard from '@/shared/ui/KpiCard.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import RegistryTable from '@/shared/ui/RegistryTable.vue'
import SearchInput from '@/shared/ui/SearchInput.vue'
import type { ChipOption } from '@/shared/ui/types'
import ToneBadge from '@/pages/stock/ToneBadge.vue'
import ReturnDrawer from './ReturnDrawer.vue'

const session = useSessionStore()
const { isFulfillment } = useCabinet()
const registry = useRegistry({
  filters: {
    status: undefined as ReturnStatus | undefined,
    reason: undefined as ReturnReason | undefined,
    sellerId: undefined as string | undefined,
  },
  sortField: 'createdAt',
})
const { state, filters, searchInput } = registry
const warehouseId = computed(() => session.warehouseId || undefined)
const query = computed<ReturnQuery>(() => ({ ...registry.query.value, warehouseId: warehouseId.value }))

const { data, isPending } = useQuery({
  queryKey: computed(() => ['returns', session.organizationId, query.value]),
  queryFn: () => returnsApi.list(query.value),
  placeholderData: keepPreviousData,
})
const { data: summary, isPending: summaryPending } = useQuery({
  queryKey: computed(() => ['returns-summary', session.organizationId, warehouseId.value, filters.sellerId]),
  queryFn: () => returnsApi.summary({ warehouseId: warehouseId.value, sellerId: filters.sellerId }),
  placeholderData: keepPreviousData,
})
const { data: clients } = useQuery({
  queryKey: computed(() => ['clients', session.organizationId]),
  queryFn: () => organizationsApi.clients(),
  enabled: isFulfillment,
})
const clientOptions = computed(() => (clients.value ?? []).map((c) => ({ value: c.id, label: c.name })))
const reasonOptions = Object.entries(RETURN_REASONS).map(([value, label]) => ({ value, label }))

const chips = computed<ChipOption<ReturnStatus>[]>(() => [
  { value: undefined, label: 'Все', count: summary.value?.total },
  ...(Object.keys(RETURN_STATUSES) as ReturnStatus[]).map((s) => ({
    value: s,
    label: RETURN_STATUSES[s].label,
    count: summary.value?.byStatus[s] ?? 0,
  })),
])
const RESOLUTION = {
  restocked: { label: 'В продажу', tone: RETURN_GRADES.as_new.tone },
  defect: { label: 'Брак', tone: RETURN_GRADES.defect.tone },
  mixed: { label: 'Частично', tone: 'warn' as const },
}

const canOperate = computed(
  () =>
    isFulfillment.value &&
    !!session.role &&
    ['fc_owner', 'fc_manager', 'fc_supervisor', 'fc_receiver'].includes(session.role),
)
const selectedId = ref<string | null>(null)
const drawer = ref(false)
function open(item: ReturnListItem) {
  selectedId.value = item.id
  drawer.value = true
}
</script>

<template>
  <div>
    <PageHeader
      title="Возвраты"
      :description="
        isFulfillment
          ? 'Приёмка возвратов и невыкупов, оценка состояния и возврат в остаток'
          : 'Возвраты и невыкупы по вашим заказам и результат оценки товара'
      "
    />
    <div class="mb-5 grid grid-cols-2 gap-3 lg:grid-cols-4">
      <KpiCard
        label="Ожидается"
        :icon="Truck"
        :value="formatNumber(summary?.byStatus.expected ?? 0)"
        :loading="summaryPending"
      />
      <KpiCard
        label="На оценке"
        :icon="Undo2"
        tone="warn"
        :value="formatNumber(summary?.byStatus.received ?? 0)"
        :loading="summaryPending"
      />
      <KpiCard
        label="Возвращено в продажу"
        :icon="PackageCheck"
        tone="success"
        :value="summary ? `${summary.restockRate}%` : undefined"
        hint="от оценённых позиций"
        :loading="summaryPending"
      />
      <KpiCard
        label="Брак и утилизация"
        :icon="PackageX"
        tone="danger"
        :value="summary ? `${formatNumber(summary.defectUnits)} шт` : undefined"
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
        <SearchInput v-model="searchInput" placeholder="Номер возврата или заказа…" />
        <Select
          :model-value="filters.reason"
          :options="reasonOptions"
          option-label="label"
          option-value="value"
          placeholder="Все причины"
          show-clear
          class="w-full sm:w-56"
          @update:model-value="registry.setFilter('reason', $event ?? undefined)"
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
          <FilterX class="size-4" /> Сбросить
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
      empty-title="Возвратов не найдено"
      @page="registry.onPage"
      @sort="registry.onSort"
      @row-click="open"
    >
      <Column field="number" header="Возврат" sortable class="min-w-36">
        <template #body="{ data: item }">
          <div class="font-medium whitespace-nowrap text-heading">{{ item.number }}</div>
          <div class="text-xs whitespace-nowrap text-secondary">заказ {{ item.orderNumber }}</div>
        </template>
      </Column>
      <Column header="Статус" class="min-w-40">
        <template #body="{ data: item }">
          <ToneBadge
            :tone="RETURN_STATUSES[item.status as ReturnStatus].tone"
            :label="RETURN_STATUSES[item.status as ReturnStatus].label"
          />
        </template>
      </Column>
      <Column header="Причина" class="min-w-44">
        <template #body="{ data: item }">
          <div class="flex items-center gap-2 text-sm whitespace-nowrap">
            <ChannelBadge :channel="item.channel" />{{ RETURN_REASONS[item.reason as ReturnReason] }}
          </div>
        </template>
      </Column>
      <Column v-if="isFulfillment" field="sellerName" header="Клиент" class="min-w-36" />
      <Column field="units" header="Шт" sortable class="tabular" />
      <Column header="Результат" class="min-w-28">
        <template #body="{ data: item }">
          <ToneBadge
            v-if="item.resolution"
            :tone="RESOLUTION[item.resolution as keyof typeof RESOLUTION].tone"
            :label="RESOLUTION[item.resolution as keyof typeof RESOLUTION].label"
          />
          <span v-else class="text-xs text-secondary">—</span>
        </template>
      </Column>
      <Column field="createdAt" header="Создан" sortable class="min-w-28">
        <template #body="{ data: item }"
          ><span class="text-sm whitespace-nowrap text-secondary">{{
            formatRelative(item.createdAt)
          }}</span></template
        >
      </Column>
    </RegistryTable>

    <ReturnDrawer :id="selectedId" v-model:visible="drawer" :can-operate="canOperate" />
  </div>
</template>
