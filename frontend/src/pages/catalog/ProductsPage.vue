<script setup lang="ts">
import { computed, ref } from 'vue'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import { FileUp, FilterX, Plus, ScanBarcode, CalendarClock } from '@lucide/vue'
import { SELLER_ROLES } from '@/modules/auth/roles'
import { catalogApi } from '@/modules/catalog/api'
import type { ProductListItem, ProductQuery } from '@/modules/catalog/types'
import { organizationsApi } from '@/modules/organizations/api'
import { useCabinet } from '@/shared/composables/useCabinet'
import { useRegistry } from '@/shared/composables/useRegistry'
import { formatMoney, formatNumber } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import FilterChips from '@/shared/ui/FilterChips.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import RegistryTable from '@/shared/ui/RegistryTable.vue'
import SearchInput from '@/shared/ui/SearchInput.vue'
import type { ChipOption } from '@/shared/ui/types'
import ImportDialog from './ImportDialog.vue'
import ProductDrawer from './ProductDrawer.vue'
import ProductFormDialog from './ProductFormDialog.vue'

type Flag = NonNullable<ProductQuery['flag']>

const session = useSessionStore()
const { isFulfillment } = useCabinet()
const editable = computed(() => !!session.role && SELLER_ROLES.includes(session.role))

const registry = useRegistry({
  filters: {
    flag: undefined as Flag | undefined,
    sellerId: undefined as string | undefined,
    category: undefined as string | undefined,
  },
  sortField: 'name',
  sortOrder: 'asc',
})
const { state, filters, searchInput } = registry

const { data, isPending } = useQuery({
  queryKey: computed(() => ['catalog', 'products', session.organizationId, registry.query.value]),
  queryFn: () => catalogApi.products(registry.query.value),
  placeholderData: keepPreviousData,
})
const facetsQuery = computed(() => ({ sellerId: filters.sellerId, search: state.search || undefined }))
const { data: facets } = useQuery({
  queryKey: computed(() => ['catalog', 'facets', session.organizationId, facetsQuery.value]),
  queryFn: () => catalogApi.facets(facetsQuery.value),
  placeholderData: keepPreviousData,
})
const { data: clients } = useQuery({
  queryKey: computed(() => ['clients', session.organizationId]),
  queryFn: () => organizationsApi.clients(),
  enabled: isFulfillment,
  staleTime: 5 * 60_000,
})
const clientOptions = computed(() => (clients.value ?? []).map((c) => ({ value: c.id, label: c.name })))
const categoryOptions = computed(() =>
  (facets.value?.categories ?? []).map((c) => ({ value: c.value, label: `${c.value} · ${c.count}` })),
)

const chips = computed<ChipOption<Flag>[]>(() => [
  { value: undefined, label: 'Все', count: facets.value?.total },
  { value: 'marking', label: 'С маркировкой', count: facets.value?.marking },
  { value: 'expiry', label: 'Со сроком годности', count: facets.value?.expiry },
  { value: 'no_stock', label: 'Нет на складе', count: facets.value?.noStock, danger: true },
  { value: 'archived', label: 'Архив', count: facets.value?.archived },
])

const selected = ref<ProductListItem | null>(null)
const drawer = ref(false)
const form = ref(false)
const editing = ref<ProductListItem | null>(null)
const importing = ref(false)

function openItem(item: ProductListItem) {
  selected.value = item
  drawer.value = true
}
function create() {
  editing.value = null
  form.value = true
}
function edit(item: ProductListItem) {
  editing.value = item
  form.value = true
}
</script>

<template>
  <div>
    <PageHeader
      :title="isFulfillment ? 'Товары клиентов' : 'Мои товары'"
      :description="
        isFulfillment
          ? 'Каталог SKU всех клиентов: штрихкоды, габариты, маркировка и сроки годности'
          : 'Карточки товаров для приёмки и отгрузки: штрихкоды, габариты, маркировка'
      "
    >
      <template v-if="editable" #actions>
        <Button severity="secondary" outlined @click="importing = true"
          ><FileUp class="size-4" />Импорт CSV</Button
        >
        <Button @click="create"><Plus class="size-4" />Добавить товар</Button>
      </template>
    </PageHeader>

    <div class="mb-4 space-y-3">
      <FilterChips
        :model-value="filters.flag"
        :options="chips"
        @update:model-value="registry.setFilter('flag', $event)"
      />
      <div class="flex flex-wrap items-center gap-2.5">
        <SearchInput v-model="searchInput" placeholder="Артикул, название, штрихкод…" />
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
        <Select
          :model-value="filters.category"
          :options="categoryOptions"
          option-label="label"
          option-value="value"
          placeholder="Все категории"
          show-clear
          class="w-full sm:w-56"
          @update:model-value="registry.setFilter('category', $event ?? undefined)"
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
      empty-title="Товаров не найдено"
      @page="registry.onPage"
      @sort="registry.onSort"
      @row-click="openItem"
    >
      <Column field="name" header="Товар" sortable class="min-w-64">
        <template #body="{ data: item }">
          <div class="flex items-center gap-2">
            <span
              class="line-clamp-1 text-sm font-medium"
              :class="item.archived ? 'text-secondary line-through' : 'text-heading'"
              >{{ item.name }}</span
            >
            <ScanBarcode
              v-if="item.requiresMarking"
              v-tooltip.top="'Маркировка «Честный знак»'"
              class="size-3.5 shrink-0 text-sky-600"
            />
            <CalendarClock
              v-if="item.tracksExpiry"
              v-tooltip.top="'Учёт сроков годности'"
              class="size-3.5 shrink-0 text-amber-500"
            />
          </div>
          <div class="text-xs text-secondary">{{ item.sku }} · {{ item.category }}</div>
        </template>
      </Column>
      <Column v-if="isFulfillment" field="sellerName" header="Клиент" sortable class="min-w-36" />
      <Column field="barcode" header="Штрихкод" class="min-w-36">
        <template #body="{ data: item }">
          <div class="font-mono text-xs text-heading">{{ item.barcode }}</div>
          <div v-if="item.wbBarcode" class="font-mono text-xs text-secondary">МП {{ item.wbBarcode }}</div>
        </template>
      </Column>
      <Column field="weightG" header="Вес / объём" sortable class="min-w-28">
        <template #body="{ data: item }">
          <span class="tabular text-sm"
            >{{ formatNumber(item.weightG) }} г · {{ formatNumber(item.volumeLiters) }} л</span
          >
        </template>
      </Column>
      <Column field="price" header="Цена" sortable class="min-w-24">
        <template #body="{ data: item }"
          ><span class="tabular">{{ formatMoney(item.price) }}</span></template
        >
      </Column>
      <Column field="stockQty" header="Остаток" sortable class="min-w-24">
        <template #body="{ data: item }">
          <span class="tabular font-medium" :class="item.stockQty ? 'text-heading' : 'text-red-600'">{{
            formatNumber(item.stockQty)
          }}</span>
        </template>
      </Column>
    </RegistryTable>

    <ProductDrawer v-model:visible="drawer" :product="selected" :editable="editable" @edit="edit" />
    <ProductFormDialog
      v-model:visible="form"
      :product="editing"
      :categories="(facets?.categories ?? []).map((c) => c.value)"
    />
    <ImportDialog v-model:visible="importing" />
  </div>
</template>
