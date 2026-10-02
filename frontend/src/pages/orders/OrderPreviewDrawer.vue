<script setup lang="ts">
import { computed } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import { TriangleAlert } from '@lucide/vue'
import { ordersApi } from '@/modules/orders/api'
import { SALES_CHANNELS } from '@/shared/config/statuses'
import { formatDateTime, formatMoney, formatNumber } from '@/shared/lib/format'
import ChannelBadge from '@/shared/ui/ChannelBadge.vue'
import InfoList from '@/shared/ui/InfoList.vue'
import StatusTag from '@/shared/ui/StatusTag.vue'

const orderId = defineModel<string | null>('orderId', { default: null })
const props = defineProps<{ showSeller: boolean }>()

const visible = computed({
  get: () => !!orderId.value,
  set: (value) => {
    if (!value) orderId.value = null
  },
})

const { data: order, isPending } = useQuery({
  queryKey: computed(() => ['order', orderId.value]),
  queryFn: () => ordersApi.get(orderId.value!),
  enabled: computed(() => !!orderId.value),
})

const info = computed(() => {
  const o = order.value
  if (!o) return []
  return [
    ...(props.showSeller ? [{ label: 'Клиент', value: o.sellerName }] : []),
    { label: 'Склад', value: o.warehouseName },
    { label: 'Канал', value: SALES_CHANNELS[o.channel].label },
    { label: 'Номер маркетплейса', value: o.externalNumber },
    { label: 'Город доставки', value: o.deliveryCity },
    { label: 'Создан', value: formatDateTime(o.createdAt) },
    { label: 'Отгрузить до', value: formatDateTime(o.shipBy) },
    { label: 'Отгружен', value: o.shippedAt ? formatDateTime(o.shippedAt) : null },
  ]
})
</script>

<template>
  <Drawer v-model:visible="visible" position="right" class="!w-full sm:!w-[520px]" :pt="{ header: { class: '!pb-2' } }">
    <template #header>
      <div v-if="order" class="min-w-0">
        <div class="flex items-center gap-2.5">
          <span class="text-lg font-semibold text-heading">{{ order.number }}</span>
          <ChannelBadge :channel="order.channel" />
        </div>
        <div class="mt-1.5"><StatusTag domain="order" :status="order.status" /></div>
      </div>
      <Skeleton v-else width="12rem" height="2rem" />
    </template>

    <div v-if="isPending" class="space-y-3">
      <Skeleton v-for="i in 8" :key="i" height="1.75rem" />
    </div>
    <div v-else-if="order" class="space-y-6">
      <Message v-if="order.isOverdue" severity="error" :closable="false" size="small">
        <span class="flex items-center gap-2">
          <TriangleAlert class="size-4" />
          Срок отгрузки истёк — заказ нужно отгрузить в первую очередь
        </span>
      </Message>

      <InfoList :items="info" />

      <div>
        <div class="mb-2 text-sm font-semibold text-heading">Состав заказа</div>
        <ul class="divide-y divide-surface-100 rounded-xl border border-surface-200/80 dark:divide-surface-800 dark:border-surface-800">
          <li v-for="line in order.lines" :key="line.productId" class="flex items-center gap-3 px-4 py-3">
            <div class="min-w-0 flex-1">
              <div class="truncate text-sm font-medium text-heading">{{ line.name }}</div>
              <div class="text-xs text-secondary">{{ line.sku }} · ШК {{ line.barcode }}</div>
            </div>
            <div class="tabular text-right text-sm">
              <div class="font-medium text-heading">× {{ formatNumber(line.quantity) }}</div>
              <div class="text-xs text-secondary">{{ formatMoney(line.price) }}</div>
            </div>
          </li>
        </ul>
        <div class="mt-3 flex items-center justify-between px-1 text-sm">
          <span class="text-secondary">Итого · {{ formatNumber(order.unitsCount) }} шт</span>
          <span class="tabular text-base font-semibold text-heading">{{ formatMoney(order.amount) }}</span>
        </div>
      </div>
    </div>
  </Drawer>
</template>
