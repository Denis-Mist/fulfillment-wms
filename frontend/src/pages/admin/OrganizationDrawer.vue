<script setup lang="ts">
import { computed } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import { adminApi } from '@/modules/admin/api'
import { ROLE_LABELS } from '@/modules/auth/roles'
import { ORGANIZATION_TYPES } from '@/shared/config/statuses'
import { formatDate, formatNumber } from '@/shared/lib/format'
import InfoList from '@/shared/ui/InfoList.vue'
import StatusTag from '@/shared/ui/StatusTag.vue'
import UserAvatar from '@/shared/ui/UserAvatar.vue'

const orgId = defineModel<string | null>('orgId', { default: null })
const visible = computed({
  get: () => !!orgId.value,
  set: (value) => {
    if (!value) orgId.value = null
  },
})

const { data: org, isPending } = useQuery({
  queryKey: computed(() => ['admin-organization', orgId.value]),
  queryFn: () => adminApi.organization(orgId.value!),
  enabled: computed(() => !!orgId.value),
})

const info = computed(() => {
  const o = org.value
  if (!o) return []
  return [
    { label: 'Тип', value: ORGANIZATION_TYPES[o.type] },
    { label: 'ИНН', value: o.inn },
    { label: 'Город', value: o.city },
    { label: 'Зарегистрирована', value: formatDate(o.createdAt) },
    { label: 'Заказов за 30 дней', value: formatNumber(o.orders30d) },
    ...(o.type === 'seller' ? [{ label: 'Товаров в каталоге', value: formatNumber(o.products) }] : []),
  ]
})
</script>

<template>
  <Drawer v-model:visible="visible" position="right" class="!w-full sm:!w-[520px]" :header="org?.name ?? 'Организация'">
    <div v-if="isPending" class="space-y-3"><Skeleton v-for="i in 6" :key="i" height="2rem" /></div>
    <div v-else-if="org" class="space-y-6">
      <StatusTag domain="organization" :status="org.status" size="md" />
      <InfoList :items="info" />

      <section>
        <h3 class="mb-2 text-sm font-semibold text-heading">Пользователи · {{ org.users.length }}</h3>
        <ul class="space-y-2">
          <li v-for="user in org.users" :key="user.id" class="flex items-center gap-3">
            <UserAvatar :name="user.fullName" />
            <div class="min-w-0 flex-1">
              <div class="truncate text-sm font-medium text-heading">{{ user.fullName }}</div>
              <div class="truncate text-xs text-secondary">{{ user.email }}</div>
            </div>
            <span class="shrink-0 text-xs text-secondary">{{ ROLE_LABELS[user.role] }}</span>
          </li>
        </ul>
      </section>

      <section v-if="org.warehouses.length">
        <h3 class="mb-2 text-sm font-semibold text-heading">Склады</h3>
        <ul class="space-y-1.5">
          <li v-for="w in org.warehouses" :key="w.id" class="flex justify-between gap-2 text-sm">
            <span class="text-heading">{{ w.name }}</span>
            <span class="font-mono text-xs text-secondary">{{ w.code }} · {{ w.city }}</span>
          </li>
        </ul>
      </section>

      <section>
        <h3 class="mb-2 text-sm font-semibold text-heading">
          {{ org.type === 'fulfillment' ? 'Договоры с клиентами' : 'Договоры с фулфилментами' }} · {{ org.partners.length }}
        </h3>
        <p v-if="!org.partners.length" class="text-sm text-secondary">Договоров нет</p>
        <ul v-else class="space-y-1.5">
          <li v-for="p in org.partners" :key="p.id" class="flex justify-between gap-2 text-sm">
            <span class="truncate text-heading">{{ p.name }}</span>
            <span class="shrink-0 text-xs text-secondary">с {{ formatDate(p.startedAt) }} · {{ p.warehouses }} скл.</span>
          </li>
        </ul>
      </section>
    </div>
  </Drawer>
</template>
