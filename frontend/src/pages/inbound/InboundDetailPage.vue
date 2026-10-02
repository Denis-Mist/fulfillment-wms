<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { useConfirm } from 'primevue/useconfirm'
import { Building2, CalendarClock, PackageX, TriangleAlert, Warehouse } from '@lucide/vue'
import { FC_OFFICE_ROLES } from '@/modules/auth/roles'
import { inboundApi } from '@/modules/inbound/api'
import type { InboundAction, InboundEventType, InboundShipment } from '@/modules/inbound/types'
import { INBOUND_CANCELLABLE, INBOUND_NEXT_ACTION, INBOUND_STEPS } from '@/modules/inbound/workflow'
import { TONE_CLASSES, type Tone } from '@/shared/config/statuses'
import { useCabinet } from '@/shared/composables/useCabinet'
import { useNotify } from '@/shared/composables/useNotify'
import { formatDate, formatDateTime, formatNumber, formatRelative } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import EmptyState from '@/shared/ui/EmptyState.vue'
import InfoList from '@/shared/ui/InfoList.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import ProgressMeter from '@/shared/ui/ProgressMeter.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'
import StatusStepper from '@/shared/ui/StatusStepper.vue'
import StatusTag from '@/shared/ui/StatusTag.vue'

const route = useRoute()
const session = useSessionStore()
const queryClient = useQueryClient()
const confirm = useConfirm()
const notify = useNotify()
const { isFulfillment, to } = useCabinet()

const id = computed(() => String(route.params.id))
const queryKey = computed(() => ['inbound-shipment', id.value])

const { data: shipment, isPending, isError } = useQuery({
  queryKey,
  queryFn: () => inboundApi.get(id.value),
})

const mutation = useMutation({
  mutationFn: (action: InboundAction) => inboundApi.action(id.value, action),
  onSuccess: (updated: InboundShipment) => {
    queryClient.setQueryData(queryKey.value, updated)
    for (const key of ['inbound', 'inbound-summary', 'dashboard', 'notifications']) {
      void queryClient.invalidateQueries({ queryKey: [key] })
    }
    notify.success('Готово', `Статус поставки: ${updated.events[updated.events.length - 1]?.title.toLowerCase()}`)
  },
  onError: (error) => notify.error(error),
})

const canOperate = computed(() => !!session.role && FC_OFFICE_ROLES.includes(session.role))
const nextAction = computed(() =>
  shipment.value && isFulfillment.value && canOperate.value ? INBOUND_NEXT_ACTION[shipment.value.status] : undefined,
)
const canCancel = computed(() => !!shipment.value && INBOUND_CANCELLABLE.includes(shipment.value.status))

function runNext() {
  const action = nextAction.value
  if (!action) return
  confirm.require({
    header: action.label,
    message: action.confirm,
    acceptProps: { label: action.label },
    rejectProps: { label: 'Отмена', severity: 'secondary', outlined: true },
    accept: () => mutation.mutate(action.action),
  })
}

function cancel() {
  confirm.require({
    header: 'Отменить поставку?',
    message: 'Поставка будет отменена. Это действие нельзя отменить.',
    acceptProps: { label: 'Отменить поставку', severity: 'danger' },
    rejectProps: { label: 'Назад', severity: 'secondary', outlined: true },
    accept: () => mutation.mutate('cancel'),
  })
}

const steps = computed(() =>
  INBOUND_STEPS.map((step) => ({
    key: step.status,
    label: step.label,
    at: shipment.value?.events.findLast((event) => event.type === step.status)?.at,
  })),
)
const currentStep = computed(() =>
  shipment.value?.status === 'cancelled' ? -1 : INBOUND_STEPS.findIndex((s) => s.status === shipment.value?.status),
)

const isCounted = computed(() => !!shipment.value && ['receiving', 'received', 'placed'].includes(shipment.value.status))
const isFinal = computed(() => !!shipment.value && ['received', 'placed'].includes(shipment.value.status))

const totals = computed(() => {
  const lines = shipment.value?.lines ?? []
  return {
    shortage: lines.reduce((sum, l) => sum + Math.max(0, l.expectedQty - l.receivedQty), 0),
    surplus: lines.reduce((sum, l) => sum + Math.max(0, l.receivedQty - l.expectedQty), 0),
    defect: lines.reduce((sum, l) => sum + l.defectQty, 0),
  }
})

const hasExpiry = computed(() => shipment.value?.lines.some((l) => l.tracksExpiry) ?? false)

const info = computed(() => {
  const s = shipment.value
  if (!s) return []
  return [
    { label: isFulfillment.value ? 'Клиент' : 'Фулфилмент', value: isFulfillment.value ? s.sellerName : s.fulfillmentName },
    { label: 'Склад', value: s.warehouseName },
    { label: 'Грузовые места', value: `${s.packagesCount} ${s.packageType === 'pallets' ? 'паллет' : 'коробов'}` },
    { label: 'Создана', value: formatDateTime(s.createdAt) },
    { label: 'Ожидается', value: formatDateTime(s.expectedAt) },
    { label: 'Водитель', value: s.driverName },
    { label: 'Автомобиль', value: s.vehicleNumber },
    { label: 'Комментарий', value: s.comment },
  ]
})

const EVENT_TONES: Record<InboundEventType, Tone> = {
  draft: 'neutral',
  in_transit: 'info',
  arrived: 'warn',
  receiving: 'progress',
  received: 'progress',
  placed: 'success',
  cancelled: 'neutral',
  discrepancy: 'danger',
  comment: 'neutral',
}
const events = computed(() => [...(shipment.value?.events ?? [])].reverse())

function diffClass(diff: number) {
  if (diff < 0) return 'text-rose-600 dark:text-rose-400'
  if (diff > 0) return 'text-amber-600 dark:text-amber-400'
  return 'text-emerald-600 dark:text-emerald-400'
}
</script>

<template>
  <div>
    <div v-if="isPending" class="space-y-6">
      <Skeleton width="16rem" height="2.5rem" />
      <Skeleton height="6rem" border-radius="16px" />
      <div class="grid gap-6 lg:grid-cols-3">
        <Skeleton class="lg:col-span-2" height="22rem" border-radius="16px" />
        <Skeleton height="22rem" border-radius="16px" />
      </div>
    </div>

    <div v-else-if="isError || !shipment" class="surface-card">
      <EmptyState :icon="PackageX" title="Поставка не найдена" description="Возможно, она была удалена или у вас нет к ней доступа.">
        <RouterLink :to="to('inbound')"><Button label="К списку поставок" /></RouterLink>
      </EmptyState>
    </div>

    <template v-else>
      <PageHeader :title="`Поставка ${shipment.number}`" :back="{ to: to('inbound'), label: 'Все поставки' }">
        <template #status>
          <StatusTag domain="inbound" :status="shipment.status" size="md" />
        </template>
        <template #meta>
          <span class="inline-flex items-center gap-1.5">
            <Building2 class="size-4" />
            {{ isFulfillment ? shipment.sellerName : shipment.fulfillmentName }}
          </span>
          <span class="inline-flex items-center gap-1.5"><Warehouse class="size-4" />{{ shipment.warehouseName }}</span>
          <span class="inline-flex items-center gap-1.5">
            <CalendarClock class="size-4" />Ожидается {{ formatDateTime(shipment.expectedAt) }}
          </span>
        </template>
        <template #actions>
          <Button
            v-if="canCancel"
            label="Отменить"
            severity="danger"
            outlined
            :disabled="mutation.isPending.value"
            @click="cancel"
          />
          <Button
            v-if="nextAction"
            :label="nextAction.label"
            :loading="mutation.isPending.value"
            @click="runNext"
          />
        </template>
      </PageHeader>

      <div class="space-y-6">
        <SectionCard>
          <StatusStepper :steps="steps" :current="currentStep" cancelled-label="Поставка отменена" />
        </SectionCard>

        <Message v-if="shipment.hasDiscrepancy" severity="warn" :closable="false">
          <span class="flex items-start gap-2">
            <TriangleAlert class="mt-0.5 size-4 shrink-0" />
            <span>
              При приёмке обнаружены расхождения:
              <template v-if="totals.shortage">недостача {{ formatNumber(totals.shortage) }} шт</template>
              <template v-if="totals.shortage && (totals.surplus || totals.defect)">, </template>
              <template v-if="totals.surplus">излишек {{ formatNumber(totals.surplus) }} шт</template>
              <template v-if="totals.surplus && totals.defect">, </template>
              <template v-if="totals.defect">брак {{ formatNumber(totals.defect) }} шт</template>.
              Акт расхождений направлен {{ isFulfillment ? 'клиенту' : 'вам' }}.
            </span>
          </span>
        </Message>

        <div class="grid gap-6 lg:grid-cols-3">
          <div class="surface-card overflow-hidden lg:col-span-2">
            <Tabs value="lines">
              <TabList>
                <Tab value="lines">Состав · {{ shipment.linesCount }}</Tab>
                <Tab value="history">История · {{ shipment.events.length }}</Tab>
              </TabList>
              <TabPanels class="!p-0">
                <TabPanel value="lines">
                  <DataTable :value="shipment.lines" data-key="id" scrollable row-hover>
                    <Column header="Товар" class="min-w-52">
                      <template #body="{ data: line }">
                        <div class="font-medium text-heading">{{ line.name }}</div>
                        <div class="text-xs text-secondary">{{ line.sku }} · ШК {{ line.barcode }}</div>
                      </template>
                    </Column>
                    <Column header="План" class="text-right" header-class="[&>div]:justify-end">
                      <template #body="{ data: line }">
                        <span class="tabular">{{ formatNumber(line.expectedQty) }}</span>
                      </template>
                    </Column>
                    <Column header="Факт" class="text-right" header-class="[&>div]:justify-end">
                      <template #body="{ data: line }">
                        <span v-if="isCounted" class="tabular font-medium text-heading">{{ formatNumber(line.receivedQty) }}</span>
                        <span v-else class="text-secondary">—</span>
                      </template>
                    </Column>
                    <Column header="Брак" class="text-right" header-class="[&>div]:justify-end">
                      <template #body="{ data: line }">
                        <span v-if="isFinal" class="tabular" :class="line.defectQty ? 'font-medium text-rose-600 dark:text-rose-400' : 'text-secondary'">
                          {{ formatNumber(line.defectQty) }}
                        </span>
                        <span v-else class="text-secondary">—</span>
                      </template>
                    </Column>
                    <Column header="Разница" class="text-right" header-class="[&>div]:justify-end">
                      <template #body="{ data: line }">
                        <span v-if="isFinal" class="tabular font-semibold" :class="diffClass(line.receivedQty - line.expectedQty)">
                          {{ line.receivedQty === line.expectedQty ? '✓' : (line.receivedQty > line.expectedQty ? '+' : '') + formatNumber(line.receivedQty - line.expectedQty) }}
                        </span>
                        <span v-else class="text-secondary">—</span>
                      </template>
                    </Column>
                    <Column v-if="hasExpiry" header="Партия" class="whitespace-nowrap">
                      <template #body="{ data: line }">
                        <template v-if="line.lotNumber">
                          <div class="text-sm">{{ line.lotNumber }}</div>
                          <div class="text-xs text-secondary">до {{ line.expiresAt && formatDate(line.expiresAt) }}</div>
                        </template>
                        <span v-else class="text-secondary">—</span>
                      </template>
                    </Column>
                  </DataTable>
                </TabPanel>
                <TabPanel value="history">
                  <ol class="p-5">
                    <li v-for="(event, index) in events" :key="event.id" class="relative flex gap-4 pb-6 last:pb-0">
                      <span
                        v-if="index < events.length - 1"
                        class="absolute top-4 left-[7px] h-full w-px bg-surface-200 dark:bg-surface-800"
                      />
                      <span
                        class="relative mt-1 size-[15px] shrink-0 rounded-full ring-4 ring-white dark:ring-surface-900"
                        :class="TONE_CLASSES[EVENT_TONES[event.type]].dot"
                      />
                      <div class="min-w-0 flex-1">
                        <div class="flex flex-wrap items-baseline justify-between gap-x-3">
                          <span class="text-sm font-semibold text-heading">{{ event.title }}</span>
                          <span v-tooltip.top="formatDateTime(event.at)" class="text-xs text-secondary">
                            {{ formatRelative(event.at) }}
                          </span>
                        </div>
                        <div v-if="event.description" class="text-sm text-surface-600 dark:text-surface-300">
                          {{ event.description }}
                        </div>
                        <div class="mt-0.5 text-xs text-secondary">{{ event.actorName }}</div>
                      </div>
                    </li>
                  </ol>
                </TabPanel>
              </TabPanels>
            </Tabs>
          </div>

          <div class="space-y-6">
            <SectionCard title="Итоги приёмки">
              <div class="flex items-baseline justify-between">
                <span class="tabular text-3xl font-semibold text-heading">{{ formatNumber(shipment.receivedUnits) }}</span>
                <span class="tabular text-sm text-secondary">из {{ formatNumber(shipment.expectedUnits) }} шт</span>
              </div>
              <ProgressMeter
                class="mt-3"
                :value="shipment.receivedUnits"
                :max="shipment.expectedUnits"
                :tone="shipment.hasDiscrepancy ? 'warn' : 'success'"
              />
              <div class="mt-5 grid grid-cols-3 gap-2 text-center">
                <div class="rounded-xl bg-surface-50 p-3 dark:bg-surface-800/60">
                  <div class="tabular text-lg font-semibold" :class="totals.shortage ? 'text-rose-600 dark:text-rose-400' : 'text-heading'">
                    {{ isFinal ? formatNumber(totals.shortage) : '—' }}
                  </div>
                  <div class="text-xs text-secondary">недостача</div>
                </div>
                <div class="rounded-xl bg-surface-50 p-3 dark:bg-surface-800/60">
                  <div class="tabular text-lg font-semibold" :class="totals.surplus ? 'text-amber-600 dark:text-amber-400' : 'text-heading'">
                    {{ isFinal ? formatNumber(totals.surplus) : '—' }}
                  </div>
                  <div class="text-xs text-secondary">излишек</div>
                </div>
                <div class="rounded-xl bg-surface-50 p-3 dark:bg-surface-800/60">
                  <div class="tabular text-lg font-semibold" :class="totals.defect ? 'text-rose-600 dark:text-rose-400' : 'text-heading'">
                    {{ isFinal ? formatNumber(totals.defect) : '—' }}
                  </div>
                  <div class="text-xs text-secondary">брак</div>
                </div>
              </div>
            </SectionCard>

            <SectionCard title="Информация">
              <InfoList :items="info" />
            </SectionCard>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>
