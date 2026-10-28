<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { ROLE_LABELS, WORKER_ROLES, FC_OFFICE_ROLES } from '@/modules/auth/roles'
import type { Role } from '@/modules/auth/types'
import { SKILLS } from '@/modules/staff/labels'
import { staffApi } from '@/modules/staff/api'
import type { Employee, EmployeeInput, Skill } from '@/modules/staff/types'
import { warehousesApi } from '@/modules/warehouses/api'
import { useNotify } from '@/shared/composables/useNotify'
import { formatDate, formatNumber } from '@/shared/lib/format'
import InfoList from '@/shared/ui/InfoList.vue'

const props = defineProps<{ employee: Employee | null; editable: boolean }>()
const visible = defineModel<boolean>('visible', { required: true })
const notify = useNotify()
const qc = useQueryClient()

const form = ref<EmployeeInput | null>(null)
watch(
  () => props.employee,
  (e) =>
    (form.value = e
      ? {
          role: e.role,
          warehouseId: e.warehouseId,
          zoneIds: [...e.zoneIds],
          skills: [...e.skills],
          active: e.active,
        }
      : null),
  { immediate: true },
)
const { data: warehouses } = useQuery({ queryKey: ['warehouses'], queryFn: () => warehousesApi.list() })
const { data: zones } = useQuery({
  queryKey: computed(() => ['zones', form.value?.warehouseId]),
  queryFn: () => warehousesApi.zones(form.value!.warehouseId),
  enabled: computed(() => !!form.value?.warehouseId && visible.value),
})
const roleOptions = [...FC_OFFICE_ROLES, ...WORKER_ROLES].map((r) => ({ value: r, label: ROLE_LABELS[r] }))
const skillOptions = (Object.keys(SKILLS) as Skill[]).map((s) => ({ value: s, label: SKILLS[s] }))
const zoneOptions = computed(() =>
  (zones.value ?? []).map((z) => ({ value: z.id, label: `${z.code} · ${z.name}` })),
)
const warehouseOptions = computed(() => (warehouses.value ?? []).map((w) => ({ value: w.id, label: w.name })))
watch(
  () => form.value?.warehouseId,
  (wh, old) => {
    if (old && wh !== old && form.value) form.value.zoneIds = []
  },
)

const save = useMutation({
  mutationFn: () => staffApi.update(props.employee!.id, form.value!),
  onSuccess: () => {
    notify.success('Карточка сотрудника сохранена')
    void qc.invalidateQueries({ queryKey: ['staff'] })
    visible.value = false
  },
  onError: (e: unknown) => notify.error(e),
})

const info = computed(() =>
  props.employee
    ? [
        { label: 'Email', value: props.employee.email },
        { label: 'Телефон', value: props.employee.phone },
        { label: 'В компании с', value: formatDate(props.employee.hiredAt) },
        ...(props.employee.norm
          ? [
              {
                label: 'Производительность',
                value: `${formatNumber(props.employee.opsPerHour)} оп/ч при норме ${props.employee.norm}`,
              },
              { label: 'Точность', value: `${props.employee.accuracy}%` },
            ]
          : []),
      ]
    : [],
)
const isWorker = computed(() => !!form.value && WORKER_ROLES.includes(form.value.role as Role))
</script>

<template>
  <Drawer
    v-model:visible="visible"
    position="right"
    class="!w-full sm:!w-[30rem]"
    :header="employee?.fullName ?? 'Сотрудник'"
  >
    <div v-if="employee && form" class="space-y-5">
      <InfoList :items="info" />
      <div class="space-y-4">
        <div>
          <label class="mb-1 block text-sm font-medium">Роль</label>
          <Select
            v-model="form.role"
            :options="roleOptions"
            option-label="label"
            option-value="value"
            :disabled="!editable"
            class="w-full"
          />
        </div>
        <div>
          <label class="mb-1 block text-sm font-medium">Склад</label>
          <Select
            v-model="form.warehouseId"
            :options="warehouseOptions"
            option-label="label"
            option-value="value"
            :disabled="!editable"
            class="w-full"
          />
        </div>
        <div v-if="isWorker">
          <label class="mb-1 block text-sm font-medium">Зоны работы</label>
          <MultiSelect
            v-model="form.zoneIds"
            :options="zoneOptions"
            option-label="label"
            option-value="value"
            display="chip"
            placeholder="Все зоны"
            :disabled="!editable"
            class="w-full"
          />
          <p class="mt-1 text-xs text-secondary">Задания из других зон сотруднику не назначаются</p>
        </div>
        <div>
          <label class="mb-2 block text-sm font-medium">Навыки и допуски</label>
          <div class="grid gap-2 sm:grid-cols-2">
            <label v-for="s in skillOptions" :key="s.value" class="flex items-center gap-2 text-sm">
              <Checkbox v-model="form.skills" :value="s.value" :disabled="!editable" />
              {{ s.label }}
            </label>
          </div>
        </div>
        <label class="flex items-center justify-between gap-3 rounded-lg border border-default p-3">
          <span>
            <span class="block text-sm font-medium text-heading">Доступ активен</span>
            <span class="text-xs text-secondary"
              >Отключённый сотрудник не может войти и получать задания</span
            >
          </span>
          <ToggleSwitch v-model="form.active" :disabled="!editable" />
        </label>
      </div>
      <div v-if="editable" class="flex gap-2">
        <Button label="Сохранить" :loading="save.isPending.value" @click="save.mutate()" />
        <Button label="Отмена" text severity="secondary" @click="visible = false" />
      </div>
    </div>
  </Drawer>
</template>
