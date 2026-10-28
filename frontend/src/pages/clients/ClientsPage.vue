<script setup lang="ts">
import { computed, ref } from 'vue'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { refDebounced } from '@vueuse/core'
import { useConfirm } from 'primevue/useconfirm'
import { Building2, Check, Phone, X } from '@lucide/vue'
import { catalogApi } from '@/modules/catalog/api'
import type { ClientListItem, ClientRequest } from '@/modules/catalog/types'
import { warehousesApi } from '@/modules/warehouses/api'
import { SALES_CHANNELS } from '@/shared/config/statuses'
import { useNotify } from '@/shared/composables/useNotify'
import { formatDate, formatNumber, formatRelative } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import EmptyState from '@/shared/ui/EmptyState.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import RegistryTable from '@/shared/ui/RegistryTable.vue'
import SearchInput from '@/shared/ui/SearchInput.vue'
import ClientDrawer from './ClientDrawer.vue'

const session = useSessionStore()
const notify = useNotify()
const confirm = useConfirm()
const queryClient = useQueryClient()

const tab = ref<'clients' | 'requests'>('clients')
const search = ref('')
const debounced = refDebounced(search, 300)

const { data: clients, isPending } = useQuery({
  queryKey: computed(() => ['fc-clients', session.organizationId, debounced.value]),
  queryFn: () => catalogApi.clients({ search: debounced.value || undefined }),
  placeholderData: keepPreviousData,
})
const { data: requests, isPending: requestsPending } = useQuery({
  queryKey: computed(() => ['fc-client-requests', session.organizationId]),
  queryFn: () => catalogApi.requests(),
})
const { data: warehouses } = useQuery({
  queryKey: computed(() => ['warehouses', session.organizationId]),
  queryFn: () => warehousesApi.list(),
  staleTime: 5 * 60_000,
})
const pending = computed(() => (requests.value ?? []).filter((r) => r.status === 'pending'))
const decided = computed(() => (requests.value ?? []).filter((r) => r.status !== 'pending'))

const selectedId = ref<string | null>(null)
const drawer = ref(false)
function openClient(item: ClientListItem) {
  selectedId.value = item.id
  drawer.value = true
}

const marketplaceLabel = (code: string) =>
  (SALES_CHANNELS as Record<string, { label: string }>)[code]?.label ?? code

/* Принятие заявки — выбор складов для договора */
const accepting = ref<ClientRequest | null>(null)
const acceptVisible = ref(false)
const acceptWarehouses = ref<string[]>([])
const warehouseOptions = computed(() => (warehouses.value ?? []).map((w) => ({ value: w.id, label: w.name })))
function startAccept(r: ClientRequest) {
  accepting.value = r
  acceptWarehouses.value = warehouseOptions.value.slice(0, 1).map((w) => w.value)
  acceptVisible.value = true
}

const decide = useMutation({
  mutationFn: (args: { id: string; decision: 'accept' | 'decline'; warehouseIds?: string[] }) =>
    catalogApi.decide(args.id, args.decision, args.warehouseIds),
  onSuccess: (r) => {
    notify.success(
      r.status === 'accepted' ? 'Заявка принята' : 'Заявка отклонена',
      r.status === 'accepted' ? `${r.companyName} получит приглашение и проект договора` : r.companyName,
    )
    acceptVisible.value = false
    void queryClient.invalidateQueries({ queryKey: ['fc-client-requests'] })
  },
  onError: (e) => notify.error(e),
})

function decline(r: ClientRequest) {
  confirm.require({
    header: 'Отклонить заявку?',
    message: `${r.companyName} получит уведомление об отказе.`,
    acceptLabel: 'Отклонить',
    rejectLabel: 'Отмена',
    acceptProps: { severity: 'danger' },
    rejectProps: { severity: 'secondary', text: true },
    accept: () => decide.mutate({ id: r.id, decision: 'decline' }),
  })
}
</script>

<template>
  <div>
    <PageHeader
      title="Клиенты и договоры"
      description="Селлеры на обслуживании, условия договоров и заявки на подключение"
    />

    <Tabs v-model:value="tab">
      <TabList>
        <Tab value="clients"
          >Клиенты <span class="ml-1 text-secondary">{{ clients?.length ?? '' }}</span></Tab
        >
        <Tab value="requests">
          Заявки
          <span
            v-if="pending.length"
            class="ml-1.5 rounded-full bg-primary-500 px-1.5 py-0.5 text-xs text-white"
            >{{ pending.length }}</span
          >
        </Tab>
      </TabList>
      <TabPanels class="!px-0">
        <TabPanel value="clients">
          <div class="mb-4"><SearchInput v-model="search" placeholder="Название или ИНН…" /></div>
          <RegistryTable
            :items="clients ?? []"
            :total="clients?.length ?? 0"
            :loading="isPending"
            :first="0"
            :rows="100"
            clickable
            empty-title="Клиентов не найдено"
            @row-click="openClient"
          >
            <Column header="Клиент" class="min-w-56">
              <template #body="{ data: c }">
                <div class="font-medium text-heading">{{ c.name }}</div>
                <div class="text-xs text-secondary">ИНН {{ c.inn }} · {{ c.city }}</div>
              </template>
            </Column>
            <Column header="Договор" class="min-w-36">
              <template #body="{ data: c }">
                <div class="text-sm text-heading">{{ c.contractNumber }}</div>
                <div class="text-xs text-secondary">с {{ formatDate(c.startedAt) }}</div>
              </template>
            </Column>
            <Column header="Склады" class="min-w-40">
              <template #body="{ data: c }">
                <span class="text-sm">{{
                  c.warehouses.map((w: { name: string }) => w.name).join(', ')
                }}</span>
              </template>
            </Column>
            <Column header="SKU" class="min-w-16">
              <template #body="{ data: c }"
                ><span class="tabular">{{ formatNumber(c.skuCount) }}</span></template
              >
            </Column>
            <Column header="На складе" class="min-w-24">
              <template #body="{ data: c }"
                ><span class="tabular">{{ formatNumber(c.stockUnits) }} шт</span></template
              >
            </Column>
            <Column header="Заказов / 30 дн" class="min-w-28">
              <template #body="{ data: c }"
                ><span class="tabular font-medium text-heading">{{
                  formatNumber(c.orders30d)
                }}</span></template
              >
            </Column>
            <Column header="Каналы" class="min-w-32">
              <template #body="{ data: c }">
                <div class="flex flex-wrap gap-1">
                  <Tag
                    v-for="m in c.marketplaces"
                    :key="m"
                    :value="marketplaceLabel(m)"
                    severity="secondary"
                  />
                </div>
              </template>
            </Column>
          </RegistryTable>
        </TabPanel>

        <TabPanel value="requests">
          <div v-if="requestsPending" class="grid gap-3 md:grid-cols-2">
            <Skeleton v-for="i in 2" :key="i" height="12rem" />
          </div>
          <EmptyState
            v-else-if="!requests?.length"
            :icon="Building2"
            title="Заявок нет"
            description="Новые заявки селлеров из каталога ФЦ появятся здесь"
          />
          <template v-else>
            <div v-if="pending.length" class="mb-6 grid gap-3 md:grid-cols-2">
              <article
                v-for="r in pending"
                :key="r.id"
                class="flex flex-col rounded-xl border border-default bg-surface-0 p-4 dark:bg-surface-900"
              >
                <div class="flex flex-wrap items-start justify-between gap-2">
                  <div class="min-w-0">
                    <h3 class="font-semibold text-heading">{{ r.companyName }}</h3>
                    <div class="text-xs text-secondary">
                      ИНН {{ r.inn }} · {{ r.city }} · {{ formatRelative(r.createdAt) }}
                    </div>
                  </div>
                  <div class="flex flex-wrap gap-1">
                    <Tag
                      v-for="m in r.marketplaces"
                      :key="m"
                      :value="marketplaceLabel(m)"
                      severity="secondary"
                    />
                  </div>
                </div>
                <p class="mt-3 text-sm text-heading">{{ r.message }}</p>
                <div class="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-xs text-secondary">
                  <span>~{{ formatNumber(r.skuEstimate) }} SKU</span>
                  <span>~{{ formatNumber(r.ordersPerDay) }} заказов/день</span>
                  <span class="inline-flex items-center gap-1"
                    ><Phone class="size-3" />{{ r.contactName }}, {{ r.contactPhone }}</span
                  >
                </div>
                <div class="mt-4 flex flex-wrap justify-end gap-2 pt-1">
                  <Button size="small" severity="secondary" text @click="decline(r)"
                    ><X class="size-4" />Отклонить</Button
                  >
                  <Button size="small" @click="startAccept(r)"><Check class="size-4" />Принять</Button>
                </div>
              </article>
            </div>
            <div v-if="decided.length">
              <h3 class="mb-2 text-sm font-semibold text-heading">Рассмотренные</h3>
              <ul class="divide-y divide-default rounded-xl border border-default">
                <li
                  v-for="r in decided"
                  :key="r.id"
                  class="flex flex-wrap items-center justify-between gap-2 px-4 py-3 text-sm"
                >
                  <span class="text-heading">{{ r.companyName }}</span>
                  <span class="flex items-center gap-2">
                    <Tag
                      :value="r.status === 'accepted' ? 'Принята' : 'Отклонена'"
                      :severity="r.status === 'accepted' ? 'success' : 'secondary'"
                    />
                    <span class="text-xs text-secondary">{{
                      r.decidedAt ? formatDate(r.decidedAt) : ''
                    }}</span>
                  </span>
                </li>
              </ul>
            </div>
          </template>
        </TabPanel>
      </TabPanels>
    </Tabs>

    <ClientDrawer v-model:visible="drawer" :client-id="selectedId" />

    <Dialog
      v-model:visible="acceptVisible"
      modal
      header="Принять заявку"
      class="w-[calc(100vw-2rem)] max-w-md"
    >
      <div v-if="accepting" class="space-y-4">
        <p class="text-sm text-secondary">
          Выберите склады для договора с
          <span class="font-medium text-heading">{{ accepting.companyName }}</span
          >. Селлер получит приглашение и проект договора.
        </p>
        <MultiSelect
          v-model="acceptWarehouses"
          :options="warehouseOptions"
          option-label="label"
          option-value="value"
          placeholder="Склады"
          display="chip"
          fluid
        />
        <div class="flex justify-end gap-2">
          <Button label="Отмена" severity="secondary" text @click="acceptVisible = false" />
          <Button
            label="Принять"
            :disabled="!acceptWarehouses.length"
            :loading="decide.isPending.value"
            @click="decide.mutate({ id: accepting.id, decision: 'accept', warehouseIds: acceptWarehouses })"
          />
        </div>
      </div>
    </Dialog>
  </div>
</template>
