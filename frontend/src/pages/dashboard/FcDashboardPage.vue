<script setup lang="ts">
import { computed, ref } from 'vue'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import dayjs from 'dayjs'
import { ChevronRight, Gauge, ListChecks, PackagePlus, ShoppingCart, TriangleAlert, Truck } from '@lucide/vue'
import { dashboardApi } from '@/modules/dashboard/api'
import type { DashboardPeriod } from '@/modules/dashboard/types'
import { warehousesApi } from '@/modules/warehouses/api'
import { ORDER_STATUSES, SALES_CHANNELS } from '@/shared/config/statuses'
import { useCabinet } from '@/shared/composables/useCabinet'
import { useChartTheme } from '@/shared/composables/useChartTheme'
import { formatDate, formatNumber, formatPercent, greeting } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import AttentionList from '@/shared/ui/AttentionList.vue'
import BaseChart from '@/shared/ui/BaseChart.vue'
import type { ChartOption } from '@/shared/ui/chart'
import EmptyState from '@/shared/ui/EmptyState.vue'
import KpiCard from '@/shared/ui/KpiCard.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import ProgressMeter from '@/shared/ui/ProgressMeter.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'

const session = useSessionStore()
const { to } = useCabinet()
const chartTheme = useChartTheme()

const period = ref<DashboardPeriod>('30d')
const periodOptions = [
  { value: '7d', label: '7 дней' },
  { value: '30d', label: '30 дней' },
  { value: '90d', label: '90 дней' },
]

const { data, isPending } = useQuery({
  queryKey: computed(() => ['dashboard', 'fc', session.organizationId, session.warehouseId, period.value]),
  queryFn: () => dashboardApi.fulfillment({ warehouseId: session.warehouseId || undefined, period: period.value }),
  placeholderData: keepPreviousData,
})

const { data: warehouses } = useQuery({
  queryKey: computed(() => ['warehouses', session.organizationId]),
  queryFn: () => warehousesApi.list(),
  staleTime: 5 * 60_000,
})

const firstName = computed(() => session.user?.fullName.split(' ')[0] ?? '')
const scopeLabel = computed(() => {
  const warehouse = warehouses.value?.find((w) => w.id === session.warehouseId)
  return warehouse ? `склад ${warehouse.name}` : 'все склады'
})

const kpis = computed(() => data.value?.kpis)

const pipelineTotal = computed(() => data.value?.pipeline.reduce((sum, stage) => sum + stage.count, 0) ?? 0)

const trendOption = computed<ChartOption>(() => {
  const trend = data.value?.trend ?? []
  const theme = chartTheme.value
  const area = (color: string) => ({
    type: 'linear' as const,
    x: 0,
    y: 0,
    x2: 0,
    y2: 1,
    colorStops: [
      { offset: 0, color: `${color}40` },
      { offset: 1, color: `${color}00` },
    ],
  })
  return {
    grid: { left: 8, right: 8, top: 36, bottom: 4, containLabel: true },
    legend: { top: 0, right: 0, icon: 'roundRect', itemWidth: 10, itemHeight: 10, textStyle: { color: theme.text } },
    tooltip: { trigger: 'axis' },
    xAxis: {
      type: 'category',
      boundaryGap: false,
      data: trend.map((p) => dayjs(p.date).format('D MMM')),
      axisLine: { lineStyle: { color: theme.grid } },
      axisTick: { show: false },
      axisLabel: { color: theme.muted, hideOverlap: true },
    },
    yAxis: {
      type: 'value',
      splitLine: { lineStyle: { color: theme.grid, type: 'dashed' } },
      axisLabel: { color: theme.muted },
    },
    series: [
      {
        name: 'Создано',
        type: 'line',
        smooth: true,
        symbol: 'none',
        lineStyle: { width: 2.5 },
        areaStyle: { color: area('#6366f1') },
        data: trend.map((p) => p.created),
      },
      {
        name: 'Отгружено',
        type: 'line',
        smooth: true,
        symbol: 'none',
        lineStyle: { width: 2.5 },
        areaStyle: { color: area('#22c55e') },
        data: trend.map((p) => p.shipped),
      },
    ],
  }
})

const channelsTotal = computed(() => data.value?.channels.reduce((sum, c) => sum + c.count, 0) ?? 0)

const channelsOption = computed<ChartOption>(() => ({
  tooltip: { trigger: 'item', formatter: '{b}: {c} ({d}%)' },
  series: [
    {
      type: 'pie',
      radius: ['64%', '88%'],
      padAngle: 2,
      itemStyle: { borderRadius: 6 },
      label: { show: false },
      data: (data.value?.channels ?? []).map((c) => ({
        name: SALES_CHANNELS[c.channel].label,
        value: c.count,
        itemStyle: { color: SALES_CHANNELS[c.channel].color },
      })),
    },
  ],
}))

const maxClientOrders = computed(() => Math.max(1, ...(data.value?.topClients.map((c) => c.orders) ?? [1])))
</script>

<template>
  <div class="space-y-6">
    <PageHeader
      :title="`${greeting()}, ${firstName}`"
      :description="`Сводка: ${scopeLabel} · ${formatDate(new Date().toISOString())}`"
    >
      <template #actions>
        <SelectButton
          v-model="period"
          :options="periodOptions"
          option-label="label"
          option-value="value"
          :allow-empty="false"
        />
      </template>
    </PageHeader>

    <div class="grid grid-cols-2 gap-4 md:grid-cols-3 2xl:grid-cols-6">
      <KpiCard
        label="Заказов сегодня"
        :value="kpis && formatNumber(kpis.ordersToday.value)"
        :current="kpis?.ordersToday.value"
        :previous="kpis?.ordersToday.previous"
        :icon="ShoppingCart"
        :loading="isPending"
        :to="to('orders')"
      />
      <KpiCard
        label="Отгружено сегодня"
        :value="kpis && formatNumber(kpis.shippedToday.value)"
        :current="kpis?.shippedToday.value"
        :previous="kpis?.shippedToday.previous"
        :icon="Truck"
        tone="success"
        :loading="isPending"
        :to="to('orders', undefined, { status: 'shipped' })"
      />
      <KpiCard
        label="В работе"
        :value="kpis && formatNumber(kpis.inProgress.value)"
        hint="заказов на разных этапах"
        :icon="ListChecks"
        tone="info"
        :loading="isPending"
      />
      <KpiCard
        label="Просрочено"
        :value="kpis && formatNumber(kpis.overdue.value)"
        :hint="kpis?.overdue.value ? 'нужно отгрузить срочно' : 'всё в срок'"
        :icon="TriangleAlert"
        :tone="kpis?.overdue.value ? 'danger' : 'success'"
        :loading="isPending"
        :to="to('orders', undefined, { status: 'overdue' })"
      />
      <KpiCard
        label="Поставок сегодня"
        :value="kpis && formatNumber(kpis.inboundToday.value)"
        hint="ожидается на складе"
        :icon="PackagePlus"
        tone="warn"
        :loading="isPending"
        :to="to('inbound')"
      />
      <KpiCard
        label="Заполненность"
        :value="kpis && formatPercent(kpis.occupancy.value)"
        hint="ячеек занято товаром"
        :icon="Gauge"
        tone="neutral"
        :loading="isPending"
      />
    </div>

    <SectionCard title="Заказы в работе" description="Где сейчас находятся заказы — нажмите на этап, чтобы открыть список">
      <div class="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
        <template v-if="isPending">
          <Skeleton v-for="i in 5" :key="i" height="5.5rem" border-radius="14px" />
        </template>
        <RouterLink
          v-for="(stage, index) in data?.pipeline"
          v-else
          :key="stage.status"
          :to="to('orders', undefined, { status: stage.status })"
          class="group relative rounded-xl border border-surface-200/80 p-4 transition-all hover:border-primary-300 hover:shadow-md hover:shadow-primary-500/5 dark:border-surface-800 dark:hover:border-primary-500/40"
        >
          <div class="flex items-center justify-between text-xs font-medium text-secondary">
            <span>{{ index + 1 }}. {{ ORDER_STATUSES[stage.status].label }}</span>
            <ChevronRight class="size-4 text-surface-300 transition-transform group-hover:translate-x-0.5 group-hover:text-primary" />
          </div>
          <div class="tabular mt-2 text-2xl font-semibold text-heading">{{ formatNumber(stage.count) }}</div>
          <ProgressMeter class="mt-3" :value="stage.count" :max="pipelineTotal" />
        </RouterLink>
      </div>
    </SectionCard>

    <div class="grid gap-6 xl:grid-cols-3">
      <SectionCard title="Требует внимания" description="Задачи, которые стоит решить в первую очередь">
        <AttentionList :items="data?.attention ?? []" :loading="isPending" />
      </SectionCard>
      <SectionCard
        class="xl:col-span-2"
        title="Динамика заказов"
        description="Сколько заказов поступило и сколько отгружено по дням"
      >
        <Skeleton v-if="isPending" height="280px" border-radius="12px" />
        <BaseChart v-else :option="trendOption" />
      </SectionCard>
    </div>

    <div class="grid gap-6 lg:grid-cols-2 xl:grid-cols-3">
      <SectionCard title="Каналы продаж" description="Заказы за выбранный период">
        <Skeleton v-if="isPending" height="220px" border-radius="12px" />
        <div v-else-if="channelsTotal">
          <div class="relative mx-auto w-44">
            <BaseChart :option="channelsOption" height="176px" />
            <div class="pointer-events-none absolute inset-0 grid place-items-center text-center">
              <div>
                <div class="tabular text-xl font-semibold text-heading">{{ formatNumber(channelsTotal) }}</div>
                <div class="text-xs text-secondary">заказов</div>
              </div>
            </div>
          </div>
          <ul class="mt-5 grid grid-cols-3 gap-2">
            <li
              v-for="item in data?.channels"
              :key="item.channel"
              class="rounded-xl bg-surface-50 px-3 py-2.5 dark:bg-surface-800/60"
            >
              <div class="flex items-center gap-1.5 text-xs text-secondary">
                <span class="size-2 shrink-0 rounded-full" :style="{ backgroundColor: SALES_CHANNELS[item.channel].color }" />
                <span class="truncate">{{ SALES_CHANNELS[item.channel].name }}</span>
              </div>
              <div class="tabular mt-0.5 text-base font-semibold text-heading">
                {{ formatPercent((item.count / channelsTotal) * 100) }}
              </div>
            </li>
          </ul>
        </div>
        <EmptyState v-else title="Нет заказов за период" compact />
      </SectionCard>

      <SectionCard title="Загрузка складов" description="Доля занятых ячеек и заказы в работе">
        <div v-if="isPending" class="space-y-4">
          <Skeleton v-for="i in 3" :key="i" height="2.75rem" />
        </div>
        <ul v-else class="space-y-4">
          <li v-for="warehouse in data?.warehouses" :key="warehouse.id">
            <div class="mb-1.5 flex items-baseline justify-between gap-3 text-sm">
              <span class="font-medium text-heading">{{ warehouse.name }}</span>
              <span class="tabular text-secondary">
                {{ formatNumber(warehouse.inProgress) }} в работе ·
                <span class="font-semibold text-heading">{{ formatPercent(warehouse.occupancy) }}</span>
              </span>
            </div>
            <ProgressMeter :value="warehouse.occupancy" tone="auto" />
          </li>
        </ul>
      </SectionCard>

      <SectionCard title="Крупнейшие клиенты" description="По числу заказов за период" class="lg:col-span-2 xl:col-span-1">
        <div v-if="isPending" class="space-y-4">
          <Skeleton v-for="i in 5" :key="i" height="2.25rem" />
        </div>
        <ul v-else class="space-y-3.5">
          <li v-for="(client, index) in data?.topClients" :key="client.id" class="flex items-center gap-3">
            <span
              class="tabular grid size-7 shrink-0 place-items-center rounded-lg bg-surface-100 text-xs font-semibold text-surface-500 dark:bg-surface-800"
            >
              {{ index + 1 }}
            </span>
            <div class="min-w-0 flex-1">
              <div class="flex items-baseline justify-between gap-2 text-sm">
                <span class="truncate font-medium text-heading">{{ client.name }}</span>
                <span class="tabular shrink-0 text-secondary">{{ formatNumber(client.orders) }} зак.</span>
              </div>
              <ProgressMeter class="mt-1.5" :value="client.orders" :max="maxClientOrders" />
            </div>
          </li>
        </ul>
      </SectionCard>
    </div>
  </div>
</template>
