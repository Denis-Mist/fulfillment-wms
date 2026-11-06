<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { useConfirm } from 'primevue/useconfirm'
import { Ban, MapPin, Route } from '@lucide/vue'
import { SKILLS } from '@/modules/staff/labels'
import { ROLE_SHORT, TASK_TYPES } from '@/modules/tasks/labels'
import { tasksApi } from '@/modules/tasks/api'
import { useNotify } from '@/shared/composables/useNotify'
import { formatDateTime, formatNumber } from '@/shared/lib/format'
import InfoList from '@/shared/ui/InfoList.vue'
import ProgressMeter from '@/shared/ui/ProgressMeter.vue'
import TaskStatusTag from './TaskStatusTag.vue'

const props = defineProps<{ taskId: string | null }>()
const visible = defineModel<boolean>('visible', { required: true })
const notify = useNotify()
const confirm = useConfirm()
const qc = useQueryClient()

const { data, isPending } = useQuery({
  queryKey: computed(() => ['task', props.taskId]),
  queryFn: () => tasksApi.get(props.taskId!),
  enabled: computed(() => !!props.taskId && visible.value),
})
const task = computed(() => (data.value?.id === props.taskId ? data.value : undefined))

const { data: workers } = useQuery({
  queryKey: ['task-workers'],
  queryFn: () => tasksApi.workers(),
  enabled: visible,
})
const options = computed(() =>
  (workers.value ?? [])
    .filter((w) => task.value?.eligibleRoles.includes(w.role))
    .sort((a, b) => Number(b.available !== false) - Number(a.available !== false) || Number(b.onShift !== false) - Number(a.onShift !== false) || a.active - b.active)
    .map((w) => {
      const bits = [ROLE_SHORT[w.role] ?? '', `заданий: ${w.active}`]
      if (w.onShift === false) bits.push('не на смене')
      else if (w.available === false) bits.push('занят')
      else if (w.onShift) bits.push('на смене')
      if (w.skills?.length) bits.push(w.skills.map((sk) => SKILLS[sk] ?? sk).slice(0, 2).join(', '))
      return { value: w.id, label: `${w.fullName} · ${bits.join(' · ')}` }
    }),
)
const assignee = ref<string | null>(null)
watch(task, (t) => (assignee.value = t?.assigneeId ?? null), { immediate: true })

const closed = computed(() => task.value?.status === 'done' || task.value?.status === 'cancelled')
const invalidate = () => {
  void qc.invalidateQueries({ queryKey: ['tasks'] })
  void qc.invalidateQueries({ queryKey: ['tasks-summary'] })
  void qc.invalidateQueries({ queryKey: ['task-workers'] })
  void qc.invalidateQueries({ queryKey: ['task', props.taskId] })
}
const assign = useMutation({
  mutationFn: (userId: string | null) => tasksApi.assign(props.taskId!, userId),
  onSuccess: (t) => {
    notify.success(t.assigneeName ? 'Исполнитель назначен' : 'Исполнитель снят', t.assigneeName ?? t.number)
    invalidate()
  },
  onError: (e) => {
    notify.error(e)
    assignee.value = task.value?.assigneeId ?? null
  },
})
const cancel = useMutation({
  mutationFn: () => tasksApi.cancel(props.taskId!),
  onSuccess: (t) => {
    notify.success('Задание отменено', t.number)
    invalidate()
    void qc.invalidateQueries({ queryKey: ['picking-candidates'] })
  },
  onError: (e) => notify.error(e),
})
function askCancel() {
  confirm.require({
    header: 'Отменить задание?',
    message:
      task.value?.type === 'picking'
        ? 'Заказы вернутся в очередь на формирование батча.'
        : 'Задание будет закрыто без выполнения.',
    acceptLabel: 'Отменить задание',
    rejectLabel: 'Назад',
    acceptProps: { severity: 'danger' },
    rejectProps: { severity: 'secondary', text: true },
    accept: () => cancel.mutate(),
  })
}

const info = computed(() =>
  task.value
    ? [
        { label: 'Основание', value: task.value.reference },
        { label: 'Склад', value: task.value.warehouseName },
        { label: 'Создано', value: formatDateTime(task.value.createdAt) },
        { label: 'Начато', value: task.value.startedAt ? formatDateTime(task.value.startedAt) : '—' },
        { label: 'Завершено', value: task.value.finishedAt ? formatDateTime(task.value.finishedAt) : '—' },
        {
          label: 'Время',
          value: `${task.value.elapsedMinutes ?? 0} мин из нормы ${task.value.normMinutes} мин`,
        },
        ...(task.value.estimatedMinutes != null
          ? [{ label: 'Оценка маршрута', value: `~${task.value.estimatedMinutes} мин` }]
          : []),
        ...(task.value.volumeLiters != null
          ? [
              {
                label: 'Тара',
                value: `${formatNumber(task.value.volumeLiters, 1)} л из ${formatNumber(task.value.toteCapacityLiters ?? 60, 0)} л${
                  task.value.toteFill != null ? ` · ${Math.round(task.value.toteFill * 100)}%` : ''
                }`,
              },
            ]
          : []),
      ]
    : [],
)
const saving = computed(() => {
  if (!task.value || !(task.value.baselineDistance > 0)) return 0
  const opt = task.value.optimizedDistance ?? task.value.routeDistance
  return Math.round((1 - opt / task.value.baselineDistance) * 100)
})
</script>

<template>
  <Drawer
    v-model:visible="visible"
    position="right"
    class="!w-full sm:!w-[34rem]"
    :header="task ? `${TASK_TYPES[task.type].label} ${task.number}` : 'Задание'"
  >
    <div v-if="isPending && !task" class="space-y-3"><Skeleton v-for="i in 6" :key="i" height="2rem" /></div>
    <div v-else-if="task" class="space-y-5">
      <div class="flex flex-wrap items-center gap-2">
        <TaskStatusTag :status="task.status" />
        <Tag v-if="task.priority === 'high'" value="Срочно" severity="danger" />
        <Tag v-if="task.isLate" value="Превышен норматив" severity="warn" />
      </div>
      <div>
        <div class="mb-1 flex justify-between text-sm">
          <span class="text-secondary">Выполнено</span>
          <span class="tabular text-heading"
            >{{ formatNumber(task.doneUnits) }} / {{ formatNumber(task.units) }} шт</span
          >
        </div>
        <ProgressMeter
          :value="task.units ? (task.doneUnits / task.units) * 100 : 0"
          :tone="task.status === 'done' ? 'success' : 'primary'"
        />
      </div>
      <div v-if="!closed" class="space-y-2">
        <label class="text-sm font-medium text-heading" for="task-assignee">Исполнитель</label>
        <div class="flex flex-col gap-2 sm:flex-row">
          <Select
            v-model="assignee"
            input-id="task-assignee"
            :options="options"
            option-label="label"
            option-value="value"
            placeholder="Не назначен"
            show-clear
            class="min-w-0 flex-1"
            :disabled="assign.isPending.value"
            @update:model-value="assign.mutate($event ?? null)"
          />
          <Button severity="danger" text :loading="cancel.isPending.value" @click="askCancel"
            ><Ban class="size-4" />Отменить</Button
          >
        </div>
        <p class="text-xs text-secondary">
          Показаны сотрудники с подходящей ролью; предпочтение — на смене, со навыком и меньшей загрузкой. Ручное назначение не ограничено сменой.
        </p>
      </div>
      <div v-else-if="task.assigneeName" class="text-sm">
        Исполнитель: <span class="font-medium text-heading">{{ task.assigneeName }}</span>
      </div>

      <InfoList :items="info" />

      <div>
        <div class="mb-2 flex flex-wrap items-center justify-between gap-2">
          <h3 class="text-sm font-semibold text-heading">Маршрут · {{ task.steps.length }} точек</h3>
          <span class="inline-flex flex-wrap items-center gap-1 text-xs text-secondary">
            <Route class="size-3.5" />
            {{ formatNumber(task.optimizedDistance ?? task.routeDistance, 1) }} м
            <template v-if="task.baselineDistance">
              · базовый {{ formatNumber(task.baselineDistance, 1) }} м
            </template>
            <template v-if="saving > 0"> · −{{ saving }}%</template>
          </span>
        </div>
        <ol class="divide-y divide-default rounded-lg border border-default">
          <li v-for="s in task.steps" :key="s.index" class="flex items-center gap-3 px-3 py-2">
            <span
              class="flex size-6 shrink-0 items-center justify-center rounded-full bg-primary-50 text-xs font-semibold text-primary-700 dark:bg-primary-950 dark:text-primary-300"
              >{{ s.index + 1 }}</span
            >
            <div class="min-w-0 flex-1">
              <div class="truncate text-sm text-heading">{{ s.productName }}</div>
              <div class="flex items-center gap-1 text-xs text-secondary">
                <MapPin class="size-3" />{{ s.locationCode }} · {{ s.sku }}
              </div>
            </div>
            <span
              class="tabular whitespace-nowrap text-sm"
              :class="s.done >= s.quantity ? 'text-emerald-600' : 'text-heading'"
              >{{ s.done }}/{{ s.quantity }}</span
            >
          </li>
        </ol>
      </div>
    </div>
  </Drawer>
</template>
