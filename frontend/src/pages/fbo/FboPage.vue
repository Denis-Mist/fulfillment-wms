<script setup lang="ts">
import { computed, ref } from 'vue'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import { CalendarClock, FilterX, PackageOpen, Plus, Truck, Boxes } from '@lucide/vue'
import { fboApi } from '@/modules/fbo/api'
import { FBO_STATUSES } from '@/modules/fbo/labels'
import type { FboListItem, FboMarketplace, FboQuery, FboStatus } from '@/modules/fbo/types'
import { organizationsApi } from '@/modules/organizations/api'
import { useCabinet } from '@/shared/composables/useCabinet'
import { useRegistry } from '@/shared/composables/useRegistry'
import { formatDate, formatNumber } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import ChannelBadge from '@/shared/ui/ChannelBadge.vue'
import FilterChips from '@/shared/ui/FilterChips.vue'
import KpiCard from '@/shared/ui/KpiCard.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import RegistryTable from '@/shared/ui/RegistryTable.vue'
import SearchInput from '@/shared/ui/SearchInput.vue'
import type { ChipOption } from '@/shared/ui/types'
import ToneBadge from '@/pages/stock/ToneBadge.vue'
import FboCreateDialog from './FboCreateDialog.vue'
import FboDrawer from './FboDrawer.vue'

const session = useSessionStore()
const { isFulfillment, isSeller } = useCabinet()
const registry = useRegistry({
  filters: {
    status: undefined as FboStatus | undefined,
    marketplace: undefined as FboMarketplace | undefined,
    sellerId: undefined as string | undefined,
  },
  sortField: 'plannedAt',
})
const { state, filters, searchInput } = registry
const warehouseId = computed(() => session.warehouseId || undefined)
const query = computed<FboQuery>(() => ({ ...registry.query.value, warehouseId: warehouseId.value }))

const { data, isPending } = useQuery({
  queryKey: computed(() => ['fbo', session.organizationId, query.value]),
  queryFn: () => fboApi.list(query.value),
  placeholderData: keepPreviousData,
})
const { data: summary, isPending: summaryPending } = useQuery({
  queryKey: computed(() => ['fbo-summary', session.organizationId, warehouseId.value]),
  queryFn: () => fboApi.summary({ warehouseId: warehouseId.value }),
  placeholderData: keepPreviousData,
})
const { data: clients } = useQuery({
  queryKey: computed(() => ['clients', session.organizationId]),
  queryFn: () => organizationsApi.clients(),
  enabled: isFulfillment,
})
const clientOptions = computed(() => (clients.value ?? []).map((c) => ({ value: c.id, label: c.name })))
const mpOptions = [
  { value: 'wb', label: 'Wildberries' },
  { value: 'ozon', label: 'Ozon' },
]

const chips = computed<ChipOption<FboStatus>[]>(() => [
  { value: undefined, label: 'Все', count: summary.value?.total },
  ...(Object.keys(FBO_STATUSES) as FboStatus[]).map((s) => ({
    value: s,
    label: FBO_STATUSES[s].label,
    count: summary.value?.byStatus[s] ?? 0,
  })),
])

const canOperate = computed(
  () =>
    isFulfillment.value &&
    !!session.role &&
    ['fc_owner', 'fc_manager', 'fc_supervisor'].includes(session.role),
)
const canManage = computed(
  () => isSeller.value && !!session.role && ['seller_owner', 'seller_manager'].includes(session.role),
)
const selectedId = ref<string | null>(null)
const drawer = ref(false)
const creating = ref(false)
function open(item: Pick<FboListItem, 'id'>) {
  selectedId.value = item.id
  drawer.value = true
}
const overdue = (item: FboListItem) =>
  !['shipped', 'cancelled'].includes(item.status) && Date.parse(item.plannedAt) < Date.now() - 86_400_000
</script>

<template>
  <div>
    <PageHeader
      title="Поставки FBO"
      :description="
        isFulfillment
          ? 'Сборка и отгрузка поставок клиентов на склады Wildberries и Ozon'
          : 'Заявки на отгрузку товара с фулфилмента на склады маркетплейсов'
      "
    >
      <template v-if="canManage" #actions>
        <Button @click="creating = true"><Plus class="size-4" /> Новая поставка</Button>
      </template>
    </PageHeader>

    <div class="mb-5 grid grid-cols-2 gap-3 lg:grid-cols-4">
      <KpiCard
        label="Ждут сборки"
        :icon="PackageOpen"
        tone="info"
        :value="formatNumber(summary?.byStatus.confirmed ?? 0)"
        :loading="summaryPending"
      />
      <KpiCard
        label="В сборке"
        :icon="Boxes"
        tone="warn"
        :value="formatNumber(summary?.byStatus.assembling ?? 0)"
        :loading="summaryPending"
      />
      <KpiCard
        label="Отгрузка сегодня"
        :icon="CalendarClock"
        :tone="summary?.dueToday ? 'danger' : 'neutral'"
        :value="formatNumber(summary?.dueToday ?? 0)"
        hint="включая просроченные"
        :loading="summaryPending"
      />
      <KpiCard
        label="Штук в работе"
        :icon="Truck"
        :value="formatNumber(summary?.unitsInWork ?? 0)"
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
        <SearchInput v-model="searchInput" placeholder="Номер или склад назначения…" />
        <Select
          :model-value="filters.marketplace"
          :options="mpOptions"
          option-label="label"
          option-value="value"
          placeholder="Все маркетплейсы"
          show-clear
          class="w-full sm:w-52"
          @update:model-value="registry.setFilter('marketplace', $event ?? undefined)"
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
      empty-title="Поставок не найдено"
      @page="registry.onPage"
      @sort="registry.onSort"
      @row-click="open"
    >
      <Column field="number" header="Поставка" sortable class="min-w-36">
        <template #body="{ data: item }">
          <div class="flex items-center gap-2 font-medium whitespace-nowrap text-heading">
            <ChannelBadge :channel="item.marketplace" />{{ item.number }}
          </div>
          <div class="text-xs whitespace-nowrap text-secondary">{{ item.destination }}</div>
        </template>
      </Column>
      <Column header="Статус" class="min-w-40">
        <template #body="{ data: item }">
          <ToneBadge
            :tone="FBO_STATUSES[item.status as FboStatus].tone"
            :label="FBO_STATUSES[item.status as FboStatus].label"
          />
        </template>
      </Column>
      <Column v-if="isFulfillment" field="sellerName" header="Клиент" class="min-w-36" />
      <Column field="units" header="Сборка" sortable class="min-w-32">
        <template #body="{ data: item }">
          <div class="text-sm whitespace-nowrap tabular">{{ item.packed }} / {{ item.units }} шт</div>
          <div class="mt-1 h-1.5 w-24 overflow-hidden rounded-full bg-surface-200 dark:bg-surface-700">
            <div
              class="h-full rounded-full bg-primary-500"
              :style="{ width: `${(item.packed / item.units) * 100}%` }"
            />
          </div>
        </template>
      </Column>
      <Column field="skuCount" header="SKU" class="tabular" />
      <Column field="boxes" header="Коробов" class="tabular" />
      <Column field="plannedAt" header="Отгрузка" sortable class="min-w-28">
        <template #body="{ data: item }">
          <span
            class="text-sm whitespace-nowrap"
            :class="overdue(item) ? 'font-medium text-rose-600' : 'text-secondary'"
          >
            {{ formatDate(item.plannedAt) }}
          </span>
        </template>
      </Column>
    </RegistryTable>

    <FboDrawer :id="selectedId" v-model:visible="drawer" :can-operate="canOperate" :can-manage="canManage" />
    <FboCreateDialog v-if="canManage" v-model:visible="creating" @created="open({ id: $event })" />
  </div>
</template>
