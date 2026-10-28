<script setup lang="ts">
import { computed } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import { Building2, CalendarCheck, PackageCheck, ShieldAlert, ShoppingCart, Truck, Users, Warehouse } from '@lucide/vue'
import { adminApi } from '@/modules/admin/api'
import { AUDIT_CATEGORIES } from '@/modules/admin/labels'
import type { SalesChannel } from '@/modules/orders/types'
import { useChartTheme } from '@/shared/composables/useChartTheme'
import { useCabinet } from '@/shared/composables/useCabinet'
import { SALES_CHANNELS, TONE_CLASSES } from '@/shared/config/statuses'
import { formatDateTime, formatNumber, formatPercent, formatShortDate } from '@/shared/lib/format'
import BaseChart from '@/shared/ui/BaseChart.vue'
import type { ChartOption } from '@/shared/ui/chart'
import KpiCard from '@/shared/ui/KpiCard.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'

const { to } = useCabinet()
const theme = useChartTheme()

const { data, isPending } = useQuery({ queryKey: ['admin-overview'], queryFn: adminApi.overview })

const CHANNELS: SalesChannel[] = ['wb', 'ozon', 'own']
const trendOption = computed<ChartOption>(() => {
  const days = data.value?.ordersByDay ?? []
  return {
    grid: { left: 8, right: 8, top: 36, bottom: 4, containLabel: true },
    legend: { top: 0, left: 0, icon: 'circle', itemWidth: 8, textStyle: { color: theme.value.text } },
    tooltip: { trigger: 'axis' },
    xAxis: {
      type: 'category',
      data: days.map((d) => formatShortDate(d.date)),
      axisLine: { lineStyle: { color: theme.value.grid } },
      axisLabel: { color: theme.value.muted, hideOverlap: true },
    },
    yAxis: { type: 'value', splitLine: { lineStyle: { color: theme.value.grid } }, axisLabel: { color: theme.value.muted } },
    series: CHANNELS.map((channel) => ({
      type: 'bar',
      stack: 'orders',
      name: SALES_CHANNELS[channel].name,
      itemStyle: { color: SALES_CHANNELS[channel].color },
      barMaxWidth: 18,
      data: days.map((d) => d.channels[channel]),
    })),
  }
})
</script>

<template>
  <div>
    <PageHeader title="Обзор платформы" description="Организации, пользователи и операционная активность за последние 30 дней" />

    <div class="mb-6 grid grid-cols-2 gap-3 lg:grid-cols-4">
      <KpiCard
        label="Организаций"
        :icon="Building2"
        :value="formatNumber(data?.organizations.total ?? 0)"
        :hint="data ? `${data.organizations.fulfillment} ФЦ · ${data.organizations.seller} селлеров` : undefined"
        :loading="isPending"
        :to="to('organizations')"
      />
      <KpiCard
        label="На модерации"
        :icon="ShieldAlert"
        :value="formatNumber(data?.organizations.pending ?? 0)"
        :tone="data?.organizations.pending ? 'warn' : 'success'"
        hint="Ожидают проверки"
        :loading="isPending"
        :to="to('moderation')"
      />
      <KpiCard
        label="Пользователей"
        :icon="Users"
        :value="formatNumber(data?.users.total ?? 0)"
        :hint="data?.users.blocked ? `${data.users.blocked} заблокировано` : 'Все активны'"
        tone="info"
        :loading="isPending"
        :to="to('users')"
      />
      <KpiCard label="Складов" :icon="Warehouse" :value="formatNumber(data?.warehouses ?? 0)" tone="neutral" :loading="isPending" />
      <KpiCard
        label="Заказов за 30 дней"
        :icon="ShoppingCart"
        :value="formatNumber(data?.orders30d ?? 0)"
        :current="data?.orders30d"
        :previous="data?.ordersPrev30d"
        delta-label="к прошлым 30 дням"
        :loading="isPending"
      />
      <KpiCard label="Отгружено" :icon="Truck" :value="formatNumber(data?.shipped30d ?? 0)" tone="success" :loading="isPending" />
      <KpiCard
        label="Отгрузка в срок"
        :icon="CalendarCheck"
        :value="formatPercent((data?.onTimeRate ?? 0) * 100, 1)"
        :tone="(data?.onTimeRate ?? 1) >= 0.95 ? 'success' : 'warn'"
        :loading="isPending"
      />
      <KpiCard label="Поставок за 30 дней" :icon="PackageCheck" :value="formatNumber(data?.inbound30d ?? 0)" tone="info" :loading="isPending" />
    </div>

    <div class="grid gap-6 xl:grid-cols-3">
      <SectionCard class="xl:col-span-2" title="Заказы по каналам" description="Все фулфилмент-центры платформы, по дням">
        <Skeleton v-if="isPending" height="280px" />
        <BaseChart v-else :option="trendOption" />
      </SectionCard>

      <SectionCard title="Крупнейшие ФЦ" description="По количеству заказов за 30 дней" flush>
        <div v-if="isPending" class="space-y-2 p-5"><Skeleton v-for="i in 5" :key="i" height="2.5rem" /></div>
        <ul v-else class="divide-y divide-surface-100 dark:divide-surface-800">
          <li v-for="(fc, index) in data?.topFulfillments" :key="fc.id" class="flex items-center gap-3 px-5 py-3">
            <span class="grid size-7 shrink-0 place-items-center rounded-lg bg-surface-100 text-xs font-semibold text-secondary dark:bg-surface-800">
              {{ index + 1 }}
            </span>
            <div class="min-w-0 flex-1">
              <div class="truncate text-sm font-medium text-heading">{{ fc.name }}</div>
              <div class="text-xs text-secondary">{{ fc.warehouses }} скл. · {{ fc.clients }} клиентов</div>
            </div>
            <span class="tabular text-sm font-semibold text-heading">{{ formatNumber(fc.orders30d) }}</span>
          </li>
        </ul>
      </SectionCard>

      <SectionCard class="xl:col-span-3" title="Последние события" description="Из журнала действий" flush>
        <template #actions>
          <Button as="router-link" :to="to('audit')" label="Весь журнал" size="small" text />
        </template>
        <div v-if="isPending" class="space-y-2 p-5"><Skeleton v-for="i in 4" :key="i" height="2.5rem" /></div>
        <ul v-else class="divide-y divide-surface-100 dark:divide-surface-800">
          <li v-for="event in data?.recentEvents" :key="event.id" class="flex flex-wrap items-center gap-x-3 gap-y-1 px-5 py-3">
            <span class="rounded-full px-2 py-0.5 text-xs font-medium" :class="TONE_CLASSES[AUDIT_CATEGORIES[event.category].tone].badge">
              {{ AUDIT_CATEGORIES[event.category].label }}
            </span>
            <span class="min-w-0 flex-1 truncate text-sm text-heading">
              {{ event.action }}<span v-if="event.entity" class="text-secondary"> · {{ event.entity }}</span>
            </span>
            <span class="w-full text-xs text-secondary sm:w-auto">{{ event.actorName }} · {{ formatDateTime(event.at) }}</span>
          </li>
        </ul>
      </SectionCard>
    </div>
  </div>
</template>
