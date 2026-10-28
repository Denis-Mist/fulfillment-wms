<script setup lang="ts">
import { computed, ref } from 'vue'
import { keepPreviousData, useQuery } from '@tanstack/vue-query'
import { ArrowLeftRight, ClipboardCheck, Trash2 } from '@lucide/vue'
import { stockApi } from '@/modules/stock/api'
import { ERROR_REASONS, OPERATION_TYPES, STOCK_STATUSES } from '@/modules/stock/labels'
import type { OperationType, StockItem } from '@/modules/stock/types'
import { formatDateTime, formatNumber } from '@/shared/lib/format'
import InfoList from '@/shared/ui/InfoList.vue'
import EmptyState from '@/shared/ui/EmptyState.vue'
import StockActionDialog from './StockActionDialog.vue'
import ToneBadge from './ToneBadge.vue'

const props = defineProps<{ item: StockItem | null; canOperate: boolean }>()
const visible = defineModel<boolean>('visible', { required: true })

const stockId = computed(() => props.item?.id)
const { data: fresh } = useQuery({
  queryKey: computed(() => ['stock', 'item', stockId.value]),
  queryFn: () => stockApi.get(stockId.value!),
  enabled: computed(() => !!stockId.value && visible.value),
  placeholderData: keepPreviousData,
})
const current = computed(() => (fresh.value?.id === stockId.value ? fresh.value : props.item))

const { data: history, isPending: historyPending } = useQuery({
  queryKey: computed(() => ['stock-operations', 'history', stockId.value]),
  queryFn: () =>
    stockApi.operations({
      page: 1,
      pageSize: 20,
      stockId: stockId.value,
      sortField: 'at',
      sortOrder: 'desc',
    }),
  enabled: computed(() => !!stockId.value && visible.value),
})

const info = computed(() =>
  current.value
    ? [
        { label: 'Клиент', value: current.value.sellerName },
        { label: 'Склад', value: current.value.warehouseName },
        { label: 'Ячейка', value: `${current.value.locationCode} · зона ${current.value.zoneCode}` },
        { label: 'Штрихкод', value: current.value.barcode || '—' },
        { label: 'Объём', value: `${formatNumber(current.value.volumeLiters)} л` },
      ]
    : [],
)

const action = ref<OperationType>('move')
const dialog = ref(false)
function open(type: OperationType) {
  action.value = type
  dialog.value = true
}
</script>

<template>
  <Drawer
    v-model:visible="visible"
    position="right"
    class="!w-full sm:!w-[30rem]"
    :header="current?.sku ?? 'Позиция'"
  >
    <div v-if="current" class="space-y-5">
      <div>
        <div class="text-base font-semibold text-heading">{{ current.productName }}</div>
        <div class="mt-2"><ToneBadge v-bind="STOCK_STATUSES[current.status]" /></div>
      </div>

      <div class="grid grid-cols-3 gap-2 text-center">
        <div class="rounded-lg border border-default p-2.5">
          <div class="tabular text-lg font-semibold text-heading">{{ formatNumber(current.quantity) }}</div>
          <div class="text-xs text-secondary">Всего</div>
        </div>
        <div class="rounded-lg border border-default p-2.5">
          <div class="tabular text-lg font-semibold text-heading">{{ formatNumber(current.reserved) }}</div>
          <div class="text-xs text-secondary">Резерв</div>
        </div>
        <div class="rounded-lg border border-default p-2.5">
          <div class="tabular text-lg font-semibold text-emerald-600">
            {{ formatNumber(current.available) }}
          </div>
          <div class="text-xs text-secondary">Доступно</div>
        </div>
      </div>

      <InfoList :items="info" />

      <div v-if="canOperate" class="flex flex-wrap gap-2">
        <Button size="small" severity="secondary" outlined @click="open('move')"
          ><ArrowLeftRight class="size-4" />Переместить</Button
        >
        <Button size="small" severity="secondary" outlined @click="open('inventory')"
          ><ClipboardCheck class="size-4" />Пересчитать</Button
        >
        <Button size="small" severity="danger" outlined @click="open('writeoff')"
          ><Trash2 class="size-4" />Списать</Button
        >
      </div>

      <div>
        <h3 class="mb-2 text-sm font-semibold text-heading">История движений</h3>
        <div v-if="historyPending" class="space-y-2">
          <Skeleton v-for="i in 3" :key="i" height="3rem" />
        </div>
        <EmptyState v-else-if="!history?.items.length" compact title="Движений пока не было" />
        <ol v-else class="space-y-2">
          <li v-for="op in history.items" :key="op.id" class="rounded-lg border border-default p-3 text-sm">
            <div class="flex flex-wrap items-center justify-between gap-2">
              <ToneBadge v-bind="OPERATION_TYPES[op.type]" />
              <span class="tabular text-xs text-secondary">{{ formatDateTime(op.at) }}</span>
            </div>
            <div class="mt-1.5 text-heading">
              <template v-if="op.type === 'move'"
                >{{ op.fromLocationCode }} → {{ op.toLocationCode }}, {{ op.quantity }} шт</template
              >
              <template v-else-if="op.type === 'inventory'">
                Учёт {{ op.expected }} → факт {{ op.counted }}
                <span v-if="op.quantity" :class="op.quantity < 0 ? 'text-red-600' : 'text-amber-600'"
                  >({{ op.quantity > 0 ? '+' : '' }}{{ op.quantity }})</span
                >
              </template>
              <template v-else
                >{{ op.quantity }} шт · {{ op.reason ? ERROR_REASONS[op.reason] : '' }}</template
              >
            </div>
            <div class="mt-0.5 text-xs text-secondary">
              {{ op.actorName }}<template v-if="op.comment"> · {{ op.comment }}</template>
            </div>
          </li>
        </ol>
      </div>
    </div>
    <StockActionDialog v-model:visible="dialog" :type="action" :item="current" />
  </Drawer>
</template>
