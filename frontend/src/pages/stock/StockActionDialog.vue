<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useMutation, useQueryClient } from '@tanstack/vue-query'
import { stockApi } from '@/modules/stock/api'
import { ERROR_REASONS } from '@/modules/stock/labels'
import type { ErrorReason, OperationType, StockItem, StockLocationOption } from '@/modules/stock/types'
import { useNotify } from '@/shared/composables/useNotify'
import { formatNumber } from '@/shared/lib/format'

const props = defineProps<{ type: OperationType; item?: StockItem | null; warehouseId?: string }>()
const visible = defineModel<boolean>('visible', { required: true })
const emit = defineEmits<{ done: [] }>()

const notify = useNotify()
const queryClient = useQueryClient()

const TITLES: Record<OperationType, string> = {
  move: 'Перемещение товара',
  inventory: 'Инвентаризация ячейки',
  writeoff: 'Списание',
}

/* Позиция может быть передана сразу (из карточки) или выбрана поиском (из журнала) */
const picked = ref<StockItem | null>(null)
const stockSearch = ref('')
const stockSuggestions = ref<StockItem[]>([])
const item = computed(() => props.item ?? picked.value)

async function searchStock(event: { query: string }) {
  const res = await stockApi.list({
    page: 1,
    pageSize: 15,
    search: event.query,
    warehouseId: props.warehouseId,
  })
  stockSuggestions.value = res.items
}

const target = ref<StockLocationOption | null>(null)
const locationSuggestions = ref<StockLocationOption[]>([])
async function searchLocations(event: { query: string }) {
  if (!item.value) return
  const list = await stockApi.locations(item.value.warehouseId, event.query)
  locationSuggestions.value = list.filter((l) => l.id !== item.value?.locationId)
}

const quantity = ref<number | null>(null)
const counted = ref<number | null>(null)
const reason = ref<ErrorReason>('defect')
const comment = ref('')
const reasonOptions = Object.entries(ERROR_REASONS).map(([value, label]) => ({ value, label }))

watch(visible, (open) => {
  if (!open) return
  picked.value = null
  stockSearch.value = ''
  target.value = null
  quantity.value = null
  counted.value = props.item?.quantity ?? null
  reason.value = 'defect'
  comment.value = ''
})
watch(picked, (value) => {
  if (value && typeof value === 'object') counted.value = value.quantity
})

const free = computed(() => (item.value ? item.value.quantity - item.value.reserved : 0))
const delta = computed(() => (item.value && counted.value !== null ? counted.value - item.value.quantity : 0))

const error = computed<string | null>(() => {
  const it = item.value
  if (!it || typeof it !== 'object') return 'Выберите позицию остатка'
  if (props.type === 'move') {
    if (!target.value || typeof target.value !== 'object') return 'Укажите ячейку назначения'
    if (!quantity.value) return 'Укажите количество'
    if (quantity.value > free.value) return `Свободно только ${free.value} шт`
  }
  if (props.type === 'writeoff') {
    if (!quantity.value) return 'Укажите количество'
    if (quantity.value > free.value) return `Свободно только ${free.value} шт`
  }
  if (props.type === 'inventory') {
    if (counted.value === null) return 'Укажите фактическое количество'
    if (counted.value < it.reserved) return `Факт не может быть меньше резерва (${it.reserved} шт)`
  }
  return null
})

const mutation = useMutation({
  mutationFn: () => {
    const stockId = item.value!.id
    const note = comment.value.trim() || undefined
    if (props.type === 'move')
      return stockApi.move({
        stockId,
        toLocationId: target.value!.id,
        quantity: quantity.value!,
        comment: note,
      })
    if (props.type === 'inventory')
      return stockApi.inventory({ stockId, counted: counted.value!, comment: note })
    return stockApi.writeoff({ stockId, quantity: quantity.value!, reason: reason.value, comment: note })
  },
  onSuccess: (op) => {
    notify.success(`${TITLES[props.type]} проведено`, `${op.number} · ${op.sku}`)
    void queryClient.invalidateQueries({ queryKey: ['stock'] })
    void queryClient.invalidateQueries({ queryKey: ['stock-summary'] })
    void queryClient.invalidateQueries({ queryKey: ['stock-operations'] })
    void queryClient.invalidateQueries({ queryKey: ['stock-operations-summary'] })
    visible.value = false
    emit('done')
  },
  onError: (e) => notify.error(e, 'Операция не проведена'),
})
</script>

<template>
  <Dialog v-model:visible="visible" modal :header="TITLES[type]" class="w-[calc(100vw-2rem)] max-w-lg">
    <form class="space-y-4" @submit.prevent="!error && mutation.mutate()">
      <div v-if="!props.item" class="space-y-1.5">
        <label class="text-sm font-medium text-heading" for="op-stock">Позиция</label>
        <AutoComplete
          v-model="picked"
          input-id="op-stock"
          :suggestions="stockSuggestions"
          option-label="sku"
          placeholder="Артикул, товар или ячейка"
          fluid
          @complete="searchStock"
        >
          <template #option="{ option }">
            <div class="min-w-0">
              <div class="truncate text-sm text-heading">{{ option.sku }} · {{ option.productName }}</div>
              <div class="text-xs text-secondary">
                {{ option.locationCode }} · {{ option.sellerName }} · {{ formatNumber(option.quantity) }} шт
              </div>
            </div>
          </template>
        </AutoComplete>
      </div>

      <div
        v-if="item && typeof item === 'object'"
        class="rounded-lg border border-default bg-surface-50 p-3 text-sm dark:bg-surface-900"
      >
        <div class="font-medium text-heading">{{ item.productName }}</div>
        <div class="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-secondary">
          <span>{{ item.sku }}</span>
          <span>Ячейка {{ item.locationCode }}</span>
          <span class="tabular">Всего {{ formatNumber(item.quantity) }}</span>
          <span class="tabular">Резерв {{ formatNumber(item.reserved) }}</span>
        </div>
      </div>

      <template v-if="type === 'move'">
        <div class="space-y-1.5">
          <label class="text-sm font-medium text-heading" for="op-target">Ячейка назначения</label>
          <AutoComplete
            v-model="target"
            input-id="op-target"
            :suggestions="locationSuggestions"
            option-label="code"
            placeholder="Код ячейки"
            :disabled="!item"
            dropdown
            fluid
            @complete="searchLocations"
          >
            <template #option="{ option }">
              <span class="text-sm">{{ option.code }}</span>
              <span class="ml-2 text-xs text-secondary">зона {{ option.zoneCode }}</span>
            </template>
          </AutoComplete>
        </div>
      </template>

      <div v-if="type !== 'inventory'" class="space-y-1.5">
        <label class="text-sm font-medium text-heading" for="op-qty">Количество, шт</label>
        <InputNumber v-model="quantity" input-id="op-qty" :min="1" :max="Math.max(1, free)" fluid />
        <p v-if="item" class="text-xs text-secondary">
          Свободно {{ formatNumber(free) }} шт — резерв под заказы не трогаем
        </p>
      </div>

      <div v-if="type === 'writeoff'" class="space-y-1.5">
        <label class="text-sm font-medium text-heading" for="op-reason">Причина</label>
        <Select
          v-model="reason"
          input-id="op-reason"
          :options="reasonOptions"
          option-label="label"
          option-value="value"
          fluid
        />
      </div>

      <div v-if="type === 'inventory'" class="space-y-1.5">
        <label class="text-sm font-medium text-heading" for="op-counted">Фактически в ячейке, шт</label>
        <InputNumber v-model="counted" input-id="op-counted" :min="0" fluid />
        <p
          v-if="item && counted !== null"
          class="tabular text-xs"
          :class="delta === 0 ? 'text-secondary' : delta < 0 ? 'text-red-600' : 'text-amber-600'"
        >
          {{
            delta === 0
              ? 'Расхождений нет'
              : `Расхождение ${delta > 0 ? '+' : ''}${delta} шт — будет оформлен акт`
          }}
        </p>
      </div>

      <div class="space-y-1.5">
        <label class="text-sm font-medium text-heading" for="op-comment">Комментарий</label>
        <InputText id="op-comment" v-model="comment" placeholder="Необязательно" fluid />
      </div>

      <Message
        v-if="error && (quantity || counted !== null || target)"
        severity="warn"
        size="small"
        variant="simple"
        >{{ error }}</Message
      >

      <div class="flex justify-end gap-2 pt-1">
        <Button type="button" label="Отмена" severity="secondary" text @click="visible = false" />
        <Button
          type="submit"
          :label="type === 'writeoff' ? 'Списать' : 'Провести'"
          :severity="type === 'writeoff' ? 'danger' : undefined"
          :disabled="!!error"
          :loading="mutation.isPending.value"
        />
      </div>
    </form>
  </Dialog>
</template>
