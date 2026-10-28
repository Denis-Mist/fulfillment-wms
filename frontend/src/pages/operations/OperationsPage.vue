<script setup lang="ts">
import { computed, ref } from 'vue'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import { ArrowLeftRight, ClipboardCheck, FilterX, Trash2 } from '@lucide/vue'
import { FC_OFFICE_ROLES } from '@/modules/auth/roles'
import { stockApi } from '@/modules/stock/api'
import { ERROR_REASONS, OPERATION_TYPES } from '@/modules/stock/labels'
import type { OperationQuery, OperationType, StockOperation } from '@/modules/stock/types'
import { useRegistry } from '@/shared/composables/useRegistry'
import { formatDate, formatNumber, formatTime } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import FilterChips from '@/shared/ui/FilterChips.vue'
import KpiCard from '@/shared/ui/KpiCard.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import ProgressMeter from '@/shared/ui/ProgressMeter.vue'
import RegistryTable from '@/shared/ui/RegistryTable.vue'
import SearchInput from '@/shared/ui/SearchInput.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'
import type { ChipOption } from '@/shared/ui/types'
import StockActionDialog from '../stock/StockActionDialog.vue'
import ToneBadge from '../stock/ToneBadge.vue'

const session = useSessionStore()
const registry = useRegistry({
  filters: { type: undefined as OperationType | undefined },
  sortField: 'at',
  sortOrder: 'desc',
})
const { state, filters, searchInput } = registry

const warehouseId = computed(() => session.warehouseId || undefined)
const listQuery = computed<OperationQuery>(() => ({
  ...registry.query.value,
  warehouseId: warehouseId.value,
}))

const { data, isPending } = useQuery({
  queryKey: computed(() => ['stock-operations', session.organizationId, listQuery.value]),
  queryFn: () => stockApi.operations(listQuery.value),
  placeholderData: keepPreviousData,
})
const { data: summary, isPending: summaryPending } = useQuery({
  queryKey: computed(() => ['stock-operations-summary', session.organizationId, warehouseId.value]),
  queryFn: () => stockApi.operationSummary(warehouseId.value),
  placeholderData: keepPreviousData,
})

const chips = computed<ChipOption<OperationType>[]>(() => [
  { value: undefined, label: 'Все' },
  ...(Object.keys(OPERATION_TYPES) as OperationType[]).map((type) => ({
    value: type,
    label: OPERATION_TYPES[type].label,
  })),
])

const maxErrors = computed(() => Math.max(1, ...(summary.value?.errors.map((e) => e.count) ?? [1])))
const totalErrors = computed(() => summary.value?.errors.reduce((s, e) => s + e.count, 0) ?? 0)

const canOperate = computed(() => !!session.role && FC_OFFICE_ROLES.includes(session.role))
const action = ref<OperationType>('move')
const dialog = ref(false)
function open(type: OperationType) {
  action.value = type
  dialog.value = true
}

function describe(op: StockOperation): string {
  if (op.type === 'move') return `${op.fromLocationCode} → ${op.toLocationCode}`
  if (op.type === 'inventory') return `${op.fromLocationCode}: учёт ${op.expected}, факт ${op.counted}`
  return `${op.fromLocationCode} · ${op.reason ? ERROR_REASONS[op.reason] : ''}`
}
</script>

<template>
  <div>
    <PageHeader
      title="Внутренние операции"
      description="Перемещения, инвентаризация и списания — каждая операция фиксируется с автором и временем"
    >
      <template v-if="canOperate" #actions>
        <Button severity="secondary" outlined @click="open('move')"
          ><ArrowLeftRight class="size-4" />Переместить</Button
        >
        <Button severity="secondary" outlined @click="open('inventory')"
          ><ClipboardCheck class="size-4" />Инвентаризация</Button
        >
        <Button severity="danger" outlined @click="open('writeoff')"><Trash2 class="size-4" />Списать</Button>
      </template>
    </PageHeader>

    <div class="mb-5 grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.3fr)]">
      <div class="grid grid-cols-2 gap-3">
        <KpiCard
          class="col-span-2"
          label="Операций за 30 дней"
          :icon="ArrowLeftRight"
          :value="summary ? formatNumber(summary.last30Days) : undefined"
          :loading="summaryPending"
        />
        <KpiCard
          label="Перемещений"
          :icon="ArrowLeftRight"
          tone="info"
          :value="summary ? formatNumber(summary.byType.move) : undefined"
          :loading="summaryPending"
        />
        <KpiCard
          label="Инвентаризаций"
          :icon="ClipboardCheck"
          tone="progress"
          :value="summary ? formatNumber(summary.byType.inventory) : undefined"
          :loading="summaryPending"
        />
      </div>
      <SectionCard
        title="Ошибки и потери за 30 дней"
        :description="`Всего ${formatNumber(totalErrors)} случаев — основа отчёта по качеству склада`"
      >
        <div v-if="summaryPending" class="space-y-3">
          <Skeleton v-for="i in 4" :key="i" height="1.5rem" />
        </div>
        <ul v-else class="space-y-3">
          <li v-for="e in summary?.errors" :key="e.reason">
            <div class="mb-1 flex justify-between gap-3 text-sm">
              <span class="text-heading">{{ ERROR_REASONS[e.reason] }}</span>
              <span class="tabular text-secondary">{{ e.count }} · −{{ formatNumber(e.quantity) }} шт</span>
            </div>
            <ProgressMeter :value="e.count" :max="maxErrors" tone="warn" />
          </li>
        </ul>
      </SectionCard>
    </div>

    <div class="mb-4 space-y-3">
      <FilterChips
        :model-value="filters.type"
        :options="chips"
        @update:model-value="registry.setFilter('type', $event)"
      />
      <div class="flex flex-wrap items-center gap-2.5">
        <SearchInput v-model="searchInput" placeholder="Номер, артикул, ячейка, сотрудник…" />
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
      empty-title="Операций не найдено"
      @page="registry.onPage"
      @sort="registry.onSort"
    >
      <Column field="at" header="Когда" sortable class="min-w-28">
        <template #body="{ data: op }">
          <div class="tabular whitespace-nowrap text-sm text-heading">{{ formatDate(op.at) }}</div>
          <div class="tabular whitespace-nowrap text-xs text-secondary">{{ formatTime(op.at) }} · {{ op.number }}</div>
        </template>
      </Column>
      <Column header="Тип" class="min-w-36">
        <template #body="{ data: op }"
          ><ToneBadge v-bind="OPERATION_TYPES[op.type as OperationType]"
        /></template>
      </Column>
      <Column field="sku" header="Товар" sortable class="min-w-52">
        <template #body="{ data: op }">
          <div class="line-clamp-1 text-sm text-heading">{{ op.productName }}</div>
          <div class="text-xs text-secondary">{{ op.sku }} · {{ op.sellerName }}</div>
        </template>
      </Column>
      <Column header="Детали" class="min-w-48">
        <template #body="{ data: op }">
          <div class="font-mono text-xs text-heading">{{ describe(op) }}</div>
          <div v-if="op.comment" class="text-xs text-secondary">{{ op.comment }}</div>
        </template>
      </Column>
      <Column field="quantity" header="Кол-во" sortable class="min-w-20">
        <template #body="{ data: op }">
          <span
            class="tabular font-medium"
            :class="
              op.type === 'move'
                ? 'text-heading'
                : op.quantity < 0
                  ? 'text-red-600'
                  : op.quantity > 0
                    ? 'text-amber-600'
                    : 'text-secondary'
            "
          >
            {{ op.type !== 'move' && op.quantity > 0 ? '+' : '' }}{{ op.quantity }}
          </span>
        </template>
      </Column>
      <Column field="actorName" header="Сотрудник" sortable class="min-w-40" />
    </RegistryTable>

    <StockActionDialog v-model:visible="dialog" :type="action" :warehouse-id="warehouseId" />
  </div>
</template>
