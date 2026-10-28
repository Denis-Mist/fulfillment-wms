<script setup lang="ts">
import EmptyState from '@/shared/ui/EmptyState.vue'
import { computed, ref } from 'vue'
import dayjs from 'dayjs'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import { Activity, Clock, Gauge, Route, ShieldAlert, Truck } from '@lucide/vue'
import { analyticsApi } from '@/modules/analytics/api'
import { organizationsApi } from '@/modules/organizations/api'
import { useChartTheme } from '@/shared/composables/useChartTheme'
import { formatNumber } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import BaseChart from '@/shared/ui/BaseChart.vue'
import type { ChartOption } from '@/shared/ui/chart'
import KpiCard from '@/shared/ui/KpiCard.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'

const session = useSessionStore()
const theme = useChartTheme()
const period = ref(30)
const sellerId = ref<string | undefined>()
const periodOptions = [
  { value: 7, label: '7 дн' },
  { value: 30, label: '30 дн' },
  { value: 56, label: '8 нед' },
]
const warehouseId = computed(() => session.warehouseId || undefined)

const { data: clients } = useQuery({
  queryKey: computed(() => ['clients', session.organizationId]),
  queryFn: () => organizationsApi.clients(),
})
const clientOptions = computed(() => (clients.value ?? []).map((c) => ({ value: c.id, label: c.name })))

const { data, isPending } = useQuery({
  queryKey: computed(() => [
    'analytics-kpi',
    session.organizationId,
    warehouseId.value,
    sellerId.value,
    period.value,
  ]),
  queryFn: () =>
    analyticsApi.kpi({ warehouseId: warehouseId.value, sellerId: sellerId.value, period: period.value }),
  placeholderData: keepPreviousData,
})
const t = computed(() => data.value?.totals)

const axis = () => ({
  type: 'category' as const,
  data: (data.value?.series ?? []).map((p) => dayjs(p.date).format('D MMM')),
  axisTick: { show: false },
  axisLine: { lineStyle: { color: theme.value.grid } },
  axisLabel: { color: theme.value.muted, hideOverlap: true },
})
const yAxis = () => ({
  type: 'value' as const,
  splitLine: { lineStyle: { color: theme.value.grid, type: 'dashed' as const } },
  axisLabel: { color: theme.value.muted },
})

const routeOption = computed<ChartOption>(() => ({
  grid: { left: 4, right: 8, top: 30, bottom: 4, containLabel: true },
  tooltip: { trigger: 'axis' },
  legend: { top: 0, left: 0, textStyle: { color: theme.value.text }, itemWidth: 14, itemHeight: 8 },
  xAxis: axis(),
  yAxis: yAxis(),
  series: [
    {
      name: 'Базовый маршрут, м',
      type: 'line',
      symbol: 'none',
      data: (data.value?.series ?? []).map((p) => p.routeBaseline),
      lineStyle: { color: '#94a3b8', type: 'dashed' },
      itemStyle: { color: '#94a3b8' },
    },
    {
      name: 'Фактический маршрут, м',
      type: 'line',
      symbol: 'none',
      data: (data.value?.series ?? []).map((p) => p.routeOptimized),
      lineStyle: { color: '#6366f1', width: 2 },
      itemStyle: { color: '#6366f1' },
      areaStyle: { color: 'rgba(99,102,241,0.08)' },
    },
  ],
}))

const ordersOption = computed<ChartOption>(() => ({
  grid: { left: 4, right: 8, top: 30, bottom: 4, containLabel: true },
  tooltip: { trigger: 'axis' },
  legend: { top: 0, left: 0, textStyle: { color: theme.value.text }, itemWidth: 14, itemHeight: 8 },
  xAxis: axis(),
  yAxis: [yAxis(), { ...yAxis(), splitLine: { show: false } }],
  series: [
    {
      name: 'Заказы',
      type: 'bar',
      barMaxWidth: 14,
      data: (data.value?.series ?? []).map((p) => p.orders),
      itemStyle: { color: '#22c55e', borderRadius: [3, 3, 0, 0] },
    },
    {
      name: 'Время обработки, ч',
      type: 'line',
      yAxisIndex: 1,
      symbol: 'none',
      data: (data.value?.series ?? []).map((p) => p.processingHours),
      lineStyle: { color: '#f59e0b' },
      itemStyle: { color: '#f59e0b' },
    },
  ],
}))
</script>

<template>
  <div class="space-y-5">
    <div class="flex flex-wrap items-center gap-3">
      <SelectButton
        v-model="period"
        :options="periodOptions"
        option-label="label"
        option-value="value"
        :allow-empty="false"
        size="small"
      />
      <Select
        v-model="sellerId"
        :options="clientOptions"
        option-label="label"
        option-value="value"
        placeholder="Все клиенты"
        show-clear
        class="w-full sm:w-64"
      />
    </div>

    <div class="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
      <KpiCard
        label="Пропускная способность"
        :value="t ? `${formatNumber(t.throughputPerDay)} / день` : '—'"
        :icon="Truck"
        tone="info"
        :hint="t ? `${formatNumber(t.orders)} заказов за период` : undefined"
        :loading="isPending"
      />
      <KpiCard
        label="Время обработки заказа"
        :value="t ? `${t.avgProcessingHours} ч` : '—'"
        :icon="Clock"
        tone="progress"
        hint="от поступления до отгрузки"
        :loading="isPending"
      />
      <KpiCard
        label="Длина маршрута отбора"
        :value="t ? `${t.avgRouteOptimized} м` : '—'"
        :icon="Route"
        tone="success"
        :hint="t ? `базовый ${t.avgRouteBaseline} м · −${t.routeReductionPct}%` : undefined"
        :loading="isPending"
      />
      <KpiCard
        label="Загрузка персонала"
        :value="t ? `${t.utilization}%` : '—'"
        :icon="Gauge"
        tone="neutral"
        :loading="isPending"
      />
      <KpiCard
        label="Доля ошибок"
        :value="t ? `${t.errorRate}%` : '—'"
        :icon="ShieldAlert"
        :tone="(t?.errorRate ?? 0) > 1 ? 'warn' : 'success'"
        hint="пересорт, недостача, брак"
        :loading="isPending"
      />
      <KpiCard
        label="Эффект оптимизации"
        :value="t ? `−${t.routeReductionPct}%` : '—'"
        :icon="Activity"
        tone="success"
        hint="«до» и «после» внедрения маршрутизации"
        :loading="isPending"
      />
    </div>

    <div class="grid grid-cols-1 gap-5 xl:grid-cols-2">
      <SectionCard
        title="Маршруты: до и после"
        description="Средняя длина маршрута отбора в день; алгоритм включён с середины периода"
      >
        <EmptyState v-if="!isPending && !data?.series?.length" compact title="Данных пока нет" />
        <BaseChart v-else :option="routeOption" height="260px" />
      </SectionCard>
      <SectionCard title="Объём и скорость обработки">
        <EmptyState v-if="!isPending && !data?.series?.length" compact title="Данных пока нет" />
        <BaseChart v-else :option="ordersOption" height="260px" />
      </SectionCard>
    </div>

    <SectionCard title="По клиентам" flush>
      <div class="overflow-x-auto">
        <table class="w-full min-w-[28rem] text-sm">
          <thead class="text-left text-xs text-secondary uppercase">
            <tr class="border-b border-default">
              <th class="px-5 py-2 font-medium">Клиент</th>
              <th class="px-3 py-2 text-right font-medium">Заказы</th>
              <th class="px-3 py-2 text-right font-medium">Обработка, ч</th>
              <th class="px-5 py-2 text-right font-medium">Ошибки</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="s in data?.bySeller ?? []"
              :key="s.sellerId"
              class="border-b border-default last:border-0"
            >
              <td class="px-5 py-2 font-medium text-heading">{{ s.name }}</td>
              <td class="tabular px-3 py-2 text-right">{{ formatNumber(s.orders) }}</td>
              <td class="tabular px-3 py-2 text-right">{{ s.avgProcessingHours }}</td>
              <td class="tabular px-5 py-2 text-right">{{ s.errorRate }}%</td>
            </tr>
          </tbody>
        </table>
      </div>
    </SectionCard>
  </div>
</template>
