<script setup lang="ts">
import { computed } from 'vue'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { useConfirm } from 'primevue/useconfirm'
import { adminApi } from '@/modules/admin/api'
import type { PlatformUser } from '@/modules/admin/types'
import { ROLE_LABELS } from '@/modules/auth/roles'
import type { Role } from '@/modules/auth/types'
import { useNotify } from '@/shared/composables/useNotify'
import { useRegistry } from '@/shared/composables/useRegistry'
import FilterChips from '@/shared/ui/FilterChips.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import RegistryTable from '@/shared/ui/RegistryTable.vue'
import SearchInput from '@/shared/ui/SearchInput.vue'
import type { ChipOption } from '@/shared/ui/types'
import UserAvatar from '@/shared/ui/UserAvatar.vue'

const queryClient = useQueryClient()
const confirm = useConfirm()
const notify = useNotify()

const registry = useRegistry({
  filters: { state: undefined as 'active' | 'blocked' | undefined, role: undefined as Role | undefined },
  sortField: 'fullName',
  sortOrder: 'asc',
})
const { state, filters, searchInput } = registry

const { data, isPending } = useQuery({
  queryKey: computed(() => ['admin-users', registry.query.value]),
  queryFn: () => adminApi.users(registry.query.value),
  placeholderData: keepPreviousData,
})

const stateChips: ChipOption<'active' | 'blocked'>[] = [
  { value: undefined, label: 'Все' },
  { value: 'active', label: 'Активные' },
  { value: 'blocked', label: 'Заблокированные', danger: true },
]
const roleOptions = (Object.keys(ROLE_LABELS) as Role[]).map((value) => ({ value, label: ROLE_LABELS[value] }))

const toggle = useMutation({
  mutationFn: (user: PlatformUser) => adminApi.setUserBlocked(user.id, !user.blocked),
  onSuccess: (user) => {
    void queryClient.invalidateQueries({ queryKey: ['admin-users'] })
    notify.success(`${user.fullName}: ${user.blocked ? 'доступ закрыт' : 'доступ восстановлен'}`)
  },
  onError: (error) => notify.error(error),
})

const reset = useMutation({
  mutationFn: (user: PlatformUser) => adminApi.resetPassword(user.id),
  onSuccess: (result) => notify.success(`Временный пароль: ${result.temporaryPassword}`),
  onError: (error) => notify.error(error),
})

function askToggle(user: PlatformUser) {
  confirm.require({
    header: user.blocked ? 'Разблокировать пользователя?' : 'Заблокировать пользователя?',
    message: user.blocked
      ? `${user.fullName} снова сможет войти в систему.`
      : `${user.fullName} (${user.email}) потеряет доступ ко всем организациям. Действие попадёт в журнал.`,
    acceptProps: { label: user.blocked ? 'Разблокировать' : 'Заблокировать', severity: user.blocked ? undefined : 'danger' },
    rejectProps: { label: 'Отмена', severity: 'secondary', outlined: true },
    accept: () => toggle.mutate(user),
  })
}
</script>

<template>
  <div>
    <PageHeader title="Пользователи" description="Все учётные записи платформы, их роли в организациях и доступ" />

    <div class="mb-4 space-y-3">
      <FilterChips :model-value="filters.state" :options="stateChips" @update:model-value="registry.setFilter('state', $event)" />
      <div class="flex flex-wrap items-center gap-2.5">
        <SearchInput v-model="searchInput" placeholder="ФИО, email или организация" />
        <Select
          :model-value="filters.role"
          :options="roleOptions"
          option-label="label"
          option-value="value"
          placeholder="Все роли"
          show-clear
          class="w-full sm:w-64"
          @update:model-value="registry.setFilter('role', $event ?? undefined)"
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
      empty-title="Пользователи не найдены"
      @page="registry.onPage"
      @sort="registry.onSort"
    >
      <Column field="fullName" header="Пользователь" sortable class="min-w-56">
        <template #body="{ data: user }">
          <div class="flex items-center gap-3">
            <UserAvatar :name="user.fullName" />
            <div class="min-w-0">
              <div class="font-semibold text-heading" :class="user.blocked && 'line-through opacity-60'">{{ user.fullName }}</div>
              <div class="truncate text-xs text-secondary">{{ user.email }}</div>
            </div>
          </div>
        </template>
      </Column>
      <Column header="Организация и роль" class="min-w-56">
        <template #body="{ data: user }">
          <div v-if="user.isPlatformAdmin" class="text-sm text-heading">{{ ROLE_LABELS.platform_admin }}</div>
          <div v-for="m in user.memberships" :key="m.organizationId">
            <div class="text-sm text-heading">{{ m.organizationName }}</div>
            <div class="text-xs text-secondary">{{ ROLE_LABELS[m.role as Role] }}</div>
          </div>
        </template>
      </Column>
      <Column field="phone" header="Телефон" class="whitespace-nowrap" />
      <Column header="Доступ">
        <template #body="{ data: user }">
          <Tag :value="user.blocked ? 'Заблокирован' : 'Активен'" :severity="user.blocked ? 'danger' : 'success'" />
        </template>
      </Column>
      <Column header="" class="w-px whitespace-nowrap text-right">
        <template #body="{ data: user }">
          <div v-if="!user.isPlatformAdmin" class="flex justify-end gap-1.5">
            <Button label="Сбросить пароль" size="small" text severity="secondary" @click="reset.mutate(user)" />
            <Button
              :label="user.blocked ? 'Разблокировать' : 'Заблокировать'"
              size="small"
              :severity="user.blocked ? undefined : 'danger'"
              outlined
              @click="askToggle(user)"
            />
          </div>
        </template>
      </Column>
    </RegistryTable>
  </div>
</template>
