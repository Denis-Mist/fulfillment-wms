<script setup lang="ts">
import { computed, ref } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { Activity, Target, UserCheck, Users } from '@lucide/vue'
import dayjs from 'dayjs'
import { ROLE_LABELS, WORKER_ROLES } from '@/modules/auth/roles'
import type { Role } from '@/modules/auth/types'
import { staffApi } from '@/modules/staff/api'
import { SHIFT_CYCLE, SHIFTS, SKILLS } from '@/modules/staff/labels'
import type { Employee, Shift } from '@/modules/staff/types'
import { useNotify } from '@/shared/composables/useNotify'
import { formatNumber } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import FilterChips from '@/shared/ui/FilterChips.vue'
import KpiCard from '@/shared/ui/KpiCard.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import RegistryTable from '@/shared/ui/RegistryTable.vue'
import SearchInput from '@/shared/ui/SearchInput.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'
import type { ChipOption } from '@/shared/ui/types'
import ToneBadge from '@/pages/stock/ToneBadge.vue'
import EmployeeDrawer from './EmployeeDrawer.vue'

const session = useSessionStore()
const notify = useNotify()
const qc = useQueryClient()
const warehouseId = computed(() => session.warehouseId || undefined)
const { data, isPending } = useQuery({
  queryKey: computed(() => ['staff', session.organizationId, warehouseId.value]),
  queryFn: () => staffApi.list(warehouseId.value),
})

const tab = ref('list')
const group = ref<'office' | 'worker' | undefined>(undefined)
const search = ref('')
const all = computed(() => data.value?.items ?? [])
const isWorker = (e: Employee) => WORKER_ROLES.includes(e.role)
const items = computed(() => {
  const q = search.value.trim().toLowerCase()
  return all.value.filter(
    (e) =>
      (!group.value || (group.value === 'worker') === isWorker(e)) &&
      (!q || e.fullName.toLowerCase().includes(q)),
  )
})
const chips = computed<ChipOption<'office' | 'worker'>[]>(() => [
  { value: undefined, label: 'Все', count: all.value.length },
  { value: 'worker', label: 'Складской персонал', count: all.value.filter(isWorker).length },
  { value: 'office', label: 'Офис и смена', count: all.value.filter((e) => !isWorker(e)).length },
])

const workers = computed(() => all.value.filter((e) => isWorker(e) && e.active))
const avg = (f: (e: Employee) => number) =>
  workers.value.length ? workers.value.reduce((s, e) => s + f(e), 0) / workers.value.length : 0
const kpi = computed(() => ({
  onShift: all.value.filter((e) => e.onShift).length,
  perf: Math.round(avg((e) => (e.norm ? (e.opsPerHour / e.norm) * 100 : 0))),
  accuracy: avg((e) => e.accuracy),
}))

const canEdit = computed(() => !!session.role && ['fc_owner', 'fc_manager'].includes(session.role))
const canPlan = computed(
  () => !!session.role && ['fc_owner', 'fc_manager', 'fc_supervisor'].includes(session.role),
)

const days = computed(() => {
  const start = dayjs(data.value?.weekStart)
  return Array.from({ length: 7 }, (_, i) => {
    const d = start.add(i, 'day')
    return { label: d.format('dd'), date: d.format('D.MM'), today: d.isSame(dayjs(), 'day') }
  })
})
const coverage = computed(() =>
  days.value.map((_, d) => ({
    day: workers.value.filter((e) => e.shifts[d] === 'day').length,
    night: workers.value.filter((e) => e.shifts[d] === 'night').length,
  })),
)
const setShift = useMutation({
  mutationFn: (v: { id: string; day: number; shift: Shift }) => staffApi.setShift(v.id, v.day, v.shift),
  onSuccess: (e) => {
    qc.setQueryData(['staff', session.organizationId, warehouseId.value], (old: typeof data.value) =>
      old ? { ...old, items: old.items.map((x) => (x.id === e.id ? e : x)) } : old,
    )
  },
  onError: (e: unknown) => notify.error(e),
})
function cycle(e: Employee, day: number) {
  if (!canPlan.value) return
  const next = SHIFT_CYCLE[(SHIFT_CYCLE.indexOf(e.shifts[day]!) + 1) % SHIFT_CYCLE.length]!
  setShift.mutate({ id: e.id, day, shift: next })
}

const perfTone = (e: Employee) =>
  !e.norm ? 'neutral' : e.opsPerHour >= e.norm ? 'success' : e.opsPerHour >= e.norm * 0.85 ? 'warn' : 'danger'

const selected = ref<Employee | null>(null)
const drawer = ref(false)
function open(e: Employee) {
  selected.value = e
  drawer.value = true
}
</script>

<template>
  <div>
    <PageHeader title="Персонал" description="Сотрудники, роли и допуски, производительность и график смен" />

    <div class="mb-5 grid grid-cols-2 gap-3 lg:grid-cols-4">
      <KpiCard label="Сотрудников" :icon="Users" :value="formatNumber(all.length)" :loading="isPending" />
      <KpiCard
        label="На смене сейчас"
        :icon="UserCheck"
        tone="success"
        :value="formatNumber(kpi.onShift)"
        :loading="isPending"
      />
      <KpiCard
        label="Выполнение нормы"
        :icon="Activity"
        :tone="kpi.perf >= 100 ? 'success' : 'warn'"
        :value="`${kpi.perf}%`"
        hint="в среднем по складскому персоналу"
        :loading="isPending"
      />
      <KpiCard
        label="Точность операций"
        :icon="Target"
        :value="`${kpi.accuracy.toFixed(1)}%`"
        hint="доля без ошибок сканирования"
        :loading="isPending"
      />
    </div>

    <Tabs v-model:value="tab">
      <TabList>
        <Tab value="list">Сотрудники</Tab>
        <Tab value="shifts">График смен</Tab>
      </TabList>
      <TabPanels class="!px-0">
        <TabPanel value="list">
          <div class="mb-4 space-y-3">
            <FilterChips v-model="group" :options="chips" />
            <SearchInput v-model="search" placeholder="ФИО сотрудника…" />
          </div>
          <RegistryTable
            :items="items"
            :total="items.length"
            :loading="isPending"
            :first="0"
            :rows="200"
            clickable
            empty-title="Сотрудники не найдены"
            @row-click="open"
          >
            <Column header="Сотрудник" class="min-w-52">
              <template #body="{ data: e }">
                <div class="flex items-center gap-2 font-medium text-heading">
                  <span
                    class="size-2 shrink-0 rounded-full"
                    :class="e.onShift ? 'bg-emerald-500' : 'bg-surface-300 dark:bg-surface-600'"
                    :title="e.onShift ? 'На смене' : 'Не на смене'"
                  />
                  <span class="whitespace-nowrap" :class="!e.active && 'line-through opacity-60'">{{
                    e.fullName
                  }}</span>
                </div>
                <div class="text-xs whitespace-nowrap text-secondary">{{ ROLE_LABELS[e.role as Role] }}</div>
              </template>
            </Column>
            <Column field="warehouseName" header="Склад" class="min-w-36" />
            <Column header="Навыки" class="min-w-56">
              <template #body="{ data: e }">
                <div class="flex flex-wrap gap-1">
                  <Tag
                    v-for="s in e.skills"
                    :key="s"
                    :value="SKILLS[s as keyof typeof SKILLS]"
                    severity="secondary"
                    class="!text-xs"
                  />
                  <span v-if="!e.skills.length" class="text-xs text-secondary">—</span>
                </div>
              </template>
            </Column>
            <Column header="Оп/ч" class="min-w-28">
              <template #body="{ data: e }">
                <ToneBadge v-if="e.norm" :tone="perfTone(e)" :label="`${e.opsPerHour} / ${e.norm}`" />
                <span v-else class="text-xs text-secondary">—</span>
              </template>
            </Column>
            <Column header="Точность" class="min-w-24">
              <template #body="{ data: e }">
                <span class="tabular" :class="e.norm && e.accuracy < 98 ? 'text-rose-600' : 'text-heading'">{{
                  e.norm ? `${e.accuracy}%` : '—'
                }}</span>
              </template>
            </Column>
            <Column header="Сегодня" class="min-w-32">
              <template #body="{ data: e }">
                <span class="text-sm whitespace-nowrap text-secondary">{{
                  SHIFTS[e.shifts[(dayjs().day() + 6) % 7] as Shift].label
                }}</span>
              </template>
            </Column>
          </RegistryTable>
        </TabPanel>

        <TabPanel value="shifts">
          <SectionCard
            title="Неделя"
            :description="
              canPlan ? 'Нажмите на ячейку, чтобы сменить тип смены' : 'График складского персонала'
            "
            flush
          >
            <template #actions>
              <div class="flex flex-wrap gap-2 text-xs">
                <span v-for="s in SHIFT_CYCLE" :key="s" class="rounded px-1.5 py-0.5" :class="SHIFTS[s].cls"
                  >{{ SHIFTS[s].short }} {{ SHIFTS[s].label }}</span
                >
              </div>
            </template>
            <div class="overflow-x-auto">
              <table class="w-full min-w-[40rem] text-sm">
                <thead>
                  <tr class="border-b border-default text-xs text-secondary">
                    <th
                      class="sticky left-0 bg-[var(--p-content-background)] px-4 py-2 text-left font-medium"
                    >
                      Сотрудник
                    </th>
                    <th
                      v-for="d in days"
                      :key="d.date"
                      class="px-1 py-2 text-center font-medium"
                      :class="d.today && 'text-primary-600'"
                    >
                      <div class="capitalize">{{ d.label }}</div>
                      <div>{{ d.date }}</div>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="e in workers" :key="e.id" class="border-b border-default last:border-0">
                    <td class="sticky left-0 bg-[var(--p-content-background)] px-4 py-1.5">
                      <div class="font-medium whitespace-nowrap text-heading">{{ e.fullName }}</div>
                      <div class="text-xs whitespace-nowrap text-secondary">{{ ROLE_LABELS[e.role] }}</div>
                    </td>
                    <td v-for="(s, i) in e.shifts" :key="i" class="px-1 py-1.5 text-center">
                      <button
                        type="button"
                        class="h-8 w-full min-w-9 rounded-md text-xs font-semibold transition"
                        :class="[
                          SHIFTS[s].cls,
                          canPlan ? 'cursor-pointer hover:ring-2 hover:ring-primary-300' : 'cursor-default',
                        ]"
                        :title="SHIFTS[s].label"
                        :disabled="!canPlan"
                        @click="cycle(e, i)"
                      >
                        {{ SHIFTS[s].short }}
                      </button>
                    </td>
                  </tr>
                </tbody>
                <tfoot>
                  <tr class="text-xs text-secondary">
                    <td class="sticky left-0 bg-[var(--p-content-background)] px-4 py-2 font-medium">
                      Покрытие Д / Н
                    </td>
                    <td
                      v-for="(c, i) in coverage"
                      :key="i"
                      class="px-1 py-2 text-center tabular"
                      :class="c.day < 2 && 'font-semibold text-rose-600'"
                    >
                      {{ c.day }} / {{ c.night }}
                    </td>
                  </tr>
                </tfoot>
              </table>
            </div>
          </SectionCard>
        </TabPanel>
      </TabPanels>
    </Tabs>

    <EmployeeDrawer v-model:visible="drawer" :employee="selected" :editable="canEdit" />
  </div>
</template>
