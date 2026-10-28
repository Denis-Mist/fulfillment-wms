<script setup lang="ts">
import EmptyState from '@/shared/ui/EmptyState.vue'
import { computed, ref } from 'vue'
import dayjs from 'dayjs'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { useStorage } from '@vueuse/core'
import { useConfirm } from 'primevue/useconfirm'
import { CalendarCheck, Target, TrendingUp, Users } from '@lucide/vue'
import { analyticsApi } from '@/modules/analytics/api'
import type { ForecastAlgorithm } from '@/modules/analytics/types'
import { useChartTheme } from '@/shared/composables/useChartTheme'
import { useNotify } from '@/shared/composables/useNotify'
import { formatDateTime, formatNumber } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import BaseChart from '@/shared/ui/BaseChart.vue'
import type { ChartOption } from '@/shared/ui/chart'
import KpiCard from '@/shared/ui/KpiCard.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'
import CalcMetaLine from './CalcMetaLine.vue'

const session = useSessionStore()
const notify = useNotify()
const confirm = useConfirm()
const qc = useQueryClient()
const theme = useChartTheme()

const algorithm = useStorage<ForecastAlgorithm>('wms.analytics.forecastAlgorithm', 'holt_winters')
const horizon = ref(14)
const algorithmOptions = [
  { value: 'holt_winters', label: 'Хольт–Уинтерс' },
  { value: 'moving_average', label: 'Базовый (среднее 7 дн)' },
]
const horizonOptions = [
  { value: 7, label: '7 дн' },
  { value: 14, label: '14 дн' },
  { value: 28, label: '28 дн' },
]
const warehouseId = computed(() => session.warehouseId || undefined)

const { data, isPending } = useQuery({
  queryKey: computed(() => [
    'analytics-forecast',
    session.organizationId,
    warehouseId.value,
    horizon.value,
    algorithm.value,
  ]),
  queryFn: () =>
    analyticsApi.forecast({
      warehouseId: warehouseId.value,
      horizon: horizon.value,
      algorithm: algorithm.value,
    }),
  placeholderData: keepPreviousData,
})

const approve = useMutation({
  mutationFn: () => analyticsApi.approvePlan({ warehouseId: warehouseId.value, algorithm: algorithm.value }),
  onSuccess: () => {
    notify.success('План персонала утверждён', 'Рекомендация передана в график смен')
    void qc.invalidateQueries({ queryKey: ['analytics-forecast'] })
  },
  onError: (e) => notify.error(e),
})

function askApprove() {
  confirm.require({
    header: 'Утвердить план персонала?',
    message: `Рекомендованная численность на ${horizon.value} дней будет зафиксирована как план смен. Её можно скорректировать вручную в разделе «Сотрудники и смены».`,
    acceptLabel: 'Утвердить',
    rejectLabel: 'Отмена',
    accept: () => approve.mutate(),
  })
}

const peak = computed(() => Math.max(0, ...(data.value?.staffing ?? []).map((s) => s.total)))
const forecastTotal = computed(() => (data.value?.staffing ?? []).reduce((a, s) => a + s.orders, 0))
const shortage = computed(() => (data.value?.staffing ?? []).filter((s) => s.total > s.available).length)

const option = computed<ChartOption>(() => {
  const t = theme.value
  const s = data.value?.series ?? []
  return {
    grid: { left: 4, right: 8, top: 30, bottom: 4, containLabel: true },
    tooltip: { trigger: 'axis' },
    legend: { top: 0, left: 0, textStyle: { color: t.text }, itemWidth: 14, itemHeight: 8 },
    xAxis: {
      type: 'category',
      data: s.map((p) => dayjs(p.date).format('D MMM')),
      axisTick: { show: false },
      axisLine: { lineStyle: { color: t.grid } },
      axisLabel: { color: t.muted, hideOverlap: true },
    },
    yAxis: {
      type: 'value',
      splitLine: { lineStyle: { color: t.grid, type: 'dashed' } },
      axisLabel: { color: t.muted },
    },
    series: [
      {
        name: 'Факт',
        type: 'line',
        symbol: 'none',
        data: s.map((p) => p.actual ?? null),
        lineStyle: { color: '#64748b', width: 1.5 },
        itemStyle: { color: '#64748b' },
      },
      {
        name: 'Прогноз',
        type: 'line',
        symbol: 'none',
        data: s.map((p) => p.predicted ?? null),
        lineStyle: { color: '#6366f1', width: 2 },
        itemStyle: { color: '#6366f1' },
      },
      {
        name: 'Нижняя граница',
        type: 'line',
        symbol: 'none',
        stack: 'band',
        data: s.map((p) => p.low ?? null),
        lineStyle: { opacity: 0 },
        itemStyle: { color: '#c7d2fe' },
      },
      {
        name: 'Интервал 80%',
        type: 'line',
        symbol: 'none',
        stack: 'band',
        data: s.map((p) => (p.high != null && p.low != null ? p.high - p.low : null)),
        lineStyle: { opacity: 0 },
        areaStyle: { color: 'rgba(99,102,241,0.15)' },
        itemStyle: { color: 'rgba(99,102,241,0.3)' },
      },
    ],
  }
})
</script>

<template>
  <div class="space-y-5">
    <div class="flex flex-wrap items-center gap-3">
      <SelectButton
        v-model="algorithm"
        :options="algorithmOptions"
        option-label="label"
        option-value="value"
        :allow-empty="false"
        size="small"
      />
      <SelectButton
        v-model="horizon"
        :options="horizonOptions"
        option-label="label"
        option-value="value"
        :allow-empty="false"
        size="small"
      />
    </div>

    <div class="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <KpiCard
        label="Прогноз заказов"
        :value="formatNumber(forecastTotal)"
        :icon="TrendingUp"
        tone="info"
        :hint="`на ${horizon} дней`"
        :loading="isPending"
      />
      <KpiCard
        label="Точность (MAPE)"
        :value="data ? `${data.backtest.mape}%` : '—'"
        :icon="Target"
        :tone="(data?.backtest.mape ?? 0) <= 10 ? 'success' : 'warn'"
        :hint="data ? `бэктест ${data.backtest.days} дн · базовый ${data.backtest.baselineMape}%` : undefined"
        :loading="isPending"
      />
      <KpiCard
        label="Пиковая потребность"
        :value="`${peak} чел.`"
        :icon="Users"
        tone="progress"
        hint="в смену"
        :loading="isPending"
      />
      <KpiCard
        label="Дней с нехваткой"
        :value="String(shortage)"
        :icon="CalendarCheck"
        :tone="shortage ? 'danger' : 'success'"
        :hint="data?.staffing[0] ? `в штате ${data.staffing[0].available} чел.` : undefined"
        :loading="isPending"
      />
    </div>

    <SectionCard
      title="Заказы в день: факт и прогноз"
      description="Последние 14 дней истории — проверка точности на отложенной выборке"
    >
      <EmptyState v-if="!isPending && !data?.series?.length" compact title="Данных пока нет" />
      <BaseChart v-else :option="option" height="300px" />
      <CalcMetaLine class="mt-2" :meta="data?.meta" />
    </SectionCard>

    <SectionCard
      title="Рекомендуемая численность"
      description="Расчёт по нормативам выработки: отбор 420, упаковка 260, приёмка 900 ед. в смену"
      flush
    >
      <template #actions>
        <div class="flex flex-wrap items-center gap-2">
          <Tag
            v-if="data?.plan"
            severity="success"
            :value="`Утверждён ${formatDateTime(data.plan.approvedAt)}`"
          />
          <Button
            label="Утвердить план"
            size="small"
            :loading="approve.isPending.value"
            :disabled="!data"
            @click="askApprove"
          />
        </div>
      </template>
      <div class="overflow-x-auto">
        <table class="w-full min-w-[36rem] text-sm">
          <thead class="text-left text-xs text-secondary uppercase">
            <tr class="border-b border-default">
              <th class="px-5 py-2 font-medium">Дата</th>
              <th class="px-3 py-2 text-right font-medium">Заказы</th>
              <th class="px-3 py-2 text-right font-medium">Единиц</th>
              <th class="px-3 py-2 text-right font-medium">Отбор</th>
              <th class="px-3 py-2 text-right font-medium">Упаковка</th>
              <th class="px-3 py-2 text-right font-medium">Приёмка</th>
              <th class="px-5 py-2 text-right font-medium">Итого / штат</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="d in data?.staffing ?? []" :key="d.date" class="border-b border-default last:border-0">
              <td class="px-5 py-2 whitespace-nowrap">{{ dayjs(d.date).format('dd, D MMM') }}</td>
              <td class="tabular px-3 py-2 text-right">{{ formatNumber(d.orders) }}</td>
              <td class="tabular px-3 py-2 text-right">{{ formatNumber(d.units) }}</td>
              <td class="tabular px-3 py-2 text-right">{{ d.pickers }}</td>
              <td class="tabular px-3 py-2 text-right">{{ d.packers }}</td>
              <td class="tabular px-3 py-2 text-right">{{ d.receivers }}</td>
              <td
                class="tabular px-5 py-2 text-right font-medium"
                :class="d.total > d.available ? 'text-red-600 dark:text-red-400' : 'text-heading'"
              >
                {{ d.total }} / {{ d.available }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </SectionCard>
  </div>
</template>
