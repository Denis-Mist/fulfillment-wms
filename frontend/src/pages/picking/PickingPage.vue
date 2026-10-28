<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { refDebounced } from '@vueuse/core'
import { Layers, PackageCheck, Route, Sparkles, Timer } from '@lucide/vue'
import { tasksApi } from '@/modules/tasks/api'
import type { RouteAlgorithm, TaskListItem } from '@/modules/tasks/types'
import { useNotify } from '@/shared/composables/useNotify'
import { formatDateTime, formatNumber, formatRelative } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import ChannelBadge from '@/shared/ui/ChannelBadge.vue'
import EmptyState from '@/shared/ui/EmptyState.vue'
import KpiCard from '@/shared/ui/KpiCard.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import ProgressMeter from '@/shared/ui/ProgressMeter.vue'
import RegistryTable from '@/shared/ui/RegistryTable.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'
import RouteMap from '../tasks/RouteMap.vue'
import TaskDrawer from '../tasks/TaskDrawer.vue'
import TaskStatusTag from '../tasks/TaskStatusTag.vue'

const session = useSessionStore()
const notify = useNotify()
const qc = useQueryClient()
const warehouseId = computed(() => session.warehouseId || undefined)
const tab = ref<'build' | 'lists'>('build')

const { data: candidates, isPending } = useQuery({
  queryKey: computed(() => ['picking-candidates', session.organizationId, warehouseId.value]),
  queryFn: () => tasksApi.candidates(warehouseId.value),
})
const selected = ref<string[]>([])
const selectedOrders = computed(() => (candidates.value ?? []).filter((c) => selected.value.includes(c.id)))
const batchWarehouse = computed(() => selectedOrders.value[0]?.warehouseId)
const visibleCandidates = computed(() =>
  (candidates.value ?? []).filter((c) => !batchWarehouse.value || c.warehouseId === batchWarehouse.value),
)

function toggle(id: string) {
  selected.value = selected.value.includes(id)
    ? selected.value.filter((x) => x !== id)
    : [...selected.value, id].slice(0, 12)
}
/** Подсказка: самые срочные заказы одного склада до 8 шт. */
function suggest() {
  const list = candidates.value ?? []
  const first = list[0]
  if (!first) return
  selected.value = list
    .filter((c) => c.warehouseId === first.warehouseId)
    .slice(0, 8)
    .map((c) => c.id)
}
watch(candidates, (list) => {
  const ids = new Set((list ?? []).map((c) => c.id))
  selected.value = selected.value.filter((id) => ids.has(id))
})

const debouncedIds = refDebounced(selected, 250)
const { data: preview, isFetching: previewLoading } = useQuery({
  queryKey: computed(() => ['picking-preview', [...debouncedIds.value].sort()]),
  queryFn: () => tasksApi.preview(debouncedIds.value),
  enabled: computed(() => debouncedIds.value.length > 0),
  placeholderData: keepPreviousData,
})
const algorithm = ref<RouteAlgorithm>('optimized')
const algoOptions = [
  { value: 'optimized', label: 'Оптимальный маршрут' },
  { value: 'baseline', label: 'По адресам' },
]
const current = computed(() => (preview.value ? preview.value[algorithm.value] : undefined))
const saving = computed(() =>
  preview.value && preview.value.baseline.distance > 0
    ? Math.round((1 - preview.value.optimized.distance / preview.value.baseline.distance) * 100)
    : 0,
)

const create = useMutation({
  mutationFn: () => tasksApi.createBatch(selected.value, algorithm.value),
  onSuccess: (t) => {
    notify.success(
      `Лист отбора ${t.number} создан`,
      `${t.ordersCount} заказов · ${t.steps.length} точек · ${formatNumber(t.routeDistance, 1)} м`,
    )
    selected.value = []
    void qc.invalidateQueries({ queryKey: ['picking-candidates'] })
    void qc.invalidateQueries({ queryKey: ['tasks'] })
    void qc.invalidateQueries({ queryKey: ['tasks-summary'] })
    selectedTask.value = t.id
    drawer.value = true
  },
  onError: (e) => notify.error(e),
})

/* Листы отбора */
const page = ref(0)
const { data: lists, isPending: listsPending } = useQuery({
  queryKey: computed(() => ['tasks', session.organizationId, 'picking-lists', warehouseId.value, page.value]),
  queryFn: () =>
    tasksApi.list({
      type: 'picking',
      status: 'active',
      warehouseId: warehouseId.value,
      page: page.value + 1,
      pageSize: 20,
    }),
  placeholderData: keepPreviousData,
})
const { data: summary } = useQuery({
  queryKey: computed(() => ['tasks-summary', session.organizationId, warehouseId.value, 'picking']),
  queryFn: () => tasksApi.summary({ warehouseId: warehouseId.value, type: 'picking' }),
})
const selectedTask = ref<string | null>(null)
const drawer = ref(false)
function openTask(t: TaskListItem) {
  selectedTask.value = t.id
  drawer.value = true
}
</script>

<template>
  <div>
    <PageHeader title="Комплектация" description="Батчи отбора, маршруты по складу и листы в работе" />

    <div class="mb-6 grid grid-cols-2 gap-3 lg:grid-cols-4">
      <KpiCard
        label="Заказов к отбору"
        :value="formatNumber(candidates?.length ?? 0)"
        :icon="PackageCheck"
        :loading="isPending"
      />
      <KpiCard
        label="Срочные (просрочены)"
        :value="formatNumber((candidates ?? []).filter((c) => c.isOverdue).length)"
        :icon="Timer"
        tone="danger"
        :loading="isPending"
      />
      <KpiCard
        label="Листов в работе"
        :value="formatNumber(summary?.byStatus.in_progress ?? 0)"
        :icon="Layers"
        tone="warn"
      />
      <KpiCard
        label="Ср. время листа"
        :value="summary?.avgMinutes ? `${summary.avgMinutes} мин` : '—'"
        :icon="Route"
        tone="success"
      />
    </div>

    <Tabs v-model:value="tab">
      <TabList>
        <Tab value="build">Формирование батча</Tab>
        <Tab value="lists"
          >Листы отбора <span class="ml-1 text-secondary">{{ lists?.total ?? '' }}</span></Tab
        >
      </TabList>
      <TabPanels class="!px-0">
        <TabPanel value="build">
          <div class="grid gap-6 lg:grid-cols-[minmax(0,1fr)_24rem]">
            <SectionCard
              title="Заказы в резерве"
              description="Отсортированы по сроку отгрузки. Батч — до 12 заказов одного склада."
              flush
            >
              <template #actions>
                <Button size="small" severity="secondary" outlined @click="suggest"
                  ><Sparkles class="size-4" />Подобрать срочные</Button
                >
              </template>
              <div v-if="isPending" class="space-y-2 p-5">
                <Skeleton v-for="i in 6" :key="i" height="3rem" />
              </div>
              <EmptyState
                v-else-if="!candidates?.length"
                compact
                :icon="PackageCheck"
                title="Все заказы распределены"
                description="Новые заказы в резерве появятся здесь"
              />
              <ul
                v-else
                class="max-h-[32rem] divide-y divide-default overflow-y-auto border-t border-default"
              >
                <li v-for="c in visibleCandidates" :key="c.id">
                  <label
                    class="flex cursor-pointer items-center gap-3 px-5 py-2.5 hover:bg-surface-50 dark:hover:bg-surface-800"
                  >
                    <Checkbox
                      :model-value="selected.includes(c.id)"
                      binary
                      :input-id="`cand-${c.id}`"
                      @update:model-value="toggle(c.id)"
                    />
                    <div class="min-w-0 flex-1">
                      <div class="flex flex-wrap items-center gap-2 text-sm font-medium text-heading">
                        {{ c.number }} <ChannelBadge :channel="c.channel" />
                      </div>
                      <div class="truncate text-xs text-secondary">
                        {{ c.sellerName }} · {{ c.linesCount }} поз. · {{ c.units }} шт
                      </div>
                    </div>
                    <span
                      class="whitespace-nowrap text-xs"
                      :class="c.isOverdue ? 'font-medium text-red-600' : 'text-secondary'"
                    >
                      {{ c.isOverdue ? 'просрочен' : formatRelative(c.shipBy) }}
                    </span>
                  </label>
                </li>
              </ul>
            </SectionCard>

            <SectionCard
              title="Маршрут отбора"
              :description="selected.length ? `${selected.length} заказов в батче` : 'Выберите заказы слева'"
            >
              <div class="space-y-4 pb-5">
                <SelectButton
                  v-model="algorithm"
                  :options="algoOptions"
                  option-label="label"
                  option-value="value"
                  :allow-empty="false"
                  class="w-full [&>*]:flex-1"
                />
                <template v-if="selected.length && current">
                  <RouteMap
                    :points="current.points"
                    :ghost="algorithm === 'optimized' ? preview!.baseline.points : undefined"
                  />
                  <div class="grid grid-cols-3 gap-2 text-center">
                    <div class="rounded-lg border border-default p-2">
                      <div class="tabular font-semibold text-heading">
                        {{ formatNumber(current.distance, 1) }} м
                      </div>
                      <div class="text-xs text-secondary">путь</div>
                    </div>
                    <div class="rounded-lg border border-default p-2">
                      <div class="tabular font-semibold text-heading">{{ preview!.stepsCount }}</div>
                      <div class="text-xs text-secondary">точек</div>
                    </div>
                    <div class="rounded-lg border border-default p-2">
                      <div class="tabular font-semibold text-heading">
                        ~{{ preview!.estimatedMinutes }} мин
                      </div>
                      <div class="text-xs text-secondary">{{ preview!.units }} шт</div>
                    </div>
                  </div>
                  <Message v-if="saving > 0" severity="success" size="small" :closable="false">
                    Оптимальный маршрут короче обхода по адресам на {{ saving }}% ({{
                      formatNumber(preview!.baseline.distance - preview!.optimized.distance, 1)
                    }}
                    м).
                  </Message>
                  <Button
                    class="w-full"
                    :loading="create.isPending.value || previewLoading"
                    @click="create.mutate()"
                  >
                    <Layers class="size-4" />Создать лист отбора
                  </Button>
                </template>
                <EmptyState
                  v-else
                  compact
                  :icon="Route"
                  title="Маршрут появится здесь"
                  description="Сравните оптимальный обход и обход по адресам ячеек"
                />
              </div>
            </SectionCard>
          </div>
        </TabPanel>

        <TabPanel value="lists">
          <RegistryTable
            :items="lists?.items ?? []"
            :total="lists?.total ?? 0"
            :loading="listsPending"
            :first="page * 20"
            :rows="20"
            clickable
            empty-title="Нет листов в работе"
            empty-description="Сформируйте батч на соседней вкладке"
            @page="page = $event.page"
            @row-click="openTask"
          >
            <Column header="Лист" class="min-w-44">
              <template #body="{ data: t }">
                <div class="font-medium text-heading">{{ t.number }}</div>
                <div class="text-xs text-secondary">{{ t.reference }}</div>
              </template>
            </Column>
            <Column header="Статус" class="min-w-28"
              ><template #body="{ data: t }"><TaskStatusTag :status="t.status" /></template
            ></Column>
            <Column header="Комплектовщик" class="min-w-40">
              <template #body="{ data: t }"
                ><span :class="!t.assigneeName && 'text-amber-600'">{{
                  t.assigneeName ?? 'Не назначен'
                }}</span></template
              >
            </Column>
            <Column header="Отобрано" class="min-w-36">
              <template #body="{ data: t }">
                <div class="tabular mb-1 text-xs text-secondary">{{ t.doneUnits }}/{{ t.units }} шт</div>
                <ProgressMeter :value="t.units ? (t.doneUnits / t.units) * 100 : 0" />
              </template>
            </Column>
            <Column header="Создан" class="min-w-32">
              <template #body="{ data: t }"
                ><span class="whitespace-nowrap text-sm text-secondary">{{
                  formatDateTime(t.createdAt)
                }}</span></template
              >
            </Column>
          </RegistryTable>
        </TabPanel>
      </TabPanels>
    </Tabs>

    <TaskDrawer v-model:visible="drawer" :task-id="selectedTask" />
  </div>
</template>
