<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import { Boxes, Grid3x3, Layers, MapPin, Plus, ShieldCheck, Warehouse as WarehouseIcon } from '@lucide/vue'
import { warehousesApi } from '@/modules/warehouses/api'
import { ZONE_TYPES } from '@/modules/warehouses/labels'
import type { LocationQuery } from '@/modules/warehouses/types'
import { useRegistry } from '@/shared/composables/useRegistry'
import { formatNumber, formatPercent } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import EmptyState from '@/shared/ui/EmptyState.vue'
import FilterChips from '@/shared/ui/FilterChips.vue'
import KpiCard from '@/shared/ui/KpiCard.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import ProgressMeter from '@/shared/ui/ProgressMeter.vue'
import RegistryTable from '@/shared/ui/RegistryTable.vue'
import SearchInput from '@/shared/ui/SearchInput.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'
import type { ChipOption } from '@/shared/ui/types'
import LocationDialog from './LocationDialog.vue'
import PutawayCheckDialog from './PutawayCheckDialog.vue'
import WarehouseMap from './WarehouseMap.vue'
import ZoneDialog from './ZoneDialog.vue'

const session = useSessionStore()

const { data: warehouses, isPending: warehousesPending } = useQuery({
  queryKey: ['warehouses'],
  queryFn: warehousesApi.list,
})

const selectedId = ref('')
watch(
  [warehouses, () => session.warehouseId],
  ([list, preferred]) => {
    if (!list?.length) return
    if (preferred && list.some((w) => w.id === preferred)) selectedId.value = preferred
    else if (!list.some((w) => w.id === selectedId.value)) selectedId.value = list[0]!.id
  },
  { immediate: true },
)
const selected = computed(() => warehouses.value?.find((w) => w.id === selectedId.value))

const { data: zones, isPending: zonesPending } = useQuery({
  queryKey: computed(() => ['warehouse-zones', selectedId.value]),
  queryFn: () => warehousesApi.zones(selectedId.value),
  enabled: computed(() => !!selectedId.value),
})

const tab = ref<'map' | 'zones' | 'locations'>('map')

const registry = useRegistry({
  filters: {
    zoneId: undefined as string | undefined,
    occupancy: undefined as LocationQuery['occupancy'],
  },
  sortField: 'code',
  sortOrder: 'asc',
})
const { state, filters, searchInput } = registry

const { data: locations, isPending: locationsPending } = useQuery({
  queryKey: computed(() => ['warehouse-locations', selectedId.value, registry.query.value]),
  queryFn: () => warehousesApi.locations(selectedId.value, registry.query.value),
  enabled: computed(() => !!selectedId.value && tab.value === 'locations'),
  placeholderData: keepPreviousData,
})

const occupancyChips: ChipOption<NonNullable<LocationQuery['occupancy']>>[] = [
  { value: undefined, label: 'Все' },
  { value: 'empty', label: 'Свободные' },
  { value: 'partial', label: 'Частично заняты' },
  { value: 'full', label: 'Заполнены ≥ 90%' },
]
const zoneOptions = computed(() => (zones.value ?? []).map((z) => ({ value: z.id, label: `${z.code} · ${z.name}` })))

const totals = computed(() => {
  const list = zones.value ?? []
  const capacity = list.reduce((sum, z) => sum + z.capacityLiters, 0)
  const used = list.reduce((sum, z) => sum + z.usedLiters, 0)
  return { capacity, used, ratio: capacity ? used / capacity : 0 }
})

const zoneDialog = ref(false)
const locationDialog = ref(false)
const putawayDialog = ref(false)
const putawayLocation = ref<string | undefined>()

function checkPutaway(locationId?: string) {
  putawayLocation.value = locationId
  putawayDialog.value = true
}

const fillTone = (ratio: number) => (ratio >= 0.9 ? 'text-rose-600' : ratio >= 0.75 ? 'text-amber-600' : 'text-heading')
</script>

<template>
  <div>
    <PageHeader title="Склады и ячейки" description="Топология склада: зоны, проходы, стеллажи и ячейки с координатами и вместимостью">
      <template #actions>
        <Button label="Проверить размещение" severity="secondary" outlined :disabled="!selected" @click="checkPutaway()">
          <template #icon><ShieldCheck class="size-4" /></template>
        </Button>
        <Button label="Ячейка" :disabled="!selected" @click="locationDialog = true">
          <template #icon><Plus class="size-4" /></template>
        </Button>
      </template>
    </PageHeader>

    <div v-if="warehousesPending" class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
      <Skeleton v-for="i in 3" :key="i" height="7rem" />
    </div>
    <EmptyState
      v-else-if="!warehouses?.length"
      :icon="WarehouseIcon"
      title="Складов пока нет"
      description="Склады появятся после подключения организации."
    />

    <template v-else>
      <div class="no-scrollbar -mx-1 mb-6 flex snap-x gap-3 overflow-x-auto px-1 pb-1">
        <button
          v-for="warehouse in warehouses"
          :key="warehouse.id"
          type="button"
          class="surface-card w-[260px] shrink-0 snap-start p-4 text-left transition-all sm:w-[300px]"
          :class="warehouse.id === selectedId ? 'ring-2 ring-primary' : 'hover:shadow-elevated'"
          @click="selectedId = warehouse.id"
        >
          <div class="flex items-center justify-between gap-2">
            <span class="truncate font-semibold text-heading">{{ warehouse.name }}</span>
            <span class="shrink-0 rounded-md bg-surface-100 px-1.5 py-0.5 font-mono text-xs text-secondary dark:bg-surface-800">
              {{ warehouse.code }}
            </span>
          </div>
          <div class="mt-0.5 truncate text-xs text-secondary">{{ warehouse.city }}, {{ warehouse.address }}</div>
          <div class="mt-3 flex items-center justify-between text-xs text-secondary">
            <span>Занято ячеек</span>
            <span class="tabular font-medium text-heading">
              {{ formatNumber(warehouse.occupiedLocations) }} / {{ formatNumber(warehouse.locationsCount) }}
            </span>
          </div>
          <ProgressMeter class="mt-1.5" tone="auto" :value="warehouse.occupiedLocations" :max="warehouse.locationsCount" />
        </button>
      </div>

      <div class="mb-6 grid grid-cols-2 gap-3 lg:grid-cols-4">
        <KpiCard label="Площадь, м²" :icon="WarehouseIcon" :value="formatNumber(selected?.areaSqm ?? 0)" tone="info" />
        <KpiCard label="Зон" :icon="Layers" :value="formatNumber(zones?.length ?? 0)" :loading="zonesPending" />
        <KpiCard label="Ячеек" :icon="Grid3x3" :value="formatNumber(selected?.locationsCount ?? 0)" tone="neutral" />
        <KpiCard
          label="Заполнение объёма"
          :icon="Boxes"
          :value="formatPercent(totals.ratio * 100)"
          :hint="`${formatNumber(totals.used)} из ${formatNumber(totals.capacity)} л`"
          :tone="totals.ratio >= 0.85 ? 'danger' : 'success'"
          :loading="zonesPending"
        />
      </div>

      <Tabs v-model:value="tab">
        <TabList>
          <Tab value="map">Схема</Tab>
          <Tab value="zones">Зоны</Tab>
          <Tab value="locations">Ячейки</Tab>
        </TabList>
        <TabPanels class="!bg-transparent !px-0">
          <TabPanel value="map">
            <WarehouseMap v-if="selectedId" :warehouse-id="selectedId" @select="checkPutaway" />
          </TabPanel>

          <TabPanel value="zones">
            <SectionCard title="Зоны склада" description="Функциональное деление склада; размещение допускается только в зонах хранения и отбора">
              <template #actions>
                <Button label="Зона" size="small" outlined @click="zoneDialog = true">
                  <template #icon><Plus class="size-4" /></template>
                </Button>
              </template>
              <div v-if="zonesPending" class="space-y-2"><Skeleton v-for="i in 4" :key="i" height="3rem" /></div>
              <div v-else class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
                <div
                  v-for="zone in zones"
                  :key="zone.id"
                  class="rounded-xl border border-surface-200 p-4 dark:border-surface-700"
                >
                  <div class="flex items-center justify-between gap-2">
                    <div class="min-w-0">
                      <div class="truncate font-semibold text-heading">{{ zone.name }}</div>
                      <div class="text-xs text-secondary">{{ ZONE_TYPES[zone.type] }}</div>
                    </div>
                    <span class="rounded-md bg-surface-100 px-1.5 py-0.5 font-mono text-xs dark:bg-surface-800">{{ zone.code }}</span>
                  </div>
                  <div class="mt-3 flex justify-between text-xs text-secondary">
                    <span>{{ formatNumber(zone.locationsCount) }} ячеек</span>
                    <span v-if="zone.capacityLiters" class="tabular" :class="fillTone(zone.usedLiters / zone.capacityLiters)">
                      {{ formatPercent((zone.usedLiters / zone.capacityLiters) * 100) }}
                    </span>
                    <span v-else>буферная зона</span>
                  </div>
                  <ProgressMeter v-if="zone.capacityLiters" class="mt-1.5" tone="auto" :value="zone.usedLiters" :max="zone.capacityLiters" />
                </div>
              </div>
            </SectionCard>
          </TabPanel>

          <TabPanel value="locations">
            <div class="mb-4 space-y-3">
              <FilterChips
                :model-value="filters.occupancy"
                :options="occupancyChips"
                @update:model-value="registry.setFilter('occupancy', $event)"
              />
              <div class="flex flex-wrap items-center gap-2.5">
                <SearchInput v-model="searchInput" placeholder="Код ячейки, например A-03" />
                <Select
                  :model-value="filters.zoneId"
                  :options="zoneOptions"
                  option-label="label"
                  option-value="value"
                  placeholder="Все зоны"
                  show-clear
                  class="w-full sm:w-60"
                  @update:model-value="registry.setFilter('zoneId', $event ?? undefined)"
                />
              </div>
            </div>
            <RegistryTable
              :items="locations?.items ?? []"
              :total="locations?.total ?? 0"
              :loading="locationsPending"
              :first="registry.first.value"
              :rows="state.pageSize"
              :sort-field="state.sortField"
              :sort-order="registry.tableSortOrder.value"
              clickable
              empty-title="Ячейки не найдены"
              @page="registry.onPage"
              @sort="registry.onSort"
              @row-click="checkPutaway($event.id)"
            >
              <Column field="code" header="Ячейка" sortable class="min-w-32">
                <template #body="{ data: loc }">
                  <div class="flex items-center gap-2">
                    <MapPin class="size-4 shrink-0 text-surface-400" />
                    <span class="font-mono font-semibold text-heading">{{ loc.code }}</span>
                  </div>
                  <div class="text-xs text-secondary">Зона {{ loc.zoneCode }} · ярус {{ loc.level }}</div>
                </template>
              </Column>
              <Column header="Координаты" class="whitespace-nowrap">
                <template #body="{ data: loc }">
                  <span class="tabular text-sm text-secondary">x {{ loc.x }} · y {{ loc.y }} м</span>
                </template>
              </Column>
              <Column field="usedLiters" header="Заполнение" sortable class="min-w-40">
                <template #body="{ data: loc }">
                  <div class="tabular text-sm" :class="fillTone(loc.usedLiters / loc.capacityLiters)">
                    {{ formatNumber(loc.usedLiters) }} / {{ formatNumber(loc.capacityLiters) }} л
                  </div>
                  <ProgressMeter class="mt-1" tone="auto" :value="loc.usedLiters" :max="loc.capacityLiters" />
                </template>
              </Column>
              <Column field="skuCount" header="SKU" sortable>
                <template #body="{ data: loc }">
                  <span class="tabular">{{ loc.skuCount }}</span>
                  <span class="text-xs text-secondary"> · {{ formatNumber(loc.quantity) }} шт</span>
                </template>
              </Column>
              <Column header="Клиенты" class="min-w-44">
                <template #body="{ data: loc }">
                  <span class="text-sm text-secondary">{{ loc.sellerNames.join(', ') || '—' }}</span>
                </template>
              </Column>
            </RegistryTable>
          </TabPanel>
        </TabPanels>
      </Tabs>
    </template>

    <template v-if="selectedId">
      <ZoneDialog v-model:visible="zoneDialog" :warehouse-id="selectedId" />
      <LocationDialog v-model:visible="locationDialog" :warehouse-id="selectedId" :zones="zones ?? []" />
      <PutawayCheckDialog v-model:visible="putawayDialog" :warehouse-id="selectedId" :initial-location-id="putawayLocation" />
    </template>
  </div>
</template>
