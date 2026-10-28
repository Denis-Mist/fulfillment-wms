<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { ArrowLeft, CheckCircle2, ChevronRight, ListChecks, MapPin, Play, ScanBarcode } from '@lucide/vue'
import { TASK_TYPES } from '@/modules/tasks/labels'
import { tasksApi } from '@/modules/tasks/api'
import type { TaskDetails, TaskListItem } from '@/modules/tasks/types'
import { ApiError } from '@/shared/api/http'
import { useNotify } from '@/shared/composables/useNotify'
import { formatNumber, formatTime } from '@/shared/lib/format'
import EmptyState from '@/shared/ui/EmptyState.vue'
import ProgressMeter from '@/shared/ui/ProgressMeter.vue'
import TaskStatusTag from '../tasks/TaskStatusTag.vue'

const notify = useNotify()
const qc = useQueryClient()

const { data: list, isPending } = useQuery({
  queryKey: ['work-tasks'],
  queryFn: () => tasksApi.myTasks(),
  refetchInterval: 30_000,
})
const active = computed(() => (list.value ?? []).filter((t) => t.status !== 'done'))
const done = computed(() => (list.value ?? []).filter((t) => t.status === 'done'))

const openId = ref<string | null>(null)
const { data: fetched } = useQuery({
  queryKey: computed(() => ['task', openId.value]),
  queryFn: () => tasksApi.get(openId.value!),
  enabled: computed(() => !!openId.value),
})
const task = ref<TaskDetails | null>(null)
watch(fetched, (t) => {
  if (t && t.id === openId.value) task.value = t
})
const step = computed(() => task.value?.steps.find((s) => s.done < s.quantity))
const allDone = computed(() => !!task.value && !step.value)
const locationConfirmed = ref(false)
watch(
  () => step.value?.index,
  () => (locationConfirmed.value = false),
)

const code = ref('')
const feedback = ref<{ ok: boolean; text: string } | null>(null)
const input = ref<{ $el: HTMLInputElement } | null>(null)
const focusInput = () => void nextTick(() => input.value?.$el?.focus?.())

function open(t: TaskListItem) {
  openId.value = t.id
  task.value = null
  feedback.value = null
}
function back() {
  openId.value = null
  task.value = null
  void qc.invalidateQueries({ queryKey: ['work-tasks'] })
}

const start = useMutation({
  mutationFn: () => tasksApi.start(task.value!.id),
  onSuccess: (t) => {
    task.value = t
    focusInput()
  },
  onError: (e) => notify.error(e),
})
const scan = useMutation({
  mutationFn: (value: string) => tasksApi.scan(task.value!.id, value),
  onSuccess: (r, value) => {
    const wasLocation = step.value && value.toUpperCase() === step.value.locationCode.toUpperCase()
    task.value = r.task
    if (wasLocation) locationConfirmed.value = true
    feedback.value = { ok: true, text: r.message }
    code.value = ''
    focusInput()
  },
  onError: (e) => {
    feedback.value = {
      ok: false,
      text:
        e instanceof ApiError
          ? [e.problem.title, e.problem.detail].filter(Boolean).join('. ')
          : 'Ошибка сканирования',
    }
    code.value = ''
    if (navigator.vibrate) navigator.vibrate(200)
    focusInput()
  },
})
const complete = useMutation({
  mutationFn: () => tasksApi.complete(task.value!.id),
  onSuccess: (t) => {
    notify.success('Задание выполнено', `${t.number} · ${t.units} шт`)
    void qc.invalidateQueries({ queryKey: ['work-tasks'] })
    back()
  },
  onError: (e) => notify.error(e),
})
function submit() {
  if (code.value.trim()) scan.mutate(code.value.trim())
}
/** Без аппаратного сканера (демо): имитация считывания ожидаемого кода */
function simulate() {
  if (!step.value) return
  scan.mutate(locationConfirmed.value ? step.value.barcode : step.value.locationCode)
}
</script>

<template>
  <div>
    <!-- Выполнение задания -->
    <div v-if="openId" class="space-y-4">
      <button type="button" class="inline-flex items-center gap-1 text-sm text-secondary" @click="back">
        <ArrowLeft class="size-4" />Все задания
      </button>
      <div v-if="!task" class="space-y-3"><Skeleton height="6rem" /><Skeleton height="12rem" /></div>
      <template v-else>
        <div class="surface-card p-4">
          <div class="flex items-start justify-between gap-2">
            <div>
              <div class="text-lg font-semibold text-heading">
                {{ TASK_TYPES[task.type].label }} {{ task.number }}
              </div>
              <div class="text-xs text-secondary">
                {{ task.reference }} · {{ task.steps.length }} точек ·
                {{ formatNumber(task.routeDistance, 1) }} м
              </div>
            </div>
            <TaskStatusTag :status="task.status" />
          </div>
          <div class="mt-3">
            <div class="tabular mb-1 text-xs text-secondary">{{ task.doneUnits }} из {{ task.units }} шт</div>
            <ProgressMeter
              :value="task.units ? (task.doneUnits / task.units) * 100 : 0"
              :tone="allDone ? 'success' : 'primary'"
            />
          </div>
        </div>

        <Button
          v-if="task.status !== 'in_progress'"
          class="w-full !py-3 !text-base"
          :loading="start.isPending.value"
          @click="start.mutate()"
        >
          <Play class="size-5" />Начать задание
        </Button>

        <template v-else>
          <div v-if="step" class="surface-card space-y-3 p-4">
            <div class="text-xs font-medium uppercase tracking-wide text-secondary">
              Шаг {{ step.index + 1 }} из {{ task.steps.length }}
            </div>
            <div
              class="flex items-center gap-3 rounded-xl p-3"
              :class="
                locationConfirmed ? 'bg-emerald-50 dark:bg-emerald-950' : 'bg-primary-50 dark:bg-primary-950'
              "
            >
              <MapPin
                class="size-7 shrink-0"
                :class="locationConfirmed ? 'text-emerald-600' : 'text-primary'"
              />
              <div>
                <div class="font-mono text-2xl font-bold tracking-wide text-heading">
                  {{ step.locationCode }}
                </div>
                <div class="text-xs text-secondary">
                  {{
                    locationConfirmed
                      ? 'Ячейка подтверждена'
                      : `Пройдите ${formatNumber(step.distance, 1)} м и отсканируйте ячейку`
                  }}
                </div>
              </div>
            </div>
            <div>
              <div class="font-medium text-heading">{{ step.productName }}</div>
              <div class="text-xs text-secondary">{{ step.sku }} · ШК {{ step.barcode }}</div>
            </div>
            <div class="flex items-baseline gap-2">
              <span class="tabular text-3xl font-bold text-heading">{{ step.done }}</span>
              <span class="text-lg text-secondary">/ {{ step.quantity }} шт</span>
            </div>
          </div>
          <div v-else class="surface-card flex items-center gap-3 p-4">
            <CheckCircle2 class="size-8 text-emerald-600" />
            <div class="text-sm text-heading">Все позиции выполнены. Подтвердите завершение.</div>
          </div>

          <Message v-if="feedback" :severity="feedback.ok ? 'success' : 'error'" :closable="false">{{
            feedback.text
          }}</Message>

          <form v-if="step" class="flex gap-2" @submit.prevent="submit">
            <InputText
              ref="input"
              v-model="code"
              placeholder="Скан ячейки или товара"
              class="min-w-0 flex-1 !py-3"
              autocomplete="off"
              aria-label="Код сканирования"
            />
            <Button type="submit" :loading="scan.isPending.value" aria-label="Отправить код"
              ><ScanBarcode class="size-5"
            /></Button>
          </form>
          <Button
            v-if="step"
            class="w-full"
            severity="secondary"
            outlined
            :disabled="scan.isPending.value"
            @click="simulate"
          >
            Демо: считать {{ locationConfirmed ? 'товар' : 'ячейку' }}
          </Button>
          <Button
            class="w-full !py-3 !text-base"
            :severity="allDone ? undefined : 'secondary'"
            :disabled="!allDone && task.type !== 'inventory'"
            :loading="complete.isPending.value"
            @click="complete.mutate()"
          >
            <CheckCircle2 class="size-5" />Завершить задание
          </Button>

          <ol class="divide-y divide-default rounded-xl border border-default">
            <li
              v-for="s in task.steps"
              :key="s.index"
              class="flex items-center gap-3 px-3 py-2 text-sm"
              :class="s.index === step?.index && 'bg-primary-50/60 dark:bg-primary-950/40'"
            >
              <span class="w-5 text-xs text-secondary">{{ s.index + 1 }}</span>
              <span class="font-mono text-xs">{{ s.locationCode }}</span>
              <span class="min-w-0 flex-1 truncate">{{ s.productName }}</span>
              <span class="tabular" :class="s.done >= s.quantity && 'text-emerald-600'"
                >{{ s.done }}/{{ s.quantity }}</span
              >
            </li>
          </ol>
        </template>
      </template>
    </div>

    <!-- Список заданий -->
    <div v-else class="space-y-4">
      <div v-if="isPending" class="space-y-3"><Skeleton v-for="i in 3" :key="i" height="5rem" /></div>
      <EmptyState
        v-else-if="!active.length && !done.length"
        :icon="ListChecks"
        title="Заданий нет"
        description="Бригадир назначит задание — оно появится здесь"
      />
      <template v-else>
        <button
          v-for="t in active"
          :key="t.id"
          type="button"
          class="surface-card flex w-full items-center gap-3 p-4 text-left transition hover:shadow-md"
          @click="open(t)"
        >
          <div class="min-w-0 flex-1">
            <div class="flex flex-wrap items-center gap-2">
              <span class="font-semibold text-heading">{{ TASK_TYPES[t.type].label }}</span>
              <TaskStatusTag :status="t.status" />
              <Tag v-if="t.priority === 'high'" value="Срочно" severity="danger" />
            </div>
            <div class="mt-0.5 truncate text-xs text-secondary">{{ t.number }} · {{ t.reference }}</div>
            <div class="mt-2"><ProgressMeter :value="t.units ? (t.doneUnits / t.units) * 100 : 0" /></div>
            <div class="tabular mt-1 text-xs text-secondary">
              {{ t.doneUnits }}/{{ t.units }} шт · норматив {{ t.normMinutes }} мин
            </div>
          </div>
          <ChevronRight class="size-5 shrink-0 text-secondary" />
        </button>
        <div v-if="done.length">
          <h2 class="mb-2 mt-6 text-sm font-semibold text-heading">Выполнено сегодня · {{ done.length }}</h2>
          <ul class="divide-y divide-default rounded-xl border border-default">
            <li
              v-for="t in done"
              :key="t.id"
              class="flex items-center justify-between gap-2 px-4 py-2.5 text-sm"
            >
              <span>{{ TASK_TYPES[t.type].label }} {{ t.number }}</span>
              <span class="tabular text-xs text-secondary"
                >{{ t.units }} шт · {{ t.finishedAt ? formatTime(t.finishedAt) : '' }}</span
              >
            </li>
          </ul>
        </div>
      </template>
    </div>
  </div>
</template>
