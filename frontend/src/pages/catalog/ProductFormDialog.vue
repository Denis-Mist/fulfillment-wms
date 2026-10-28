<script setup lang="ts">
import { computed, reactive, watch } from 'vue'
import { useMutation, useQueryClient } from '@tanstack/vue-query'
import { catalogApi } from '@/modules/catalog/api'
import type { ProductInput, ProductListItem } from '@/modules/catalog/types'
import { isValidEan } from '@/modules/catalog/validation'
import { useNotify } from '@/shared/composables/useNotify'

const props = defineProps<{ product?: ProductListItem | null; categories: string[] }>()
const visible = defineModel<boolean>('visible', { required: true })

const notify = useNotify()
const queryClient = useQueryClient()

const empty = (): ProductInput => ({
  sku: '',
  name: '',
  category: '',
  barcode: '',
  wbBarcode: '',
  price: 0,
  weightG: 0,
  volumeLiters: 0,
  tracksExpiry: false,
  requiresMarking: false,
})
const form = reactive<ProductInput>(empty())
const touched = reactive<Record<string, boolean>>({})

watch(visible, (open) => {
  if (!open) return
  Object.assign(
    form,
    empty(),
    props.product ? { ...props.product, wbBarcode: props.product.wbBarcode ?? '' } : {},
  )
  for (const key of Object.keys(touched)) delete touched[key]
})

const errors = computed<Record<string, string | undefined>>(() => ({
  sku: !form.sku.trim()
    ? 'Обязательное поле'
    : !/^[A-Za-z0-9-_.]{2,32}$/.test(form.sku.trim())
      ? 'Латиница, цифры, «-», «_», «.» — от 2 до 32 символов'
      : undefined,
  name: form.name.trim().length < 3 ? 'Не короче 3 символов' : undefined,
  barcode: !/^\d{8}$|^\d{13}$/.test(form.barcode) ? 'EAN-8 или EAN-13: 8 или 13 цифр' : undefined,
  wbBarcode: form.wbBarcode && !/^\d{8,14}$/.test(form.wbBarcode) ? 'От 8 до 14 цифр' : undefined,
  price: form.price > 0 ? undefined : 'Больше нуля',
  weightG: form.weightG > 0 ? undefined : 'Больше нуля',
  volumeLiters: form.volumeLiters > 0 ? undefined : 'Больше нуля',
}))
const invalid = computed(() => Object.values(errors.value).some(Boolean))
const checksumWarning = computed(() => !errors.value.barcode && !isValidEan(form.barcode))
const show = (key: string) => touched[key] && errors.value[key]

const categorySuggestions = computed(() =>
  props.categories.filter((c) => c.toLowerCase().includes(form.category.toLowerCase())),
)

const mutation = useMutation({
  mutationFn: () => {
    const input = { ...form, wbBarcode: form.wbBarcode || undefined }
    return props.product ? catalogApi.update(props.product.id, input) : catalogApi.create(input)
  },
  onSuccess: (item) => {
    notify.success(props.product ? 'Карточка обновлена' : 'Товар добавлен', `${item.sku} · ${item.name}`)
    void queryClient.invalidateQueries({ queryKey: ['catalog'] })
    visible.value = false
  },
  onError: (e) => notify.error(e, 'Не удалось сохранить товар'),
})

function submit() {
  for (const key of Object.keys(errors.value)) touched[key] = true
  if (!invalid.value) mutation.mutate()
}
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    :header="product ? 'Редактирование товара' : 'Новый товар'"
    class="w-[calc(100vw-2rem)] max-w-2xl"
  >
    <form class="grid gap-4 sm:grid-cols-2" novalidate @submit.prevent="submit">
      <div class="space-y-1.5">
        <label for="pf-sku" class="text-sm font-medium text-heading">Артикул (SKU)</label>
        <InputText id="pf-sku" v-model="form.sku" :invalid="!!show('sku')" fluid @blur="touched.sku = true" />
        <small v-if="show('sku')" class="text-red-600">{{ errors.sku }}</small>
      </div>
      <div class="space-y-1.5">
        <label for="pf-cat" class="text-sm font-medium text-heading">Категория</label>
        <AutoComplete
          v-model="form.category"
          input-id="pf-cat"
          :suggestions="categorySuggestions"
          dropdown
          fluid
          @complete="() => {}"
        />
      </div>
      <div class="space-y-1.5 sm:col-span-2">
        <label for="pf-name" class="text-sm font-medium text-heading">Название</label>
        <InputText
          id="pf-name"
          v-model="form.name"
          :invalid="!!show('name')"
          fluid
          @blur="touched.name = true"
        />
        <small v-if="show('name')" class="text-red-600">{{ errors.name }}</small>
      </div>
      <div class="space-y-1.5">
        <label for="pf-bc" class="text-sm font-medium text-heading">Штрихкод</label>
        <InputText
          id="pf-bc"
          v-model="form.barcode"
          inputmode="numeric"
          :invalid="!!show('barcode')"
          fluid
          @blur="touched.barcode = true"
        />
        <small v-if="show('barcode')" class="text-red-600">{{ errors.barcode }}</small>
        <small v-else-if="checksumWarning && form.barcode" class="text-amber-600"
          >Контрольная цифра не сходится — проверьте код</small
        >
      </div>
      <div class="space-y-1.5">
        <label for="pf-wb" class="text-sm font-medium text-heading">ШК маркетплейса</label>
        <InputText
          id="pf-wb"
          v-model="form.wbBarcode"
          inputmode="numeric"
          placeholder="Необязательно"
          :invalid="!!show('wbBarcode')"
          fluid
          @blur="touched.wbBarcode = true"
        />
        <small v-if="show('wbBarcode')" class="text-red-600">{{ errors.wbBarcode }}</small>
      </div>
      <div class="grid grid-cols-3 gap-3 sm:col-span-2">
        <div class="space-y-1.5">
          <label for="pf-price" class="text-sm font-medium text-heading">Цена, ₽</label>
          <InputNumber
            v-model="form.price"
            input-id="pf-price"
            :min="0"
            :invalid="!!show('price')"
            fluid
            @blur="touched.price = true"
          />
        </div>
        <div class="space-y-1.5">
          <label for="pf-w" class="text-sm font-medium text-heading">Вес, г</label>
          <InputNumber
            v-model="form.weightG"
            input-id="pf-w"
            :min="0"
            :invalid="!!show('weightG')"
            fluid
            @blur="touched.weightG = true"
          />
        </div>
        <div class="space-y-1.5">
          <label for="pf-v" class="text-sm font-medium text-heading">Объём, л</label>
          <InputNumber
            v-model="form.volumeLiters"
            input-id="pf-v"
            :min="0"
            :max-fraction-digits="2"
            :invalid="!!show('volumeLiters')"
            fluid
            @blur="touched.volumeLiters = true"
          />
        </div>
      </div>
      <label class="flex items-center justify-between gap-3 rounded-lg border border-default p-3">
        <span>
          <span class="block text-sm font-medium text-heading">Учёт сроков годности</span>
          <span class="block text-xs text-secondary">Партии и FEFO при отборе</span>
        </span>
        <ToggleSwitch v-model="form.tracksExpiry" />
      </label>
      <label class="flex items-center justify-between gap-3 rounded-lg border border-default p-3">
        <span>
          <span class="block text-sm font-medium text-heading">Маркировка «Честный знак»</span>
          <span class="block text-xs text-secondary">Сканирование кода при упаковке</span>
        </span>
        <ToggleSwitch v-model="form.requiresMarking" />
      </label>
      <div class="flex justify-end gap-2 sm:col-span-2">
        <Button type="button" label="Отмена" severity="secondary" text @click="visible = false" />
        <Button
          type="submit"
          :label="product ? 'Сохранить' : 'Добавить'"
          :loading="mutation.isPending.value"
        />
      </div>
    </form>
  </Dialog>
</template>
