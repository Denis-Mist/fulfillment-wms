<script setup lang="ts">
import { computed, reactive, watch } from 'vue'
import { useMutation, useQueryClient } from '@tanstack/vue-query'
import { warehousesApi } from '@/modules/warehouses/api'
import type { LocationInput, Zone } from '@/modules/warehouses/types'
import { z } from 'zod'
import { useFormValidation } from '@/shared/composables/useFormValidation'
import { useNotify } from '@/shared/composables/useNotify'

const props = defineProps<{ warehouseId: string; zones: Zone[] }>()
const visible = defineModel<boolean>('visible', { required: true })
const queryClient = useQueryClient()
const notify = useNotify()

const blank = (): LocationInput => ({
  zoneId: props.zones.find((z) => z.type === 'storage')?.id ?? '',
  aisle: 'A',
  rack: 1,
  level: 1,
  capacityLiters: 600,
})
const form = reactive<LocationInput>(blank())
const int = (label: string, max: number) =>
  z
    .number({ error: `Укажите ${label}` })
    .int(`${label[0]!.toUpperCase()}${label.slice(1)} — целое число`)
    .min(1, `Минимум 1`)
    .max(max, `Максимум ${max}`)
const schema = z.object({
  zoneId: z.string().min(1, 'Выберите зону'),
  aisle: z
    .string()
    .trim()
    .min(1, 'Укажите проход')
    .refine((v) => /^[A-Z]{1,2}$/i.test(v), 'Проход: 1–2 латинские буквы, например A'),
  rack: int('стеллаж', 99),
  level: int('ярус', 8),
  capacityLiters: int('вместимость', 5000),
})
const v = useFormValidation(schema, form)

watch(visible, (open) => {
  if (!open) return
  Object.assign(form, blank())
  v.reset()
})

function submit() {
  if (v.validate()) create.mutate()
}

const zoneOptions = computed(() => props.zones.map((z) => ({ value: z.id, label: `${z.code} · ${z.name}` })))
const preview = computed(
  () => `${form.aisle.toUpperCase() || '?'}-${String(form.rack ?? 0).padStart(2, '0')}-${form.level ?? 0}`,
)

const create = useMutation({
  mutationFn: () =>
    warehousesApi.createLocation(props.warehouseId, { ...form, aisle: form.aisle.trim().toUpperCase() }),
  onSuccess: (loc) => {
    for (const key of ['warehouse-locations', 'warehouse-zones', 'warehouse-map', 'warehouses']) {
      void queryClient.invalidateQueries({ queryKey: [key] })
    }
    notify.success(`Ячейка ${loc.code} добавлена`)
    visible.value = false
  },
  // 409 «Ячейка уже существует» — под полем «Проход» (код ячейки собирается из прохода/стеллажа/яруса)
  onError: (error) => v.setServerError(error, 'aisle') || notify.error(error),
})
</script>

<template>
  <Dialog v-model:visible="visible" modal header="Новая ячейка" class="w-[calc(100vw-2rem)] max-w-lg">
    <form
      id="location-form"
      class="grid grid-cols-2 gap-4 sm:grid-cols-3"
      novalidate
      @submit.prevent="submit"
    >
      <div class="col-span-2 space-y-1.5 sm:col-span-3">
        <label for="loc-zone" class="text-sm font-medium text-heading">Зона</label>
        <Select
          id="loc-zone"
          v-model="form.zoneId"
          :options="zoneOptions"
          option-label="label"
          option-value="value"
          placeholder="Выберите зону"
          fluid
          :invalid="!!v.show('zoneId')"
        />
        <small v-if="v.show('zoneId')" class="text-red-600">{{ v.show('zoneId') }}</small>
      </div>
      <div class="space-y-1.5">
        <label for="loc-aisle" class="text-sm font-medium text-heading">Проход</label>
        <InputText
          id="loc-aisle"
          v-model="form.aisle"
          maxlength="2"
          fluid
          :invalid="!!v.show('aisle')"
          @blur="v.touched.aisle = true"
        />
        <small v-if="v.show('aisle')" class="text-red-600">{{ v.show('aisle') }}</small>
      </div>
      <div class="space-y-1.5">
        <label for="loc-rack" class="text-sm font-medium text-heading">Стеллаж</label>
        <InputNumber
          v-model="form.rack"
          input-id="loc-rack"
          :max="99"
          fluid
          :invalid="!!v.show('rack')"
          @blur="v.touched.rack = true"
        />
        <small v-if="v.show('rack')" class="text-red-600">{{ v.show('rack') }}</small>
      </div>
      <div class="space-y-1.5">
        <label for="loc-level" class="text-sm font-medium text-heading">Ярус</label>
        <InputNumber
          v-model="form.level"
          input-id="loc-level"
          :max="8"
          fluid
          :invalid="!!v.show('level')"
          @blur="v.touched.level = true"
        />
        <small v-if="v.show('level')" class="text-red-600">{{ v.show('level') }}</small>
      </div>
      <div class="col-span-2 space-y-1.5 sm:col-span-3">
        <label for="loc-cap" class="text-sm font-medium text-heading">Вместимость, л</label>
        <InputNumber
          v-model="form.capacityLiters"
          input-id="loc-cap"
          :max="5000"
          fluid
          :invalid="!!v.show('capacityLiters')"
          @blur="v.touched.capacityLiters = true"
        />
        <small v-if="v.show('capacityLiters')" class="text-red-600">{{ v.show('capacityLiters') }}</small>
      </div>
      <p class="col-span-2 text-sm text-secondary sm:col-span-3">
        Код ячейки: <span class="font-mono font-semibold text-heading">{{ preview }}</span
        >. Координаты рассчитаются по проходу и стеллажу.
      </p>
    </form>
    <template #footer>
      <Button label="Отмена" severity="secondary" outlined @click="visible = false" />
      <Button label="Добавить" type="submit" form="location-form" :loading="create.isPending.value" />
    </template>
  </Dialog>
</template>
