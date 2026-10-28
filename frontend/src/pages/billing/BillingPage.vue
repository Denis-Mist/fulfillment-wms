<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { CircleDollarSign, FileText, ReceiptText, Wallet } from '@lucide/vue'
import dayjs from 'dayjs'
import { useConfirm } from 'primevue/useconfirm'
import { billingApi } from '@/modules/billing/api'
import { ACT_STATUSES, BILLING_SERVICES } from '@/modules/billing/labels'
import type { ActItem, BillingService } from '@/modules/billing/types'
import { useCabinet } from '@/shared/composables/useCabinet'
import { useChartTheme } from '@/shared/composables/useChartTheme'
import { useNotify } from '@/shared/composables/useNotify'
import { useRegistry } from '@/shared/composables/useRegistry'
import { formatCompactMoney, formatDate, formatMoney, formatNumber } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import BaseChart from '@/shared/ui/BaseChart.vue'
import type { ChartOption } from '@/shared/ui/chart'
import EmptyState from '@/shared/ui/EmptyState.vue'
import KpiCard from '@/shared/ui/KpiCard.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import RegistryTable from '@/shared/ui/RegistryTable.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'
import ToneBadge from '@/pages/stock/ToneBadge.vue'
import TariffCard from './TariffCard.vue'

const session = useSessionStore()
const { isFulfillment } = useCabinet()
const notify = useNotify()
const confirm = useConfirm()
const qc = useQueryClient()
const chartTheme = useChartTheme()

const tab = ref('charges')
const contractId = ref<string | undefined>(undefined)
const { data: contracts } = useQuery({
  queryKey: computed(() => ['billing-contracts', session.organizationId]),
  queryFn: () => billingApi.contracts(),
})
const contractOptions = computed(() =>
  (contracts.value ?? []).map((c) => ({
    value: c.contractId,
    label: `${c.counterpartyName} · ${c.contractNumber}`,
  })),
)
watch(contracts, (list) => {
  if (!isFulfillment.value && list?.length === 1) contractId.value = list[0]!.contractId
})

const { data: summary, isPending: summaryPending } = useQuery({
  queryKey: computed(() => ['billing-summary', session.organizationId, contractId.value]),
  queryFn: () => billingApi.summary(contractId.value),
  placeholderData: keepPreviousData,
})

const registry = useRegistry({
  filters: { service: undefined as BillingService | undefined },
  sortField: 'date',
})
const chargeQuery = computed(() => ({ ...registry.query.value, contractId: contractId.value }))
const { data: charges, isPending: chargesPending } = useQuery({
  queryKey: computed(() => ['billing-charges', session.organizationId, chargeQuery.value]),
  queryFn: () => billingApi.charges(chargeQuery.value),
  placeholderData: keepPreviousData,
})
const serviceOptions = (Object.keys(BILLING_SERVICES) as BillingService[]).map((s) => ({
  value: s,
  label: BILLING_SERVICES[s].label,
}))

const { data: acts, isPending: actsPending } = useQuery({
  queryKey: computed(() => ['billing-acts', session.organizationId, contractId.value]),
  queryFn: () => billingApi.acts(contractId.value),
})

const canFinance = computed(
  () =>
    isFulfillment.value &&
    !!session.role &&
    ['fc_owner', 'fc_manager', 'fc_account_manager'].includes(session.role),
)
const canEditTariff = computed(
  () => isFulfillment.value && !!session.role && ['fc_owner', 'fc_manager'].includes(session.role),
)

const refresh = () => {
  for (const key of ['billing-acts', 'billing-summary']) void qc.invalidateQueries({ queryKey: [key] })
}
const issue = useMutation({
  mutationFn: (id: string) => billingApi.issueAct(id),
  onSuccess: (a) => {
    notify.success('Акт выставлен', `${a.number} на ${formatMoney(a.amount)}`)
    refresh()
  },
  onError: (e: unknown) => notify.error(e),
})
const pay = useMutation({
  mutationFn: (id: string) => billingApi.payAct(id),
  onSuccess: (a) => {
    notify.success('Оплата отмечена', a.number)
    refresh()
  },
  onError: (e: unknown) => notify.error(e),
})
function askIssue(a: ActItem) {
  confirm.require({
    header: 'Выставить акт?',
    message: `${a.counterpartyName}: ${formatMoney(a.amount)} за ${periodLabel(a.period)}. Начисления периода будут зафиксированы.`,
    acceptProps: { label: 'Выставить' },
    rejectProps: { label: 'Отмена', severity: 'secondary', outlined: true },
    accept: () => issue.mutate(a.id),
  })
}
const periodLabel = (p: string) => dayjs(`${p}-01`).format('MMMM YYYY')
const overdue = (a: ActItem) => a.status === 'issued' && !!a.dueAt && Date.parse(a.dueAt) < Date.now()

const chartOption = computed<ChartOption>(() => {
  const theme = chartTheme.value
  const daily = summary.value?.daily ?? []
  return {
    grid: { left: 4, right: 4, top: 36, bottom: 4, containLabel: true },
    tooltip: { trigger: 'axis', valueFormatter: (v) => formatMoney(Number(v)) },
    legend: { top: 0, textStyle: { color: theme.muted }, itemWidth: 10, itemHeight: 10, type: 'scroll' },
    xAxis: {
      type: 'category',
      data: daily.map((d) => dayjs(d.date).format('D MMM')),
      axisTick: { show: false },
      axisLine: { lineStyle: { color: theme.grid } },
      axisLabel: { color: theme.muted, hideOverlap: true },
    },
    yAxis: {
      type: 'value',
      splitLine: { lineStyle: { color: theme.grid, type: 'dashed' } },
      axisLabel: { color: theme.muted, formatter: (v: number) => formatCompactMoney(v) },
    },
    series: (Object.keys(BILLING_SERVICES) as BillingService[]).map((s) => ({
      name: BILLING_SERVICES[s].label,
      type: 'bar',
      stack: 'total',
      barMaxWidth: 16,
      itemStyle: { color: BILLING_SERVICES[s].color },
      data: daily.map((d) => d.amounts[s] ?? 0),
    })),
  }
})
const monthTotal = computed(() => summary.value?.byService.reduce((s, x) => s + x.amount, 0) || 1)
const tariffContracts = computed(() =>
  (contracts.value ?? []).filter((c) => !contractId.value || c.contractId === contractId.value),
)
</script>

<template>
  <div>
    <PageHeader
      title="Биллинг"
      :description="
        isFulfillment
          ? 'Тарифы клиентов, автоматические начисления по операциям склада и акты'
          : 'Начисления фулфилмента за услуги, акты и задолженность'
      "
    >
      <template #actions>
        <Select
          v-model="contractId"
          :options="contractOptions"
          option-label="label"
          option-value="value"
          :placeholder="isFulfillment ? 'Все клиенты' : 'Все фулфилменты'"
          :show-clear="isFulfillment || (contracts?.length ?? 0) > 1"
          filter
          class="w-full sm:w-72"
        />
      </template>
    </PageHeader>

    <div class="mb-5 grid grid-cols-2 gap-3 lg:grid-cols-4">
      <KpiCard
        label="Начислено за месяц"
        :icon="CircleDollarSign"
        :value="summary ? formatCompactMoney(summary.monthAccrued) : undefined"
        :hint="summary ? `прошлый месяц ${formatCompactMoney(summary.prevMonthAccrued)}` : undefined"
        :loading="summaryPending"
      />
      <KpiCard
        label="К оплате по актам"
        :icon="ReceiptText"
        :tone="summary?.unpaid ? 'warn' : 'success'"
        :value="summary ? formatCompactMoney(summary.unpaid) : undefined"
        :loading="summaryPending"
      />
      <KpiCard
        :label="isFulfillment ? 'Сальдо клиентов' : 'Баланс'"
        :icon="Wallet"
        :tone="(summary?.balance ?? 0) < 0 ? 'danger' : 'success'"
        :value="summary ? formatCompactMoney(summary.balance) : undefined"
        hint="с учётом текущего месяца"
        :loading="summaryPending"
      />
      <KpiCard
        label="Актов к выставлению"
        :icon="FileText"
        :value="acts ? formatNumber(acts.filter((a) => a.status === 'draft').length) : undefined"
        :loading="actsPending"
      />
    </div>

    <Tabs v-model:value="tab">
      <TabList>
        <Tab value="charges">Начисления</Tab>
        <Tab value="acts">Акты</Tab>
        <Tab value="tariffs">Тарифы</Tab>
      </TabList>
      <TabPanels class="!px-0">
        <TabPanel value="charges">
          <div class="mb-4 grid gap-4 xl:grid-cols-[2fr_1fr]">
            <SectionCard title="Начисления за 30 дней" description="По видам услуг, ₽">
              <Skeleton v-if="summaryPending" height="240px" />
              <EmptyState v-else-if="!summary?.daily?.length" compact title="Данных пока нет" />
              <BaseChart v-else :option="chartOption" height="240px" />
            </SectionCard>
            <SectionCard title="Структура текущего месяца">
              <ul class="space-y-3">
                <li v-for="s in summary?.byService ?? []" :key="s.service">
                  <div class="mb-1 flex justify-between gap-2 text-sm">
                    <span class="text-heading">{{ BILLING_SERVICES[s.service].label }}</span>
                    <span class="tabular text-secondary">{{ formatMoney(s.amount) }}</span>
                  </div>
                  <div class="h-1.5 overflow-hidden rounded-full bg-surface-100 dark:bg-surface-800">
                    <div
                      class="h-full rounded-full"
                      :style="{
                        width: `${(s.amount / monthTotal) * 100}%`,
                        background: BILLING_SERVICES[s.service].color,
                      }"
                    />
                  </div>
                </li>
              </ul>
            </SectionCard>
          </div>
          <div class="mb-3 flex flex-wrap gap-2.5">
            <Select
              :model-value="registry.filters.service"
              :options="serviceOptions"
              option-label="label"
              option-value="value"
              placeholder="Все услуги"
              show-clear
              class="w-full sm:w-56"
              @update:model-value="registry.setFilter('service', $event ?? undefined)"
            />
          </div>
          <RegistryTable
            :items="charges?.items ?? []"
            :total="charges?.total ?? 0"
            :loading="chargesPending"
            :first="registry.first.value"
            :rows="registry.state.pageSize"
            :sort-field="registry.state.sortField"
            :sort-order="registry.tableSortOrder.value"
            empty-title="Начислений нет"
            @page="registry.onPage"
            @sort="registry.onSort"
          >
            <Column field="date" header="Дата" sortable class="min-w-28">
              <template #body="{ data: x }"
                ><span class="whitespace-nowrap">{{ formatDate(x.date) }}</span></template
              >
            </Column>
            <Column header="Услуга" class="min-w-36">
              <template #body="{ data: x }">
                <span class="inline-flex items-center gap-2 whitespace-nowrap">
                  <span
                    class="size-2 rounded-full"
                    :style="{ background: BILLING_SERVICES[x.service as BillingService].color }"
                  />
                  {{ BILLING_SERVICES[x.service as BillingService].label }}
                </span>
              </template>
            </Column>
            <Column
              v-if="!contractId"
              field="counterpartyName"
              :header="isFulfillment ? 'Клиент' : 'Фулфилмент'"
              class="min-w-40"
            />
            <Column field="quantity" header="Объём" sortable class="min-w-28">
              <template #body="{ data: x }"
                ><span class="whitespace-nowrap tabular"
                  >{{ formatNumber(x.quantity, x.quantity % 1 ? 1 : 0) }} {{ x.unit }}</span
                ></template
              >
            </Column>
            <Column header="Ставка" class="min-w-24">
              <template #body="{ data: x }"
                ><span class="whitespace-nowrap tabular text-secondary">{{
                  formatMoney(x.rate)
                }}</span></template
              >
            </Column>
            <Column field="amount" header="Сумма" sortable class="min-w-28">
              <template #body="{ data: x }"
                ><span class="font-medium whitespace-nowrap tabular text-heading">{{
                  formatMoney(x.amount)
                }}</span></template
              >
            </Column>
          </RegistryTable>
        </TabPanel>

        <TabPanel value="acts">
          <RegistryTable
            :items="acts ?? []"
            :total="acts?.length ?? 0"
            :loading="actsPending"
            :first="0"
            :rows="200"
            empty-title="Актов пока нет"
          >
            <Column header="Акт" class="min-w-44">
              <template #body="{ data: a }">
                <div class="font-medium whitespace-nowrap text-heading">{{ a.number }}</div>
                <div class="text-xs text-secondary capitalize">{{ periodLabel(a.period) }}</div>
              </template>
            </Column>
            <Column
              field="counterpartyName"
              :header="isFulfillment ? 'Клиент' : 'Фулфилмент'"
              class="min-w-40"
            />
            <Column header="Статус" class="min-w-32">
              <template #body="{ data: a }">
                <ToneBadge
                  :tone="overdue(a) ? 'danger' : ACT_STATUSES[a.status as keyof typeof ACT_STATUSES].tone"
                  :label="
                    overdue(a) ? 'Просрочен' : ACT_STATUSES[a.status as keyof typeof ACT_STATUSES].label
                  "
                />
              </template>
            </Column>
            <Column header="Сумма" class="min-w-28">
              <template #body="{ data: a }"
                ><span class="font-medium whitespace-nowrap tabular text-heading">{{
                  formatMoney(a.amount)
                }}</span></template
              >
            </Column>
            <Column header="Срок / оплата" class="min-w-32">
              <template #body="{ data: a }">
                <span class="text-sm whitespace-nowrap text-secondary">
                  {{
                    a.paidAt
                      ? `оплачен ${formatDate(a.paidAt)}`
                      : a.dueAt
                        ? `до ${formatDate(a.dueAt)}`
                        : 'идёт период'
                  }}
                </span>
              </template>
            </Column>
            <Column v-if="canFinance" class="min-w-36">
              <template #body="{ data: a }">
                <Button
                  v-if="a.status === 'draft'"
                  label="Выставить"
                  size="small"
                  outlined
                  @click="askIssue(a)"
                />
                <Button
                  v-else-if="a.status === 'issued'"
                  label="Отметить оплату"
                  size="small"
                  text
                  :loading="pay.isPending.value && pay.variables.value === a.id"
                  @click="pay.mutate(a.id)"
                />
              </template>
            </Column>
          </RegistryTable>
        </TabPanel>

        <TabPanel value="tariffs">
          <EmptyState
            v-if="!tariffContracts.length"
            :icon="FileText"
            title="Нет договоров"
            description="Тарифы появятся после заключения договора"
          />
          <div v-else class="grid gap-4 lg:grid-cols-2">
            <TariffCard
              v-for="c in tariffContracts.slice(0, 6)"
              :key="c.contractId"
              :contract-id="c.contractId"
              :title="`${c.counterpartyName} · ${c.contractNumber}`"
              :editable="canEditTariff"
            />
          </div>
          <p v-if="tariffContracts.length > 6" class="mt-3 text-sm text-secondary">
            Показаны 6 договоров — выберите клиента вверху, чтобы открыть его тариф.
          </p>
        </TabPanel>
      </TabPanels>
    </Tabs>
  </div>
</template>
