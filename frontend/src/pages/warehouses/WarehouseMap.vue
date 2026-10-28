<script setup lang="ts">
import { computed, ref } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import { warehousesApi } from '@/modules/warehouses/api'
import SectionCard from '@/shared/ui/SectionCard.vue'

const props = defineProps<{ warehouseId: string }>()
const emit = defineEmits<{ select: [locationId: string] }>()

const level = ref(1)
const levelOptions = [1, 2, 3, 4].map((value) => ({ value, label: `Ярус ${value}` }))

const { data, isPending } = useQuery({
  queryKey: computed(() => ['warehouse-map', props.warehouseId, level.value]),
  queryFn: () => warehousesApi.map(props.warehouseId, level.value),
})

const grid = computed(() => {
  const cells = data.value ?? []
  const aisles = [...new Set(cells.map((c) => c.aisle))].sort()
  const racks = Math.max(0, ...cells.map((c) => c.rack))
  const index = new Map(cells.map((c) => [`${c.aisle}-${c.rack}`, c]))
  return { aisles, racks: Array.from({ length: racks }, (_, i) => i + 1), index }
})

function cellClass(fill: number) {
  if (fill === 0) return 'bg-surface-100 text-surface-400 dark:bg-surface-800'
  if (fill >= 0.9) return 'bg-rose-500 text-white'
  if (fill >= 0.6) return 'bg-amber-400 text-amber-950'
  return 'bg-emerald-400 text-emerald-950'
}
</script>

<template>
  <SectionCard title="Схема склада" description="Проходы × стеллажи. Нажмите на ячейку, чтобы проверить размещение товара">
    <template #actions>
      <SelectButton v-model="level" :options="levelOptions" option-label="label" option-value="value" :allow-empty="false" size="small" />
    </template>
    <div v-if="isPending" class="grid grid-cols-6 gap-1.5"><Skeleton v-for="i in 24" :key="i" height="2rem" /></div>
    <template v-else>
      <div class="overflow-x-auto pb-2">
        <table class="mx-auto border-separate border-spacing-1">
          <thead>
            <tr>
              <th class="w-8" />
              <th v-for="aisle in grid.aisles" :key="aisle" class="text-xs font-semibold text-secondary">{{ aisle }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="rack in grid.racks" :key="rack">
              <td class="pr-1 text-right text-[11px] text-secondary tabular">{{ String(rack).padStart(2, '0') }}</td>
              <td v-for="aisle in grid.aisles" :key="aisle">
                <button
                  v-if="grid.index.get(`${aisle}-${rack}`)"
                  v-tooltip.top="`${grid.index.get(`${aisle}-${rack}`)!.code}: ${Math.round(grid.index.get(`${aisle}-${rack}`)!.fill * 100)}%`"
                  type="button"
                  class="grid h-7 w-9 place-items-center rounded-md text-[10px] font-semibold transition-transform hover:scale-110 sm:h-8 sm:w-11"
                  :class="cellClass(grid.index.get(`${aisle}-${rack}`)!.fill)"
                  :aria-label="grid.index.get(`${aisle}-${rack}`)!.code"
                  @click="emit('select', grid.index.get(`${aisle}-${rack}`)!.id)"
                >
                  {{ Math.round(grid.index.get(`${aisle}-${rack}`)!.fill * 100) }}
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="mt-3 flex flex-wrap gap-x-4 gap-y-1.5 text-xs text-secondary">
        <span class="inline-flex items-center gap-1.5"><span class="size-3 rounded bg-surface-200 dark:bg-surface-700" />Пусто</span>
        <span class="inline-flex items-center gap-1.5"><span class="size-3 rounded bg-emerald-400" />до 60%</span>
        <span class="inline-flex items-center gap-1.5"><span class="size-3 rounded bg-amber-400" />60–90%</span>
        <span class="inline-flex items-center gap-1.5"><span class="size-3 rounded bg-rose-500" />≥ 90%</span>
      </div>
    </template>
  </SectionCard>
</template>
