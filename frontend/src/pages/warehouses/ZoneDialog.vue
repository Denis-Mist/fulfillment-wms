<script setup lang="ts">
import { reactive, watch } from 'vue'
import { useMutation, useQueryClient } from '@tanstack/vue-query'
import { warehousesApi } from '@/modules/warehouses/api'
import { ZONE_TYPES } from '@/modules/warehouses/labels'
import type { ZoneInput } from '@/modules/warehouses/types'
import { useNotify } from '@/shared/composables/useNotify'

const props = defineProps<{ warehouseId: string }>()
const visible = defineModel<boolean>('visible', { required: true })
const queryClient = useQueryClient()
const notify = useNotify()

const form = reactive<ZoneInput>({ code: '', name: '', type: 'storage' })
const typeOptions = Object.entries(ZONE_TYPES).map(([value, label]) => ({ value, label }))

watch(visible, (open) => open && Object.assign(form, { code: '', name: '', type: 'storage' }))

const create = useMutation({
  mutationFn: () => warehousesApi.createZone(props.warehouseId, form),
  onSuccess: (zone) => {
    void queryClient.invalidateQueries({ queryKey: ['warehouse-zones'] })
    notify.success(`Зона ${zone.code} создана`)
    visible.value = false
  },
  onError: (error) => notify.error(error),
})
</script>

<template>
  <Dialog v-model:visible="visible" modal header="Новая зона" class="w-[calc(100vw-2rem)] max-w-md">
    <form id="zone-form" class="space-y-4" @submit.prevent="create.mutate()">
      <div class="space-y-1.5">
        <label for="zone-code" class="text-sm font-medium text-heading">Код</label>
        <InputText id="zone-code" v-model="form.code" placeholder="Например, MZ1" maxlength="6" fluid required />
      </div>
      <div class="space-y-1.5">
        <label for="zone-name" class="text-sm font-medium text-heading">Название</label>
        <InputText id="zone-name" v-model="form.name" placeholder="Мезонин" fluid required />
      </div>
      <div class="space-y-1.5">
        <label for="zone-type" class="text-sm font-medium text-heading">Назначение</label>
        <Select id="zone-type" v-model="form.type" :options="typeOptions" option-label="label" option-value="value" fluid />
      </div>
    </form>
    <template #footer>
      <Button label="Отмена" severity="secondary" outlined @click="visible = false" />
      <Button label="Создать" type="submit" form="zone-form" :loading="create.isPending.value" />
    </template>
  </Dialog>
</template>
