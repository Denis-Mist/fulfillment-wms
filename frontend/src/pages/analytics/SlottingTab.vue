<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { useStorage } from '@vueuse/core'
import { useConfirm } from 'primevue/useconfirm'
import { ArrowRight, Footprints, Layers, Route, Sparkles } from '@lucide/vue'
import { analyticsApi } from '@/modules/analytics/api'
import type { AbcClass, SlottingAlgorithm } from '@/modules/analytics/types'
import { useNotify } from '@/shared/composables/useNotify'
import { formatNumber } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import EmptyState from '@/shared/ui/EmptyState.vue'
import KpiCard from '@/shared/ui/KpiCard.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'
import CalcMetaLine from './CalcMetaLine.vue'

const session = useSessionStore()
const notify = useNotify()
const confirm = useConfirm()
const qc = useQueryClient()

const enabled = useStorage('wms.analytics.slottingEnabled', true)
const algorithm = useStorage<SlottingAlgorithm>('wms.analytics.slottingAlgorithm', 'abc_affinity')
const algorithmOptions = [
  { value: 'abc_affinity', label: 'ABC + совместные покупки' },
  { value: 'abc', label: 'Только ABC' },
]
const warehouseId = computed(() => session.warehouseId || undefined)
const selected = ref<string[]>([])

const { data, isPending } = useQuery({
  queryKey: computed(() => [
    'analytics-slotting',
    session.organizationId,
    warehouseId.value,
    algorithm.value,
  ]),
  queryFn: () => analyticsApi.slotting({ warehouseId: warehouseId.value, algorithm: algorithm.value }),
  placeholderData: keepPreviousData,
  enabled,
})
watch(data, () => (selected.value = []))

const pending = computed(() => (data.value?.moves ?? []).filter((m) => !m.applied))
const allSelected = computed({
  get: () => pending.value.length > 0 && selected.value.length === pending.value.length,
  set: (v: boolean) => (selected.value = v ? pending.value.map((m) => m.id) : []),
})
const selectedGain = computed(() =>
  (data.value?.moves ?? []).filter((m) => selected.value.includes(m.id)).reduce((a, m) => a + m.gain, 0),
)

const apply = useMutation({
  mutationFn: () =>
    analyticsApi.applySlotting({
      warehouseId: warehouseId.value,
      algorithm: algorithm.value,
      ids: selected.value,
    }),
  onSuccess: (r) => {
    notify.success(`Принято рекомендаций: ${r.applied}`, 'Перемещения переданы в очередь внутренних операций')
    void qc.invalidateQueries({ queryKey: ['analytics-slotting'] })
  },
  onError: (e) => notify.error(e),
})

function askApply() {
  confirm.require({
    header: 'Принять рекомендации?',
    message: `Будет сформировано ${selected.value.length} перемещений. Ожидаемое сокращение пробега — ${formatNumber(selectedGain.value)} м за 30 дней.`,
    acceptLabel: 'Принять',
    rejectLabel: 'Отмена',
    accept: () => apply.mutate(),
  })
}

const ABC_TONE: Record<AbcClass, string> = {
  A: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300',
  B: 'bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300',
  C: 'bg-surface-100 text-surface-600 dark:bg-surface-800 dark:text-surface-300',
}
</script>

<template>
  <div class="space-y-5">
    <div class="flex flex-wrap items-center gap-3">
      <label class="flex items-center gap-2 text-sm">
        <ToggleSwitch v-model="enabled" />
        Рекомендации по размещению
      </label>
      <SelectButton
        v-if="enabled"
        v-model="algorithm"
        :options="algorithmOptions"
        option-label="label"
        option-value="value"
        :allow-empty="false"
        size="small"
      />
    </div>

    <div v-if="!enabled" class="surface-card">
      <EmptyState
        :icon="Layers"
        title="Алгоритм отключён"
        description="Размещение выполняется базовым правилом: в ближайшую свободную ячейку подходящей вместимости. Складские операции не зависят от модуля рекомендаций."
      />
    </div>

    <template v-else>
      <div class="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <KpiCard
          v-for="c in ['A', 'B', 'C'] as AbcClass[]"
          :key="c"
          :label="`Класс ${c}`"
          :value="data ? `${data.classes[c].sku} SKU` : '—'"
          :icon="Layers"
          :tone="c === 'A' ? 'success' : c === 'B' ? 'warn' : 'neutral'"
          :hint="data ? `${data.classes[c].picksShare}% отборов` : undefined"
          :loading="isPending"
        />
        <KpiCard
          label="Сокращение пробега"
          :value="data ? `${data.reductionPct}%` : '—'"
          :icon="Footprints"
          tone="info"
          :hint="
            data
              ? `${formatNumber(data.currentDistance)} → ${formatNumber(data.projectedDistance)} м / 30 дн`
              : undefined
          "
          :loading="isPending"
        />
      </div>

      <SectionCard
        title="План перемещений"
        description="Частые SKU (класс A) переносятся ближе к зоне отгрузки; совместно покупаемые — в соседние ячейки"
        flush
      >
        <template #actions>
          <Button
            :label="
              selected.length
                ? `Принять (${selected.length}) · −${formatNumber(selectedGain)} м`
                : 'Принять выбранные'
            "
            size="small"
            :disabled="!selected.length"
            :loading="apply.isPending.value"
            @click="askApply"
          >
            <template #icon><Sparkles class="mr-1.5 size-4" /></template>
          </Button>
        </template>
        <div v-if="data && !data.moves.length" class="px-5 pb-5">
          <EmptyState compact :icon="Route" title="Размещение оптимально" description="Рекомендаций нет" />
        </div>
        <div v-else class="overflow-x-auto">
          <table class="w-full min-w-[46rem] text-sm">
            <thead class="text-left text-xs text-secondary uppercase">
              <tr class="border-b border-default">
                <th class="w-10 px-5 py-2">
                  <Checkbox v-model="allSelected" binary :disabled="!pending.length" />
                </th>
                <th class="px-3 py-2 font-medium">Товар</th>
                <th class="px-3 py-2 font-medium">ABC</th>
                <th class="px-3 py-2 text-right font-medium">Отборов / 30 дн</th>
                <th class="px-3 py-2 font-medium">Перемещение</th>
                <th class="px-5 py-2 text-right font-medium">Экономия</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="m in data?.moves ?? []"
                :key="m.id"
                class="border-b border-default last:border-0"
                :class="m.applied && 'opacity-60'"
              >
                <td class="px-5 py-2.5">
                  <Tag v-if="m.applied" value="Принято" severity="success" class="!text-[10px]" />
                  <Checkbox v-else v-model="selected" :value="m.id" />
                </td>
                <td class="max-w-[18rem] px-3 py-2.5">
                  <div class="truncate font-medium text-heading">{{ m.name }}</div>
                  <div class="truncate text-xs text-secondary">
                    {{ m.sku }} · {{ m.sellerName
                    }}<template v-if="m.affinitySku"> · рядом с {{ m.affinitySku }}</template>
                  </div>
                </td>
                <td class="px-3 py-2.5">
                  <span class="rounded-md px-1.5 py-0.5 text-xs font-semibold" :class="ABC_TONE[m.abc]">{{
                    m.abc
                  }}</span>
                </td>
                <td class="tabular px-3 py-2.5 text-right">{{ m.picks30d }}</td>
                <td class="px-3 py-2.5 whitespace-nowrap">
                  <span class="font-mono text-xs">{{ m.fromCell }}</span>
                  <span class="text-xs text-secondary"> ({{ m.fromDistance }} м)</span>
                  <ArrowRight class="mx-1 inline size-3.5 text-secondary" />
                  <span class="font-mono text-xs">{{ m.toCell }}</span>
                  <span class="text-xs text-secondary"> ({{ m.toDistance }} м)</span>
                </td>
                <td
                  class="tabular px-5 py-2.5 text-right font-medium whitespace-nowrap text-emerald-600 dark:text-emerald-400"
                >
                  −{{ formatNumber(m.gain) }} м
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <CalcMetaLine class="px-5 py-3" :meta="data?.meta" />
      </SectionCard>
    </template>
  </div>
</template>
