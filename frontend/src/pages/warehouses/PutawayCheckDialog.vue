<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useMutation, useQuery } from '@tanstack/vue-query'
import { refDebounced } from '@vueuse/core'
import { CircleCheck, CircleX } from '@lucide/vue'
import { warehousesApi } from '@/modules/warehouses/api'
import type { WarehouseProductOption } from '@/modules/warehouses/types'

const props = defineProps<{ warehouseId: string; initialLocationId?: string }>()
const visible = defineModel<boolean>('visible', { required: true })

const locationSearch = ref('')
const locationId = ref<string | undefined>()
const product = ref<WarehouseProductOption | undefined>()
const productSearch = ref('')
const quantity = ref(10)

const { data: locationOptions } = useQuery({
  queryKey: computed(() => ['warehouse-locations', props.warehouseId, 'options', locationSearch.value]),
  queryFn: () => warehousesApi.locations(props.warehouseId, { page: 1, pageSize: 50, search: locationSearch.value || undefined }),
  enabled: visible,
})

const debouncedProduct = refDebounced(productSearch, 250)
const { data: products } = useQuery({
  queryKey: computed(() => ['warehouse-products', props.warehouseId, debouncedProduct.value]),
  queryFn: () => warehousesApi.products(props.warehouseId, debouncedProduct.value),
  enabled: visible,
})

const check = useMutation({
  mutationFn: () =>
    warehousesApi.checkPutaway(props.warehouseId, {
      locationId: locationId.value!,
      productId: product.value!.id,
      quantity: quantity.value,
    }),
})

watch(visible, (open) => {
  if (!open) return
  check.reset()
  locationId.value = props.initialLocationId
  locationSearch.value = props.initialLocationId?.split('_l_')[1] ?? ''
})
watch([locationId, product, quantity], () => check.reset())

const options = computed(() => (locationOptions.value?.items ?? []).map((l) => ({ value: l.id, label: `${l.code} · ${Math.round((l.usedLiters / l.capacityLiters) * 100)}%` })))
const ready = computed(() => !!locationId.value && !!product.value && quantity.value > 0)
</script>

<template>
  <Dialog v-model:visible="visible" modal header="Проверка размещения" class="w-[calc(100vw-2rem)] max-w-xl">
    <p class="mb-4 text-sm text-secondary">
      Система проверит договор, тип зоны, свободный объём, ограничения по массе и принадлежность ячейки. Нарушающее размещение будет отклонено с пояснением.
    </p>
    <div class="grid gap-4 sm:grid-cols-[1fr_8rem]">
      <div class="space-y-1.5 sm:col-span-2">
        <label for="pa-product" class="text-sm font-medium text-heading">Товар</label>
        <AutoComplete
          v-model="product"
          input-id="pa-product"
          :suggestions="products ?? []"
          option-label="name"
          placeholder="Название, артикул или штрихкод"
          dropdown
          fluid
          @complete="productSearch = $event.query"
        >
          <template #option="{ option }">
            <div class="min-w-0">
              <div class="truncate text-sm">{{ option.name }}</div>
              <div class="text-xs text-secondary">{{ option.sku }} · {{ option.sellerName }} · {{ option.volumeLiters }} л</div>
            </div>
          </template>
        </AutoComplete>
      </div>
      <div class="space-y-1.5">
        <label for="pa-loc" class="text-sm font-medium text-heading">Ячейка</label>
        <Select
          v-model="locationId"
          input-id="pa-loc"
          :options="options"
          option-label="label"
          option-value="value"
          filter
          placeholder="Выберите ячейку"
          fluid
          @filter="locationSearch = $event.value"
        />
      </div>
      <div class="space-y-1.5">
        <label for="pa-qty" class="text-sm font-medium text-heading">Кол-во, шт</label>
        <InputNumber v-model="quantity" input-id="pa-qty" :min="1" :max="100000" fluid />
      </div>
    </div>

    <div v-if="check.data.value" class="mt-5 rounded-xl border p-4" :class="check.data.value.allowed ? 'border-emerald-200 bg-emerald-50/60 dark:border-emerald-500/30 dark:bg-emerald-500/10' : 'border-rose-200 bg-rose-50/60 dark:border-rose-500/30 dark:bg-rose-500/10'">
      <div class="mb-3 font-semibold" :class="check.data.value.allowed ? 'text-emerald-700 dark:text-emerald-300' : 'text-rose-700 dark:text-rose-300'">
        {{ check.data.value.allowed ? 'Размещение допустимо' : 'Размещение отклонено' }}
      </div>
      <ul class="space-y-2">
        <li v-for="item in check.data.value.checks" :key="item.rule" class="flex gap-2.5 text-sm">
          <component :is="item.passed ? CircleCheck : CircleX" class="mt-0.5 size-4 shrink-0" :class="item.passed ? 'text-emerald-600' : 'text-rose-600'" />
          <span><span class="font-medium text-heading">{{ item.rule }}:</span> <span class="text-secondary">{{ item.detail }}</span></span>
        </li>
      </ul>
    </div>
    <Message v-else-if="check.error.value" severity="error" class="mt-4">{{ check.error.value.message }}</Message>

    <template #footer>
      <Button label="Закрыть" severity="secondary" outlined @click="visible = false" />
      <Button label="Проверить" :disabled="!ready" :loading="check.isPending.value" @click="check.mutate()" />
    </template>
  </Dialog>
</template>
