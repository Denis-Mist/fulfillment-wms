<script setup lang="ts">
import { computed, ref } from 'vue'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { CheckCheck, Clock, FilterX, ListChecks, TriangleAlert, UserPlus, Wand2 } from '@lucide/vue'
import { ROLE_SHORT, TASK_TYPES } from '@/modules/tasks/labels'
import { tasksApi } from '@/modules/tasks/api'
import type { TaskListItem, TaskQuery, TaskStatus, TaskType } from '@/modules/tasks/types'
import { useNotify } from '@/shared/composables/useNotify'
import { useRegistry } from '@/shared/composables/useRegistry'
import { formatNumber, formatRelative } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import FilterChips from '@/shared/ui/FilterChips.vue'
import KpiCard from '@/shared/ui/KpiCard.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import ProgressMeter from '@/shared/ui/ProgressMeter.vue'
import RegistryTable from '@/shared/ui/RegistryTable.vue'
import SearchInput from '@/shared/ui/SearchInput.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'
import UserAvatar from '@/shared/ui/UserAvatar.vue'
import type { ChipOption } from '@/shared/ui/types'
import TaskDrawer from './TaskDrawer.vue'
import TaskStatusTag from './TaskStatusTag.vue'

type StatusFilter = TaskStatus | 'active' | 'late'

const session = useSessionStore()
const notify = useNotify()
const qc = useQueryClient()

const registry = useRegistry({
  filters: {
    status: 'active' as StatusFilter | undefined,
    type: undefined as TaskType | undefined,
    assigneeId: undefined as string | undefined,
  },
  sortField: 'createdAt',
})
const { state, filters, searchInput } = registry
const warehouseId = computed(() => session.warehouseId || undefined)
const listQuery = computed<TaskQuery>(() => ({ ...registry.query.value, warehouseId: warehouseId.value }))

const { data, isPending } = useQuery({
  queryKey: computed(() => ['tasks', session.organizationId, listQuery.value]),
  queryFn: () => tasksApi.list(listQuery.value),
  placeholderData: keepPreviousData,
  refetchInterval: 30_000,
})
const { data: summary, isPending: summaryPending } = useQuery({
  queryKey: computed(() => ['tasks-summary', session.organizationId, warehouseId.value, filters.type]),
  queryFn: () => tasksApi.summary({ warehouseId: warehouseId.value, type: filters.type }),
  placeholderData: keepPreviousData,
  refetchInterval: 30_000,
})
const { data: workers, isPending: workersPending } = useQuery({
  queryKey: computed(() => ['task-workers', session.organizationId, warehouseId.value]),
  queryFn: () => tasksApi.workers(warehouseId.value),
})

const chips = computed<ChipOption<StatusFilter>[]>(() => [
  { value: 'active', label: 'Активные', count: summary.value?.active },
  { value: 'late', label: 'Сверх норматива', count: summary.value?.late, danger: true },
  { value: 'new', label: 'Без исполнителя', count: summary.value?.byStatus.new ?? 0 },
  { value: 'assigned', label: 'Назначены', count: summary.value?.byStatus.assigned ?? 0 },
  { value: 'in_progress', label: 'В работе', count: summary.value?.byStatus.in_progress ?? 0 },
  { value: 'done', label: 'Выполнены', count: summary.value?.byStatus.done ?? 0 },
  { value: undefined, label: 'Все', count: summary.value?.total },
])
const typeOptions = Object.entries(TASK_TYPES).map(([value, m]) => ({ value, label: m.label }))
const workerOptions = computed(() => (workers.value ?? []).map((w) => ({ value: w.id, label: w.fullName })))
const sortedWorkers = computed(() =>
  [...(workers.value ?? [])].sort((a, b) => b.active - a.active || b.doneToday - a.doneToday),
)

const autoAssign = useMutation({
  mutationFn: () => tasksApi.autoAssign(warehouseId.value),
  onSuccess: (r) => {
    notify.success(
      r.assigned ? `Назначено заданий: ${r.assigned}` : 'Нечего назначать',
      r.assigned
        ? 'Учтены роль и текущая загрузка сотрудников'
        : 'Все сотрудники загружены или очередь пуста',
    )
    void qc.invalidateQueries({ queryKey: ['tasks'] })
    void qc.invalidateQueries({ queryKey: ['tasks-summary'] })
    void qc.invalidateQueries({ queryKey: ['task-workers'] })
  },
  onError: (e) => notify.error(e),
})

const selectedId = ref<string | null>(null)
const drawer = ref(false)
function open(t: TaskListItem) {
  selectedId.value = t.id
  drawer.value = true
}
</script>

<template>
  <div>
    <PageHeader title="Задания" description="Очередь складских операций, исполнители и контроль нормативов" />

    <div class="mb-6 grid grid-cols-2 gap-3 lg:grid-cols-4">
      <KpiCard
        label="Активные задания"
        :value="formatNumber(summary?.active ?? 0)"
        :icon="ListChecks"
        :loading="summaryPending"
      />
      <KpiCard
        label="Без исполнителя"
        :value="formatNumber(summary?.unassigned ?? 0)"
        :icon="UserPlus"
        tone="info"
        :loading="summaryPending"
      />
      <KpiCard
        label="Сверх норматива"
        :value="formatNumber(summary?.late ?? 0)"
        :icon="TriangleAlert"
        :tone="summary?.late ? 'danger' : 'success'"
        :loading="summaryPending"
      />
      <KpiCard
        label="Выполнено сегодня"
        :value="formatNumber(summary?.doneToday ?? 0)"
        :icon="CheckCheck"
        tone="success"
        :hint="summary?.avgMinutes ? `в среднем ${summary.avgMinutes} мин` : undefined"
        :loading="summaryPending"
      />
    </div>

    <div class="grid gap-6 xl:grid-cols-[minmax(0,1fr)_20rem]">
      <div class="min-w-0">
        <div class="mb-4 space-y-3">
          <FilterChips
            :model-value="filters.status"
            :options="chips"
            @update:model-value="registry.setFilter('status', $event)"
          />
          <div class="flex flex-wrap items-center gap-2.5">
            <SearchInput v-model="searchInput" placeholder="Номер, заказ, сотрудник…" />
            <Select
              :model-value="filters.type"
              :options="typeOptions"
              option-label="label"
              option-value="value"
              placeholder="Все типы"
              show-clear
              class="w-full sm:w-44"
              @update:model-value="registry.setFilter('type', $event ?? undefined)"
            />
            <Select
              :model-value="filters.assigneeId"
              :options="workerOptions"
              option-label="label"
              option-value="value"
              placeholder="Все сотрудники"
              filter
              show-clear
              class="w-full sm:w-52"
              @update:model-value="registry.setFilter('assigneeId', $event ?? undefined)"
            />
            <Button
              v-if="registry.hasActiveFilters.value"
              text
              severity="secondary"
              @click="registry.resetFilters()"
              ><FilterX class="size-4" />Сбросить</Button
            >
            <Button
              class="w-full sm:ml-auto sm:w-auto"
              :loading="autoAssign.isPending.value"
              :disabled="!summary?.unassigned"
              @click="autoAssign.mutate()"
            >
              <Wand2 class="size-4" />Распределить автоматически
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
          empty-title="Заданий нет"
          @page="registry.onPage"
          @sort="registry.onSort"
          @row-click="open"
        >
          <Column field="number" header="Задание" sortable class="min-w-40">
            <template #body="{ data: t }">
              <div class="flex items-center gap-1.5 font-medium text-heading">
                {{ TASK_TYPES[t.type as TaskType].label }}
                <span
                  v-if="t.priority === 'high'"
                  class="size-1.5 rounded-full bg-red-500"
                  v-tooltip="'Срочно'"
                />
              </div>
              <div class="text-xs text-secondary">{{ t.number }} · {{ t.reference }}</div>
            </template>
          </Column>
          <Column field="status" header="Статус" class="min-w-28">
            <template #body="{ data: t }"><TaskStatusTag :status="t.status" /></template>
          </Column>
          <Column header="Исполнитель" class="min-w-40">
            <template #body="{ data: t }">
              <span v-if="t.assigneeName" class="text-sm">{{ t.assigneeName }}</span>
              <span v-else class="text-sm text-amber-600">Не назначен</span>
            </template>
          </Column>
          <Column header="Прогресс" class="min-w-36">
            <template #body="{ data: t }">
              <div class="tabular mb-1 text-xs text-secondary">
                {{ t.doneUnits }}/{{ t.units }} шт · {{ t.linesCount }} поз.
              </div>
              <ProgressMeter
                :value="t.units ? (t.doneUnits / t.units) * 100 : 0"
                :tone="t.status === 'done' ? 'success' : t.isLate ? 'warn' : 'primary'"
              />
            </template>
          </Column>
          <Column header="Время" class="min-w-28">
            <template #body="{ data: t }">
              <div
                class="tabular inline-flex items-center gap-1 whitespace-nowrap text-sm"
                :class="t.isLate ? 'font-medium text-red-600' : 'text-heading'"
              >
                <Clock class="size-3.5" /><template v-if="t.startedAt"
                  >{{ t.elapsedMinutes ?? 0 }} / {{ t.normMinutes }} мин</template
                ><template v-else>норма {{ t.normMinutes }} мин</template>
              </div>
            </template>
          </Column>
          <Column field="createdAt" header="Создано" sortable class="min-w-28">
            <template #body="{ data: t }"
              ><span class="whitespace-nowrap text-sm text-secondary">{{
                formatRelative(t.createdAt)
              }}</span></template
            >
          </Column>
        </RegistryTable>
      </div>

      <SectionCard title="Загрузка смены" description="Активные задания и выработка за сегодня">
        <div v-if="workersPending" class="space-y-3 pb-5" aria-busy="true">
          <div v-for="i in 5" :key="i" class="flex items-center gap-3">
            <Skeleton shape="circle" size="2rem" />
            <div class="flex-1 space-y-1.5">
              <Skeleton width="70%" height="0.9rem" /><Skeleton width="40%" height="0.7rem" />
            </div>
          </div>
        </div>
        <ul v-else class="space-y-3 pb-5">
          <li v-for="w in sortedWorkers" :key="w.id" class="flex items-center gap-3">
            <UserAvatar :name="w.fullName" size="sm" />
            <div class="min-w-0 flex-1">
              <div class="truncate text-sm font-medium text-heading">{{ w.fullName }}</div>
              <div class="text-xs text-secondary">
                {{ ROLE_SHORT[w.role] }} · сегодня {{ w.doneToday }} зад., {{ formatNumber(w.unitsToday) }} шт
              </div>
            </div>
            <span
              class="tabular rounded-md px-2 py-0.5 text-xs font-semibold"
              :class="
                w.active >= 3
                  ? 'bg-red-50 text-red-700 dark:bg-red-950 dark:text-red-300'
                  : w.active
                    ? 'bg-primary-50 text-primary-700 dark:bg-primary-950 dark:text-primary-300'
                    : 'bg-surface-100 text-secondary dark:bg-surface-800'
              "
              >{{ w.active }}</span
            >
          </li>
        </ul>
      </SectionCard>
    </div>

    <TaskDrawer v-model:visible="drawer" :task-id="selectedId" />
  </div>
</template>
