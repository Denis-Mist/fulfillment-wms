<script setup lang="ts">
import { computed, reactive, watch } from 'vue'
import { useMutation, useQueryClient } from '@tanstack/vue-query'
import { warehousesApi } from '@/modules/warehouses/api'
import type { LocationInput, Zone } from '@/modules/warehouses/types'
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
watch(visible, (open) => open && Object.assign(form, blank()))

const zoneOptions = computed(() => props.zones.map((z) => ({ value: z.id, label: `${z.code} · ${z.name}` })))
const preview = computed(() => `${form.aisle.toUpperCase() || '?'}-${String(form.rack ?? 0).padStart(2, '0')}-${form.level ?? 0}`)

const create = useMutation({
  mutationFn: () => warehousesApi.createLocation(props.warehouseId, form),
  onSuccess: (loc) => {
    for (const key of ['warehouse-locations', 'warehouse-zones', 'warehouse-map', 'warehouses']) {
      void queryClient.invalidateQueries({ queryKey: [key] })
    }
    notify.success(`Ячейка ${loc.code} добавлена`)
    visible.value = false
  },
  onError: (error) => notify.error(error),
})
</script>

<template>
  <Dialog v-model:visible="visible" modal header="Новая ячейка" class="w-[calc(100vw-2rem)] max-w-lg">
    <form id="location-form" class="grid grid-cols-2 gap-4 sm:grid-cols-3" @submit.prevent="create.mutate()">
      <div class="col-span-2 space-y-1.5 sm:col-span-3">
        <label for="loc-zone" class="text-sm font-medium text-heading">Зона</label>
        <Select id="loc-zone" v-model="form.zoneId" :options="zoneOptions" option-label="label" option-value="value" fluid />
      </div>
      <div class="space-y-1.5">
        <label for="loc-aisle" class="text-sm font-medium text-heading">Проход</label>
        <InputText id="loc-aisle" v-model="form.aisle" maxlength="2" fluid />
      </div>
      <div class="space-y-1.5">
        <label for="loc-rack" class="text-sm font-medium text-heading">Стеллаж</label>
        <InputNumber v-model="form.rack" input-id="loc-rack" :min="1" :max="99" fluid />
      </div>
      <div class="space-y-1.5">
        <label for="loc-level" class="text-sm font-medium text-heading">Ярус</label>
        <InputNumber v-model="form.level" input-id="loc-level" :min="1" :max="8" fluid />
      </div>
      <div class="col-span-2 space-y-1.5 sm:col-span-3">
        <label for="loc-cap" class="text-sm font-medium text-heading">Вместимость, л</label>
        <InputNumber v-model="form.capacityLiters" input-id="loc-cap" :min="1" :max="5000" fluid />
      </div>
      <p class="col-span-2 text-sm text-secondary sm:col-span-3">
        Код ячейки: <span class="font-mono font-semibold text-heading">{{ preview }}</span>. Координаты рассчитаются по проходу и стеллажу.
      </p>
    </form>
    <template #footer>
      <Button label="Отмена" severity="secondary" outlined @click="visible = false" />
      <Button label="Добавить" type="submit" form="location-form" :loading="create.isPending.value" />
    </template>
  </Dialog>
</template>
