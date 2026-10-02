<script setup lang="ts">
import { computed, ref } from 'vue'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import dayjs from 'dayjs'
import { Boxes, CircleCheck, MapPin, Receipt, ShoppingCart, Wallet, Warehouse } from '@lucide/vue'
import { dashboardApi } from '@/modules/dashboard/api'
import type { DashboardPeriod } from '@/modules/dashboard/types'
import { SALES_CHANNELS } from '@/shared/config/statuses'
import { useCabinet } from '@/shared/composables/useCabinet'
import { useChartTheme } from '@/shared/composables/useChartTheme'
import { formatCompactMoney, formatNumber, formatPercent, formatShortDate, greeting, pluralize } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import AttentionList from '@/shared/ui/AttentionList.vue'
import BaseChart from '@/shared/ui/BaseChart.vue'
import type { ChartOption } from '@/shared/ui/chart'
import EmptyState from '@/shared/ui/EmptyState.vue'
import KpiCard from '@/shared/ui/KpiCard.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'
import StatusTag from '@/shared/ui/StatusTag.vue'

const session = useSessionStore()
const { to } = useCabinet()
const chartTheme = useChartTheme()

const period = ref<DashboardPeriod>('30d')
const periodOptions = [
  { value: '7d', label: '7 дней' },
  { value: '30d', label: '30 дней' },
  { value: '90d', label: '90 дней' },
]
const periodLabel = computed(() => periodOptions.find((o) => o.value === period.value)?.label ?? '')

const { data, isPending } = useQuery({
  queryKey: computed(() => ['dashboard', 'seller', session.organizationId, period.value]),
  queryFn: () => dashboardApi.seller({ period: period.value }),
  placeholderData: keepPreviousData,
})

const kpis = computed(() => data.value?.kpis)
const firstName = computed(() => session.user?.fullName.split(' ')[0] ?? '')

const trendOption = computed<ChartOption>(() => {
  const trend = data.value?.trend ?? []
  const theme = chartTheme.value
  const series = (['wb', 'ozon', 'own'] as const).map((channel, index, all) => ({
    name: SALES_CHANNELS[channel].label,
    type: 'bar' as const,
    stack: 'orders',
    barMaxWidth: 18,
    itemStyle: {
      color: SALES_CHANNELS[channel].color,
      borderRadius: index === all.length - 1 ? [4, 4, 0, 0] : 0,
    },
    data: trend.map((p) => p[channel]),
  }))
  return {
    grid: { left: 8, right: 8, top: 36, bottom: 4, containLabel: true },
    legend: { top: 0, right: 0, icon: 'roundRect', itemWidth: 10, itemHeight: 10, textStyle: { color: theme.text } },
    tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
    xAxis: {
      type: 'category',
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
    series,
  }
})

function daysLeftClass(days: number) {
  if (days < 7) return 'bg-rose-50 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300'
  if (days < 14) return 'bg-amber-50 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300'
  return 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300'
}
</script>

<template>
  <div class="space-y-6">
    <PageHeader
      :title="`${greeting()}, ${firstName}`"
      :description="`${session.activeMembership?.organizationName} · ваши товары, заказы и поставки`"
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

    <div class="grid grid-cols-2 gap-4 md:grid-cols-3 xl:grid-cols-5">
      <KpiCard
        label="На складах"
        :value="kpis && `${formatNumber(kpis.stockUnits.value)} шт`"
        hint="во всех фулфилмент-центрах"
        :icon="Boxes"
        :loading="isPending"
      />
      <KpiCard
        label="Стоимость остатков"
        :value="kpis && formatCompactMoney(kpis.stockValue.value)"
        hint="по розничной цене"
        :icon="Wallet"
        tone="info"
        :loading="isPending"
      />
      <KpiCard
        :label="`Заказы за ${periodLabel}`"
        :value="kpis && formatNumber(kpis.orders30d.value)"
        :current="kpis?.orders30d.value"
        :previous="kpis?.orders30d.previous"
        delta-label="к прошлому периоду"
        :icon="ShoppingCart"
        :loading="isPending"
        :to="to('orders')"
      />
      <KpiCard
        label="Отгружено вовремя"
        :value="kpis && formatPercent(kpis.onTimeRate.value, 1)"
        :current="kpis?.onTimeRate.value"
        :previous="kpis?.onTimeRate.previous"
        delta-label="к прошлому периоду"
        :icon="CircleCheck"
        tone="success"
        :loading="isPending"
      />
      <KpiCard
        label="Начислено за месяц"
        :value="kpis && formatCompactMoney(kpis.chargesMonth.value)"
        hint="хранение и операции ФЦ"
        :icon="Receipt"
        tone="warn"
        :loading="isPending"
      />
    </div>

    <div class="grid gap-6 xl:grid-cols-3">
      <SectionCard title="Требует внимания" description="Что стоит проверить сегодня">
        <AttentionList :items="data?.attention ?? []" :loading="isPending" />
      </SectionCard>
      <SectionCard class="xl:col-span-2" title="Заказы по каналам" description="Сколько заказов пришло с каждого маркетплейса">
        <Skeleton v-if="isPending" height="280px" border-radius="12px" />
        <BaseChart v-else :option="trendOption" />
      </SectionCard>
    </div>

    <div class="grid gap-6 lg:grid-cols-2 xl:grid-cols-3">
      <SectionCard title="Остатки по фулфилмент-центрам" description="Где хранится ваш товар">
        <div v-if="isPending" class="space-y-3">
          <Skeleton v-for="i in 2" :key="i" height="5rem" border-radius="12px" />
        </div>
        <ul v-else class="space-y-3">
          <li
            v-for="fc in data?.fulfillments"
            :key="fc.id"
            class="rounded-xl border border-surface-200/80 p-4 dark:border-surface-800"
          >
            <div class="flex items-start justify-between gap-3">
              <div class="min-w-0">
                <div class="truncate font-semibold text-heading">{{ fc.name }}</div>
                <div class="mt-0.5 flex items-center gap-3 text-xs text-secondary">
                  <span class="inline-flex items-center gap-1"><MapPin class="size-3.5" />{{ fc.city }}</span>
                  <span class="inline-flex items-center gap-1">
                    <Warehouse class="size-3.5" />{{ fc.warehouses }} {{ pluralize(fc.warehouses, ['склад', 'склада', 'складов']) }}
                  </span>
                </div>
              </div>
              <div class="shrink-0 text-right">
                <div class="tabular font-semibold whitespace-nowrap text-heading">{{ formatNumber(fc.units) }} шт</div>
                <div class="text-xs text-secondary">{{ fc.skus }} SKU</div>
              </div>
            </div>
          </li>
        </ul>
      </SectionCard>

      <SectionCard title="Скоро закончатся" description="Прогноз по средним продажам за 30 дней">
        <div v-if="isPending" class="space-y-3">
          <Skeleton v-for="i in 5" :key="i" height="2.5rem" />
        </div>
        <ul v-else-if="data?.lowStock.length" class="divide-y divide-surface-100 dark:divide-surface-800">
          <li v-for="item in data.lowStock" :key="item.productId" class="flex items-center gap-3 py-2.5 first:pt-0 last:pb-0">
            <div class="min-w-0 flex-1">
              <div class="truncate text-sm font-medium text-heading">{{ item.name }}</div>
              <div class="text-xs text-secondary">
                {{ item.sku }} · осталось {{ formatNumber(item.available) }} шт · ~{{ formatNumber(item.dailySales, 1) }}/день
              </div>
            </div>
            <span class="tabular shrink-0 rounded-md px-2 py-0.5 text-xs font-semibold" :class="daysLeftClass(item.daysLeft)">
              {{ item.daysLeft }} дн.
            </span>
          </li>
        </ul>
        <EmptyState v-else title="Запасов достаточно" compact />
      </SectionCard>

      <SectionCard title="Последние поставки" class="lg:col-span-2 xl:col-span-1">
        <template #actions>
          <RouterLink :to="to('inbound')" class="text-sm font-medium text-primary hover:underline">Все поставки</RouterLink>
        </template>
        <div v-if="isPending" class="space-y-3">
          <Skeleton v-for="i in 5" :key="i" height="2.5rem" />
        </div>
        <ul v-else class="divide-y divide-surface-100 dark:divide-surface-800">
          <li v-for="item in data?.recentInbound" :key="item.id">
            <RouterLink
              :to="to('inbound-detail', { id: item.id })"
              class="-mx-2 flex items-center gap-3 rounded-lg px-2 py-2.5 transition-colors hover:bg-surface-50 dark:hover:bg-surface-800/60"
            >
              <div class="min-w-0 flex-1">
                <div class="text-sm font-medium text-heading">{{ item.number }}</div>
                <div class="truncate text-xs text-secondary">
                  {{ item.warehouseName }} · {{ formatShortDate(item.expectedAt) }} · {{ formatNumber(item.expectedUnits) }} шт
                </div>
              </div>
              <StatusTag domain="inbound" :status="item.status" />
            </RouterLink>
          </li>
        </ul>
      </SectionCard>
    </div>
  </div>
</template>
