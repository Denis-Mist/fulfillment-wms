<script setup lang="ts">
import { computed, ref } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { Building2, FileCheck2, MapPin, Send, Star, Store, Timer } from '@lucide/vue'
import { partnersApi } from '@/modules/partners/api'
import { CONNECTION_META, FC_SERVICE_LABELS } from '@/modules/partners/labels'
import type { ConnectRequestInput, FulfillmentCard } from '@/modules/partners/types'
import { useNotify } from '@/shared/composables/useNotify'
import { formatDate, formatMoney, formatNumber, formatRelative } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import EmptyState from '@/shared/ui/EmptyState.vue'
import FilterChips from '@/shared/ui/FilterChips.vue'
import KpiCard from '@/shared/ui/KpiCard.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import SearchInput from '@/shared/ui/SearchInput.vue'
import type { ChipOption } from '@/shared/ui/types'
import ToneBadge from '@/pages/stock/ToneBadge.vue'

type Scope = 'mine' | 'available'

const session = useSessionStore()
const notify = useNotify()
const qc = useQueryClient()
const canRequest = computed(() => session.role === 'seller_owner')

const { data: cards, isPending } = useQuery({
  queryKey: computed(() => ['seller-fulfillments', session.organizationId]),
  queryFn: () => partnersApi.list(),
})

const scope = ref<Scope | undefined>()
const search = ref('')
const city = ref<string | null>(null)
const isMine = (c: FulfillmentCard) => c.connection === 'connected' || c.connection === 'pending'

const chips = computed<ChipOption<Scope>[]>(() => [
  { value: undefined, label: 'Все', count: cards.value?.length },
  { value: 'mine', label: 'Мои и заявки', count: cards.value?.filter(isMine).length },
  { value: 'available', label: 'Доступные', count: cards.value?.filter((c) => !isMine(c)).length },
])
const cities = computed(() =>
  [...new Set((cards.value ?? []).flatMap((c) => c.cities))].sort((a, b) => a.localeCompare(b, 'ru')),
)
const visible = computed(() => {
  const q = search.value.trim().toLowerCase()
  return (cards.value ?? []).filter(
    (c) =>
      (!scope.value || (scope.value === 'mine') === isMine(c)) &&
      (!city.value || c.cities.includes(city.value)) &&
      (!q || c.name.toLowerCase().includes(q)),
  )
})

const kpi = computed(() => {
  const list = cards.value ?? []
  return {
    connected: list.filter((c) => c.connection === 'connected').length,
    pending: list.filter((c) => c.connection === 'pending').length,
    warehouses: list
      .filter((c) => c.connection === 'connected')
      .reduce((s, c) => s + (c.contract?.warehouseIds.length ?? 0), 0),
    total: list.length,
  }
})

/* Заявка на подключение */
const MARKETPLACES = [
  { value: 'wb', label: 'Wildberries' },
  { value: 'ozon', label: 'Ozon' },
  { value: 'yandex', label: 'Яндекс Маркет' },
]
const target = ref<FulfillmentCard | null>(null)
const form = ref<ConnectRequestInput>({
  skuEstimate: 50,
  ordersPerDay: 20,
  marketplaces: ['wb'],
  message: '',
})
const formError = computed(() =>
  !form.value.skuEstimate || form.value.skuEstimate < 1
    ? 'Укажите примерный ассортимент'
    : !form.value.marketplaces.length
      ? 'Выберите хотя бы один маркетплейс'
      : null,
)
function openRequest(card: FulfillmentCard) {
  target.value = card
  form.value = { skuEstimate: 50, ordersPerDay: 20, marketplaces: card.marketplaces.slice(0, 1), message: '' }
}
const connect = useMutation({
  mutationFn: () => partnersApi.connect(target.value!.id, form.value),
  onSuccess: (card) => {
    notify.success('Заявка отправлена', `${card.name} рассмотрит её и предложит договор`)
    target.value = null
    void qc.invalidateQueries({ queryKey: ['seller-fulfillments'] })
  },
  onError: (e) => notify.error(e),
})

const warehouseNames = (c: FulfillmentCard) =>
  c.warehouses.filter((w) => c.contract?.warehouseIds.includes(w.id)).map((w) => w.name)
</script>

<template>
  <div>
    <PageHeader
      title="Фулфилмент-центры"
      description="Каталог фулфилмент-центров платформы, заявки на подключение и действующие договоры"
    />

    <div class="mb-5 grid grid-cols-2 gap-3 lg:grid-cols-4">
      <KpiCard
        label="Договоров"
        :icon="FileCheck2"
        tone="success"
        :value="formatNumber(kpi.connected)"
        :loading="isPending"
      />
      <KpiCard
        label="Заявок на рассмотрении"
        :icon="Send"
        tone="warn"
        :value="formatNumber(kpi.pending)"
        :loading="isPending"
      />
      <KpiCard
        label="Доступно складов"
        :icon="Building2"
        tone="info"
        :value="formatNumber(kpi.warehouses)"
        hint="по действующим договорам"
        :loading="isPending"
      />
      <KpiCard label="ФЦ на платформе" :icon="Store" :value="formatNumber(kpi.total)" :loading="isPending" />
    </div>

    <div class="mb-4 flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
      <FilterChips v-model="scope" :options="chips" />
      <div class="flex flex-col gap-2 sm:flex-row">
        <Select
          v-model="city"
          :options="cities"
          placeholder="Все города"
          show-clear
          class="w-full sm:w-48"
          aria-label="Город"
        />
        <SearchInput v-model="search" placeholder="Название ФЦ…" />
      </div>
    </div>

    <div v-if="isPending" class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
      <Skeleton v-for="i in 6" :key="i" height="18rem" border-radius="0.75rem" />
    </div>
    <div v-else-if="!visible.length" class="surface-card">
      <EmptyState :icon="Store" title="Ничего не найдено" description="Измените фильтры или строку поиска" />
    </div>
    <div v-else class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
      <article v-for="c in visible" :key="c.id" class="surface-card flex min-w-0 flex-col gap-4 p-5">
        <div class="flex items-start justify-between gap-3">
          <div class="min-w-0">
            <h3 class="truncate text-base font-semibold text-heading">{{ c.name }}</h3>
            <p class="mt-0.5 flex items-center gap-1 text-sm text-secondary">
              <MapPin class="size-3.5 shrink-0" />
              <span class="truncate">{{ c.cities.join(', ') || c.city }}</span>
            </p>
          </div>
          <span class="inline-flex shrink-0 items-center gap-1 text-sm font-semibold text-amber-600">
            <Star class="size-4 fill-current" />{{ c.rating.toFixed(1) }}
          </span>
        </div>

        <div class="flex flex-wrap items-center gap-2">
          <ToneBadge
            :tone="CONNECTION_META[c.connection].tone"
            :label="CONNECTION_META[c.connection].label"
          />
          <span class="text-xs text-secondary">{{ formatNumber(c.clientsCount) }} клиентов</span>
          <span class="inline-flex items-center gap-1 text-xs text-secondary">
            <Timer class="size-3.5" />SLA {{ c.slaHours }} ч
          </span>
        </div>

        <div class="flex flex-wrap gap-1.5">
          <span
            v-for="s in c.services"
            :key="s"
            class="rounded-md bg-surface-100 px-2 py-0.5 text-xs text-surface-700 dark:bg-surface-800 dark:text-surface-200"
            >{{ FC_SERVICE_LABELS[s] }}</span
          >
        </div>

        <dl class="grid grid-cols-2 gap-x-4 gap-y-2 rounded-lg bg-surface-50 p-3 text-sm dark:bg-surface-900">
          <div>
            <dt class="text-xs text-secondary">Приёмка</dt>
            <dd class="font-medium text-heading">{{ formatMoney(c.rates.receivingPerUnit) }} / шт</dd>
          </div>
          <div>
            <dt class="text-xs text-secondary">Хранение</dt>
            <dd class="font-medium text-heading">{{ formatMoney(c.rates.storagePerM3Day) }} / м³·сут</dd>
          </div>
          <div>
            <dt class="text-xs text-secondary">Сборка заказа</dt>
            <dd class="font-medium text-heading">{{ formatMoney(c.rates.pickingPerOrder) }}</dd>
          </div>
          <div>
            <dt class="text-xs text-secondary">Упаковка</dt>
            <dd class="font-medium text-heading">{{ formatMoney(c.rates.packingPerUnit) }} / шт</dd>
          </div>
        </dl>

        <div class="mt-auto text-sm">
          <template v-if="c.connection === 'connected' && c.contract">
            <p class="text-secondary">
              Договор с {{ formatDate(c.contract.startedAt) }} · склады:
              <span class="text-heading">{{ warehouseNames(c).join(', ') || '—' }}</span>
            </p>
          </template>
          <template v-else-if="c.connection === 'pending'">
            <p class="text-secondary">Заявка отправлена {{ formatRelative(c.requestedAt!) }}</p>
          </template>
          <template v-else>
            <p class="mb-3 text-secondary">
              {{ c.warehouses.length }} {{ c.warehouses.length === 1 ? 'склад' : 'склада(ов)' }},
              {{ formatNumber(c.warehouses.reduce((s, w) => s + w.areaSqm, 0)) }} м²
            </p>
            <Button
              v-if="canRequest"
              :label="c.connection === 'declined' ? 'Подать заявку повторно' : 'Подать заявку'"
              size="small"
              class="w-full"
              @click="openRequest(c)"
            >
              <template #icon><Send class="mr-2 size-4" /></template>
            </Button>
          </template>
        </div>
      </article>
    </div>

    <Dialog
      :visible="!!target"
      modal
      :header="`Заявка в ${target?.name ?? ''}`"
      class="w-[min(32rem,calc(100vw-2rem))]"
      @update:visible="!$event && (target = null)"
    >
      <div class="flex flex-col gap-4">
        <p class="text-sm text-secondary">
          Фулфилмент-центр получит реквизиты вашей организации и контакт владельца кабинета.
        </p>
        <div class="grid gap-4 sm:grid-cols-2">
          <label class="flex flex-col gap-1.5 text-sm">
            <span class="font-medium text-heading">Ассортимент, SKU</span>
            <InputNumber v-model="form.skuEstimate" :min="1" :max="100000" input-class="w-full" />
          </label>
          <label class="flex flex-col gap-1.5 text-sm">
            <span class="font-medium text-heading">Заказов в день</span>
            <InputNumber v-model="form.ordersPerDay" :min="0" :max="100000" input-class="w-full" />
          </label>
        </div>
        <label class="flex flex-col gap-1.5 text-sm">
          <span class="font-medium text-heading">Маркетплейсы</span>
          <MultiSelect
            v-model="form.marketplaces"
            :options="MARKETPLACES"
            option-label="label"
            option-value="value"
            display="chip"
            placeholder="Выберите"
            class="w-full"
          />
        </label>
        <label class="flex flex-col gap-1.5 text-sm">
          <span class="font-medium text-heading">Комментарий</span>
          <Textarea
            v-model="form.message"
            rows="3"
            auto-resize
            placeholder="Категории товаров, особые условия…"
          />
        </label>
        <Message v-if="formError" severity="warn" size="small" variant="simple">{{ formError }}</Message>
      </div>
      <template #footer>
        <Button label="Отмена" severity="secondary" text @click="target = null" />
        <Button
          label="Отправить заявку"
          :disabled="!!formError"
          :loading="connect.isPending.value"
          @click="connect.mutate()"
        />
      </template>
    </Dialog>
  </div>
</template>
