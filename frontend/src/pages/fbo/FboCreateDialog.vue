<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { Plus, Trash2 } from '@lucide/vue'
import dayjs from 'dayjs'
import { fboApi } from '@/modules/fbo/api'
import { FBO_DESTINATIONS } from '@/modules/fbo/labels'
import type { FboMarketplace } from '@/modules/fbo/types'
import { useNotify } from '@/shared/composables/useNotify'

const visible = defineModel<boolean>('visible', { required: true })
const emit = defineEmits<{ created: [id: string] }>()
const notify = useNotify()
const qc = useQueryClient()

const { data: stock } = useQuery({
  queryKey: ['fbo-stock'],
  queryFn: () => fboApi.stock(),
  enabled: visible,
})

const marketplace = ref<FboMarketplace>('wb')
const destination = ref<string | null>(null)
const warehouseId = ref<string | null>(null)
const plannedAt = ref<Date>(dayjs().add(3, 'day').startOf('day').toDate())
const lines = ref<{ productId: string | null; quantity: number }[]>([{ productId: null, quantity: 10 }])

watch(marketplace, () => (destination.value = null))
watch(visible, (v) => {
  if (v) lines.value = [{ productId: null, quantity: 10 }]
})

const warehouses = computed(() => {
  const map = new Map((stock.value ?? []).map((s) => [s.warehouseId, s.warehouseName]))
  return [...map].map(([value, label]) => ({ value, label }))
})
watch(warehouses, (w) => {
  if (!warehouseId.value && w[0]) warehouseId.value = w[0].value
})
const products = computed(() =>
  (stock.value ?? [])
    .filter((s) => s.warehouseId === warehouseId.value)
    .map((s) => ({ value: s.productId, label: `${s.sku} · ${s.name}`, available: s.available })),
)
const availableOf = (id: string | null) => products.value.find((p) => p.value === id)?.available ?? 0
const units = computed(() => lines.value.reduce((a, l) => a + (l.productId ? l.quantity : 0), 0))
const valid = computed(
  () =>
    !!destination.value &&
    !!warehouseId.value &&
    lines.value.some((l) => l.productId && l.quantity > 0) &&
    lines.value.every((l) => !l.productId || l.quantity <= availableOf(l.productId)),
)

const create = useMutation({
  mutationFn: () =>
    fboApi.create({
      marketplace: marketplace.value,
      destination: destination.value!,
      warehouseId: warehouseId.value!,
      plannedAt: plannedAt.value.toISOString(),
      lines: lines.value
        .filter((l) => l.productId)
        .map((l) => ({ productId: l.productId!, quantity: l.quantity })),
    }),
  onSuccess: (r) => {
    notify.success('Черновик создан', `${r.number}: ${r.units} шт`)
    void qc.invalidateQueries({ queryKey: ['fbo'] })
    void qc.invalidateQueries({ queryKey: ['fbo-summary'] })
    visible.value = false
    emit('created', r.id)
  },
  onError: (e: unknown) => notify.error(e),
})
const mpOptions = [
  { value: 'wb', label: 'Wildberries' },
  { value: 'ozon', label: 'Ozon' },
]
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    header="Новая поставка FBO"
    class="w-[calc(100vw-1.5rem)] max-w-2xl"
    :draggable="false"
  >
    <div class="space-y-4">
      <SelectButton
        v-model="marketplace"
        :options="mpOptions"
        option-label="label"
        option-value="value"
        :allow-empty="false"
      />
      <div class="grid gap-3 sm:grid-cols-3">
        <div>
          <label class="mb-1 block text-sm font-medium">Склад маркетплейса</label>
          <Select
            v-model="destination"
            :options="FBO_DESTINATIONS[marketplace]"
            placeholder="Выберите"
            class="w-full"
          />
        </div>
        <div>
          <label class="mb-1 block text-sm font-medium">Склад ФЦ</label>
          <Select
            v-model="warehouseId"
            :options="warehouses"
            option-label="label"
            option-value="value"
            class="w-full"
          />
        </div>
        <div>
          <label class="mb-1 block text-sm font-medium">Отгрузка</label>
          <DatePicker
            v-model="plannedAt"
            :min-date="new Date()"
            date-format="dd.mm.yy"
            class="w-full"
            fluid
          />
        </div>
      </div>

      <div class="space-y-2">
        <div v-for="(l, i) in lines" :key="i" class="flex flex-wrap items-start gap-2 sm:flex-nowrap">
          <Select
            v-model="l.productId"
            :options="products"
            option-label="label"
            option-value="value"
            filter
            placeholder="Товар"
            class="w-full min-w-0 sm:flex-1"
          />
          <div class="flex flex-1 items-start gap-2 sm:flex-none">
            <div class="flex-1 sm:w-28">
              <InputNumber
                v-model="l.quantity"
                :min="1"
                :invalid="!!l.productId && l.quantity > availableOf(l.productId)"
                fluid
              />
              <div v-if="l.productId" class="mt-0.5 text-xs text-secondary">
                доступно {{ availableOf(l.productId) }}
              </div>
            </div>
            <Button
              text
              severity="secondary"
              aria-label="Удалить строку"
              :disabled="lines.length === 1"
              @click="lines.splice(i, 1)"
            >
              <Trash2 class="size-4" />
            </Button>
          </div>
        </div>
        <Button text size="small" @click="lines.push({ productId: null, quantity: 10 })">
          <Plus class="size-4" /> Добавить товар
        </Button>
      </div>
    </div>
    <template #footer>
      <div class="flex w-full flex-wrap items-center justify-between gap-2">
        <span class="text-sm text-secondary"
          >Итого {{ units }} шт · ~{{ Math.max(1, Math.ceil(units / 40)) }} коробов</span
        >
        <div class="flex gap-2">
          <Button label="Отмена" severity="secondary" text @click="visible = false" />
          <Button
            label="Создать черновик"
            :disabled="!valid"
            :loading="create.isPending.value"
            @click="create.mutate()"
          />
        </div>
      </div>
    </template>
  </Dialog>
</template>
