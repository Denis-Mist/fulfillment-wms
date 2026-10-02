<script setup lang="ts">
import { computed } from 'vue'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { useConfirm } from 'primevue/useconfirm'
import { Store, Warehouse } from '@lucide/vue'
import type { OrganizationStatus, OrganizationType } from '@/modules/auth/types'
import { organizationsApi } from '@/modules/organizations/api'
import type { AdminOrganization, ModerationAction } from '@/modules/organizations/types'
import { ORGANIZATION_STATUSES, ORGANIZATION_TYPES } from '@/shared/config/statuses'
import { useNotify } from '@/shared/composables/useNotify'
import { useRegistry } from '@/shared/composables/useRegistry'
import { formatDateTime, formatRelative } from '@/shared/lib/format'
import FilterChips from '@/shared/ui/FilterChips.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import RegistryTable from '@/shared/ui/RegistryTable.vue'
import SearchInput from '@/shared/ui/SearchInput.vue'
import StatusTag from '@/shared/ui/StatusTag.vue'
import type { ChipOption } from '@/shared/ui/types'

const queryClient = useQueryClient()
const confirm = useConfirm()
const notify = useNotify()

const registry = useRegistry({
  filters: {
    status: 'pending' as OrganizationStatus | undefined,
    type: undefined as OrganizationType | undefined,
  },
  sortField: 'createdAt',
})
const { state, filters, searchInput } = registry

const { data, isPending } = useQuery({
  queryKey: computed(() => ['admin-organizations', registry.query.value]),
  queryFn: () => organizationsApi.adminList(registry.query.value),
  placeholderData: keepPreviousData,
})

const { data: summary } = useQuery({
  queryKey: computed(() => ['admin-organizations-summary', state.search, filters.type]),
  queryFn: () => organizationsApi.adminSummary({ search: state.search || undefined, type: filters.type }),
  placeholderData: keepPreviousData,
})

const STATUS_ORDER: OrganizationStatus[] = ['pending', 'active', 'rejected', 'blocked']
const chips = computed<ChipOption<OrganizationStatus>[]>(() => [
  ...STATUS_ORDER.map((status) => ({
    value: status,
    label: ORGANIZATION_STATUSES[status].label,
    count: summary.value?.byStatus[status] ?? 0,
  })),
  { value: undefined, label: 'Все', count: summary.value?.total },
])

const typeOptions = Object.entries(ORGANIZATION_TYPES).map(([value, label]) => ({ value, label }))

const moderate = useMutation({
  mutationFn: ({ id, action }: { id: string; action: ModerationAction }) => organizationsApi.moderate(id, action),
  onSuccess: (org) => {
    void queryClient.invalidateQueries({ queryKey: ['admin-organizations'] })
    void queryClient.invalidateQueries({ queryKey: ['admin-organizations-summary'] })
    void queryClient.invalidateQueries({ queryKey: ['notifications'] })
    notify.success(`${org.name}: ${ORGANIZATION_STATUSES[org.status].label.toLowerCase()}`)
  },
  onError: (error) => notify.error(error),
})

const ACTIONS: Record<ModerationAction, { header: string; message: (org: AdminOrganization) => string; label: string; danger: boolean }> = {
  approve: {
    header: 'Одобрить организацию?',
    message: (org) => `${org.name} получит доступ к платформе. Владелец (${org.ownerEmail}) сможет войти в кабинет.`,
    label: 'Одобрить',
    danger: false,
  },
  reject: {
    header: 'Отклонить заявку?',
    message: (org) => `Заявка ${org.name} будет отклонена.`,
    label: 'Отклонить',
    danger: true,
  },
  block: {
    header: 'Заблокировать организацию?',
    message: (org) => `Все пользователи ${org.name} потеряют доступ к платформе.`,
    label: 'Заблокировать',
    danger: true,
  },
  unblock: {
    header: 'Разблокировать организацию?',
    message: (org) => `Пользователи ${org.name} снова смогут работать в системе.`,
    label: 'Разблокировать',
    danger: false,
  },
}

function run(org: AdminOrganization, action: ModerationAction) {
  const meta = ACTIONS[action]
  confirm.require({
    header: meta.header,
    message: meta.message(org),
    acceptProps: { label: meta.label, severity: meta.danger ? 'danger' : undefined },
    rejectProps: { label: 'Отмена', severity: 'secondary', outlined: true },
    accept: () => moderate.mutate({ id: org.id, action }),
  })
}
</script>

<template>
  <div>
    <PageHeader title="Модерация" description="Проверка новых организаций перед открытием доступа к платформе" />

    <div class="mb-4 space-y-3">
      <FilterChips
        :model-value="filters.status"
        :options="chips"
        @update:model-value="registry.setFilter('status', $event)"
      />
      <div class="flex flex-wrap items-center gap-2.5">
        <SearchInput v-model="searchInput" placeholder="Название, ИНН или email владельца" />
        <Select
          :model-value="filters.type"
          :options="typeOptions"
          option-label="label"
          option-value="value"
          placeholder="Все типы"
          show-clear
          class="w-full sm:w-56"
          @update:model-value="registry.setFilter('type', $event ?? undefined)"
        />
      </div>
    </div>

    <RegistryTable
      :items="data?.items ?? []"
      :total="data?.total ?? 0"
      :loading="isPending"
      :first="registry.first.value"
      :rows="state.pageSize"
      :sort-field="state.sortField"
      :sort-order="registry.tableSortOrder.value"
      :empty-title="filters.status === 'pending' ? 'Новых заявок нет' : 'Организаций не найдено'"
      :empty-description="filters.status === 'pending' ? 'Все заявки обработаны.' : undefined"
      @page="registry.onPage"
      @sort="registry.onSort"
    >
      <Column field="name" header="Организация" sortable class="min-w-56">
        <template #body="{ data: org }">
          <div class="flex items-center gap-3">
            <span class="grid size-9 shrink-0 place-items-center rounded-lg bg-surface-100 text-surface-500 dark:bg-surface-800">
              <component :is="org.type === 'fulfillment' ? Warehouse : Store" class="size-[18px]" />
            </span>
            <div>
              <div class="font-semibold text-heading">{{ org.name }}</div>
              <div class="text-xs text-secondary">{{ ORGANIZATION_TYPES[org.type as OrganizationType] }} · ИНН {{ org.inn }}</div>
            </div>
          </div>
        </template>
      </Column>
      <Column field="city" header="Город" sortable />
      <Column header="Владелец" class="min-w-48">
        <template #body="{ data: org }">
          <div class="text-sm text-heading">{{ org.ownerName }}</div>
          <div class="text-xs text-secondary">{{ org.ownerEmail }}</div>
        </template>
      </Column>
      <Column field="createdAt" header="Заявка" sortable class="min-w-36">
        <template #body="{ data: org }">
          <div class="tabular text-sm text-heading">{{ formatDateTime(org.createdAt) }}</div>
          <div class="text-xs text-secondary">{{ formatRelative(org.createdAt) }}</div>
        </template>
      </Column>
      <Column header="Статус">
        <template #body="{ data: org }">
          <StatusTag domain="organization" :status="org.status" />
        </template>
      </Column>
      <Column header="" class="w-px whitespace-nowrap text-right">
        <template #body="{ data: org }">
          <div class="flex justify-end gap-1.5">
            <template v-if="org.status === 'pending'">
              <Button label="Отклонить" size="small" severity="danger" text @click="run(org, 'reject')" />
              <Button label="Одобрить" size="small" @click="run(org, 'approve')" />
            </template>
            <Button v-else-if="org.status === 'active'" label="Заблокировать" size="small" severity="secondary" text @click="run(org, 'block')" />
            <Button v-else-if="org.status === 'blocked'" label="Разблокировать" size="small" outlined @click="run(org, 'unblock')" />
            <Button v-else-if="org.status === 'rejected'" label="Одобрить" size="small" outlined @click="run(org, 'approve')" />
          </div>
        </template>
      </Column>
    </RegistryTable>
  </div>
</template>
