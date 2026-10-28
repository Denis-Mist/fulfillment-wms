<script setup lang="ts">
import { computed, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { Building2, RotateCw, Warehouse } from '@lucide/vue'
import { CABINET_HOME, cabinetForRole } from '@/modules/auth/roles'
import { warehousesApi } from '@/modules/warehouses/api'
import { ORGANIZATION_TYPES } from '@/shared/config/statuses'
import { useSessionStore } from '@/stores/session'

const session = useSessionStore()
const router = useRouter()

const memberships = computed(() => session.user?.memberships ?? [])
const showWarehouses = computed(() => session.cabinet === 'fc')

const {
  data: warehouses,
  isError,
  isFetching,
  refetch,
} = useQuery({
  meta: { shell: true },
  queryKey: computed(() => ['warehouses', session.organizationId, 'switcher']),
  queryFn: () => warehousesApi.list(),
  enabled: showWarehouses,
  staleTime: 5 * 60_000,
})

const warehouseOptions = computed(() => [
  { value: '', label: 'Все склады', short: 'Все', hint: `${warehouses.value?.length ?? 0} складов` },
  ...(warehouses.value ?? []).map((w) => ({
    value: w.id,
    label: w.name,
    short: w.code,
    hint: `${w.code} · ${w.city}`,
  })),
])

watch(warehouses, (list) => {
  if (list && session.warehouseId && !list.some((w) => w.id === session.warehouseId)) session.setWarehouse('')
})

const organizationOptions = computed(() =>
  memberships.value.map((m) => ({
    value: m.organizationId,
    label: m.organizationName,
    hint: ORGANIZATION_TYPES[m.organizationType],
  })),
)

function switchOrganization(id: string) {
  session.setOrganization(id)
  const membership = memberships.value.find((m) => m.organizationId === id)
  if (membership) void router.push({ name: CABINET_HOME[cabinetForRole(membership.role)] })
}
</script>

<template>
  <div class="flex min-w-0 items-center gap-2">
    <Select
      v-if="memberships.length > 1"
      :model-value="session.organizationId"
      :options="organizationOptions"
      option-label="label"
      option-value="value"
      class="hidden !rounded-xl md:flex md:w-52"
      @update:model-value="switchOrganization"
    >
      <template #value="{ value }">
        <span class="flex items-center gap-2 truncate">
          <Building2 class="size-4 shrink-0 text-surface-400" />
          <span class="truncate">{{ organizationOptions.find((o) => o.value === value)?.label }}</span>
        </span>
      </template>
      <template #option="{ option }">
        <div>
          <div class="text-sm font-medium">{{ option.label }}</div>
          <div class="text-xs text-secondary">{{ option.hint }}</div>
        </div>
      </template>
    </Select>

    <Button
      v-if="showWarehouses && isError"
      v-tooltip.bottom="'Не удалось загрузить склады. Нажмите, чтобы повторить'"
      severity="secondary"
      outlined
      class="shrink-0 !gap-2 !rounded-xl whitespace-nowrap"
      aria-label="Не удалось загрузить склады, повторить"
      :loading="isFetching"
      @click="refetch()"
    >
      <Warehouse class="size-4 text-red-500" />
      <span class="hidden text-sm sm:inline">Склады недоступны</span>
      <RotateCw class="size-4" />
    </Button>

    <Select
      v-else-if="showWarehouses"
      :model-value="session.warehouseId"
      :options="warehouseOptions"
      option-label="label"
      option-value="value"
      class="w-[7.5rem] !rounded-xl sm:w-48"
      :pt="{ label: { class: '!pr-0 !pl-2.5 sm:!px-3' }, dropdown: { class: '!w-8 sm:!w-10' } }"
      :loading="!warehouses"
      :aria-label="`Склад: ${warehouseOptions.find((o) => o.value === session.warehouseId)?.label ?? 'Все склады'}`"
      @update:model-value="session.setWarehouse"
    >
      <template #value="{ value }">
        <span class="flex items-center gap-1.5 truncate sm:gap-2">
          <Warehouse class="size-4 shrink-0 text-surface-400" />
          <span class="truncate font-medium sm:hidden">{{
            warehouseOptions.find((o) => o.value === value)?.short ?? 'Все'
          }}</span>
          <span class="hidden truncate sm:inline">{{
            warehouseOptions.find((o) => o.value === value)?.label ?? 'Все склады'
          }}</span>
        </span>
      </template>
      <template #option="{ option }">
        <div>
          <div class="text-sm font-medium">{{ option.label }}</div>
          <div class="text-xs text-secondary">{{ option.hint }}</div>
        </div>
      </template>
    </Select>
  </div>
</template>
