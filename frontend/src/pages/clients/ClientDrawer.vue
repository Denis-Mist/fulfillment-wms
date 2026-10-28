<script setup lang="ts">
import EmptyState from '@/shared/ui/EmptyState.vue'
import { computed } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import dayjs from 'dayjs'
import { catalogApi } from '@/modules/catalog/api'
import { useChartTheme } from '@/shared/composables/useChartTheme'
import { formatDate, formatMoney, formatNumber } from '@/shared/lib/format'
import BaseChart from '@/shared/ui/BaseChart.vue'
import type { ChartOption } from '@/shared/ui/chart'
import InfoList from '@/shared/ui/InfoList.vue'

const props = defineProps<{ clientId: string | null }>()
const visible = defineModel<boolean>('visible', { required: true })
const chartTheme = useChartTheme()

const { data, isPending } = useQuery({
  queryKey: computed(() => ['fc-client', props.clientId]),
  queryFn: () => catalogApi.client(props.clientId!),
  enabled: computed(() => !!props.clientId && visible.value),
})
const c = computed(() => (data.value?.id === props.clientId ? data.value : undefined))

const contract = computed(() =>
  c.value
    ? [
        { label: 'Договор', value: `${c.value.contractNumber} от ${formatDate(c.value.startedAt)}` },
        { label: 'Склады', value: c.value.warehouses.map((w) => w.name).join(', ') || '—' },
        { label: 'Хранение', value: `${formatMoney(c.value.tariff.storagePerLiterDay)} / л в сутки` },
        { label: 'Сборка заказа', value: formatMoney(c.value.tariff.pickPerOrder) },
        { label: 'Приёмка', value: `${formatMoney(c.value.tariff.receivingPerUnit)} / шт` },
      ]
    : [],
)
const contact = computed(() =>
  c.value
    ? [
        { label: 'ИНН', value: c.value.inn },
        { label: 'Город', value: c.value.city },
        { label: 'Владелец', value: c.value.ownerName },
        { label: 'Email', value: c.value.ownerEmail },
        { label: 'Телефон', value: c.value.ownerPhone },
      ]
    : [],
)

const option = computed<ChartOption>(() => {
  const theme = chartTheme.value
  const trend = c.value?.ordersTrend ?? []
  return {
    grid: { left: 4, right: 4, top: 10, bottom: 4, containLabel: true },
    tooltip: { trigger: 'axis' },
    xAxis: {
      type: 'category',
      data: trend.map((p) => dayjs(p.date).format('D MMM')),
      axisTick: { show: false },
      axisLine: { lineStyle: { color: theme.grid } },
      axisLabel: { color: theme.muted, hideOverlap: true },
    },
    yAxis: {
      type: 'value',
      splitLine: { lineStyle: { color: theme.grid, type: 'dashed' } },
      axisLabel: { color: theme.muted },
    },
    series: [
      {
        name: 'Заказы',
        type: 'bar',
        barMaxWidth: 18,
        itemStyle: { color: '#6366f1', borderRadius: [4, 4, 0, 0] },
        data: trend.map((p) => p.count),
      },
    ],
  }
})
</script>

<template>
  <Drawer
    v-model:visible="visible"
    position="right"
    class="!w-full sm:!w-[32rem]"
    :header="c?.name ?? 'Клиент'"
  >
    <div v-if="isPending && !c" class="space-y-3"><Skeleton v-for="i in 6" :key="i" height="2rem" /></div>
    <div v-else-if="c" class="space-y-5">
      <div class="grid grid-cols-2 gap-2 sm:grid-cols-4">
        <div
          v-for="kpi in [
            { label: 'SKU', value: c.skuCount },
            { label: 'На складе, шт', value: c.stockUnits },
            { label: 'Заказов / 30 дн', value: c.orders30d },
            { label: 'Поставок в работе', value: c.inboundActive },
          ]"
          :key="kpi.label"
          class="rounded-lg border border-default p-2.5"
        >
          <div class="tabular text-lg font-semibold text-heading">{{ formatNumber(kpi.value) }}</div>
          <div class="text-xs text-secondary">{{ kpi.label }}</div>
        </div>
      </div>
      <div>
        <h3 class="mb-1 text-sm font-semibold text-heading">Заказы за 14 дней</h3>
        <EmptyState v-if="!c?.ordersTrend?.length" compact title="Данных пока нет" />
        <BaseChart v-else :option="option" height="160px" />
      </div>
      <div>
        <h3 class="mb-2 text-sm font-semibold text-heading">Договор и тарифы</h3>
        <InfoList :items="contract" />
      </div>
      <div>
        <h3 class="mb-2 text-sm font-semibold text-heading">Реквизиты и контакт</h3>
        <InfoList :items="contact" />
      </div>
    </div>
  </Drawer>
</template>
