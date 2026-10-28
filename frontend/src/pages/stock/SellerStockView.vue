<script setup lang="ts">
import { computed, ref } from 'vue'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import { Boxes, CalendarClock, PackageCheck, PackageX, Lock } from '@lucide/vue'
import { stockApi } from '@/modules/stock/api'
import type { ProductStock } from '@/modules/stock/types'
import { useRegistry } from '@/shared/composables/useRegistry'
import { formatNumber, pluralize } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import FilterChips from '@/shared/ui/FilterChips.vue'
import KpiCard from '@/shared/ui/KpiCard.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import RegistryTable from '@/shared/ui/RegistryTable.vue'
import SearchInput from '@/shared/ui/SearchInput.vue'
import type { ChipOption } from '@/shared/ui/types'

const session = useSessionStore()
const registry = useRegistry({
  filters: { lowStock: undefined as string | undefined },
  sortField: 'daysOfCover',
  sortOrder: 'asc',
})
const { state, filters, searchInput } = registry

const { data, isPending } = useQuery({
  queryKey: computed(() => ['stock', 'products', session.organizationId, registry.query.value]),
  queryFn: () => stockApi.products(registry.query.value),
  placeholderData: keepPreviousData,
})
const { data: summary, isPending: summaryPending } = useQuery({
  queryKey: computed(() => ['stock-summary', session.organizationId]),
  queryFn: () => stockApi.summary({}),
})

const chips = computed<ChipOption<string>[]>(() => [
  { value: undefined, label: 'Все товары' },
  { value: 'true', label: 'Заканчиваются (< 14 дней)', danger: true },
])

function coverTone(days: number | null) {
  if (days === null) return 'text-secondary'
  if (days < 7) return 'text-red-600 font-semibold'
  if (days < 14) return 'text-amber-600 font-semibold'
  return 'text-heading'
}
function coverLabel(days: number | null) {
  if (days === null) return 'нет продаж'
  if (days > 365) return '> года'
  return `${days} ${pluralize(days, ['день', 'дня', 'дней'])}`
}

const expanded = ref<Record<string, boolean>>({})
const toggle = (p: ProductStock) =>
  void (expanded.value = { ...expanded.value, [p.productId]: !expanded.value[p.productId] })
</script>

<template>
  <div>
    <PageHeader
      title="Остатки"
      description="Ваш товар на складах фулфилмент-центров и прогноз, на сколько дней его хватит"
    />

    <div class="mb-5 grid grid-cols-2 gap-3 lg:grid-cols-4">
      <KpiCard
        label="Всего на складах"
        :icon="Boxes"
        :value="summary ? `${formatNumber(summary.quantity)} шт` : undefined"
        :hint="summary ? `${formatNumber(summary.skuCount)} SKU` : undefined"
        :loading="summaryPending"
      />
      <KpiCard
        label="Доступно к продаже"
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
        label="Брак"
        :icon="PackageX"
        tone="danger"
        :value="summary ? formatNumber(summary.defect) : undefined"
        :loading="summaryPending"
      />
    </div>

    <div class="mb-4 flex flex-wrap items-center gap-2.5">
      <FilterChips
        :model-value="filters.lowStock"
        :options="chips"
        @update:model-value="registry.setFilter('lowStock', $event)"
      />
      <SearchInput v-model="searchInput" placeholder="Артикул или название…" />
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
      empty-title="Товаров не найдено"
      @page="registry.onPage"
      @sort="registry.onSort"
      @row-click="toggle"
    >
      <Column field="name" header="Товар" sortable class="min-w-60">
        <template #body="{ data: p }">
          <div class="line-clamp-1 text-sm font-medium text-heading">{{ p.name }}</div>
          <div class="text-xs text-secondary">
            {{ p.sku }} · {{ p.warehouses.length }}
            {{ pluralize(p.warehouses.length, ['склад', 'склада', 'складов']) }}
          </div>
          <ul v-if="expanded[p.productId]" class="mt-2 space-y-1 border-l-2 border-default pl-3">
            <li v-for="w in p.warehouses" :key="w.warehouseId" class="flex justify-between gap-3 text-xs">
              <span class="text-secondary">{{ w.fulfillmentName }} · {{ w.warehouseName }}</span>
              <span class="tabular text-heading"
                >{{ formatNumber(w.available)
                }}<span v-if="w.reserved" class="text-sky-600"> +{{ w.reserved }} рез.</span></span
              >
            </li>
          </ul>
        </template>
      </Column>
      <Column field="available" header="Доступно" sortable class="min-w-24">
        <template #body="{ data: p }"
          ><span class="tabular font-medium text-heading">{{ formatNumber(p.available) }}</span></template
        >
      </Column>
      <Column field="reserved" header="Резерв" sortable class="min-w-20">
        <template #body="{ data: p }"
          ><span class="tabular text-secondary">{{ formatNumber(p.reserved) }}</span></template
        >
      </Column>
      <Column field="defect" header="Брак" sortable class="min-w-20">
        <template #body="{ data: p }"
          ><span class="tabular" :class="p.defect ? 'text-red-600' : 'text-secondary'">{{
            formatNumber(p.defect)
          }}</span></template
        >
      </Column>
      <Column field="avgDailySales" header="Продажи в день" sortable class="min-w-28">
        <template #body="{ data: p }"
          ><span class="tabular">{{ formatNumber(p.avgDailySales) }}</span></template
        >
      </Column>
      <Column field="daysOfCover" header="Хватит на" sortable class="min-w-28">
        <template #body="{ data: p }">
          <span class="inline-flex items-center gap-1.5 tabular text-sm" :class="coverTone(p.daysOfCover)">
            <CalendarClock v-if="p.daysOfCover !== null && p.daysOfCover < 14" class="size-3.5" />
            {{ coverLabel(p.daysOfCover) }}
          </span>
        </template>
      </Column>
    </RegistryTable>
  </div>
</template>
