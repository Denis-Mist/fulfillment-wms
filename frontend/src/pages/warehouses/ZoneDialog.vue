<script setup lang="ts">
import { reactive, watch } from 'vue'
import { useMutation, useQueryClient } from '@tanstack/vue-query'
import { warehousesApi } from '@/modules/warehouses/api'
import { ZONE_TYPES } from '@/modules/warehouses/labels'
import type { ZoneInput } from '@/modules/warehouses/types'
import { z } from 'zod'
import { useFormValidation } from '@/shared/composables/useFormValidation'
import { useNotify } from '@/shared/composables/useNotify'

const props = defineProps<{ warehouseId: string }>()
const visible = defineModel<boolean>('visible', { required: true })
const queryClient = useQueryClient()
const notify = useNotify()

const form = reactive<ZoneInput>({ code: '', name: '', type: 'storage' })
const typeOptions = Object.entries(ZONE_TYPES).map(([value, label]) => ({ value, label }))

const schema = z.object({
  code: z
    .string()
    .trim()
    .min(1, 'Укажите код зоны')
    .refine((v) => /^[A-Z0-9]{2,6}$/i.test(v), 'Код: 2–6 латинских букв или цифр, например MZ1'),
  name: z.string().trim().min(1, 'Укажите название зоны').max(60, 'Не длиннее 60 символов'),
  type: z.string().min(1, 'Выберите назначение'),
})
const v = useFormValidation(schema, form)

watch(visible, (open) => {
  if (!open) return
  Object.assign(form, { code: '', name: '', type: 'storage' })
  v.reset()
})

function submit() {
  if (v.validate()) create.mutate()
}

const create = useMutation({
  mutationFn: () =>
    warehousesApi.createZone(props.warehouseId, { ...form, code: form.code.trim().toUpperCase() }),
  onSuccess: (zone) => {
    void queryClient.invalidateQueries({ queryKey: ['warehouse-zones'] })
    notify.success(`Зона ${zone.code} создана`)
    visible.value = false
  },
  // 409 (дубль) и 400 по коду показываем под полем «Код»
  onError: (error) => v.setServerError(error, 'code') || notify.error(error),
})
</script>

<template>
  <Dialog v-model:visible="visible" modal header="Новая зона" class="w-[calc(100vw-2rem)] max-w-md">
    <form id="zone-form" class="space-y-4" novalidate @submit.prevent="submit">
      <div class="space-y-1.5">
        <label for="zone-code" class="text-sm font-medium text-heading">Код</label>
        <InputText
          id="zone-code"
          v-model="form.code"
          placeholder="Например, MZ1"
          maxlength="6"
          fluid
          :invalid="!!v.show('code')"
          aria-describedby="zone-code-err"
          @blur="v.touched.code = true"
        />
        <small v-if="v.show('code')" id="zone-code-err" class="text-red-600">{{ v.show('code') }}</small>
      </div>
      <div class="space-y-1.5">
        <label for="zone-name" class="text-sm font-medium text-heading">Название</label>
        <InputText
          id="zone-name"
          v-model="form.name"
          placeholder="Мезонин"
          fluid
          :invalid="!!v.show('name')"
          aria-describedby="zone-name-err"
          @blur="v.touched.name = true"
        />
        <small v-if="v.show('name')" id="zone-name-err" class="text-red-600">{{ v.show('name') }}</small>
      </div>
      <div class="space-y-1.5">
        <label for="zone-type" class="text-sm font-medium text-heading">Назначение</label>
        <Select
          id="zone-type"
          v-model="form.type"
          :options="typeOptions"
          option-label="label"
          option-value="value"
          fluid
        />
      </div>
    </form>
    <template #footer>
      <Button label="Отмена" severity="secondary" outlined @click="visible = false" />
      <Button label="Создать" type="submit" form="zone-form" :loading="create.isPending.value" />
    </template>
  </Dialog>
</template>
