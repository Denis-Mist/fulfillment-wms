<script setup lang="ts">
import { computed, ref } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { useConfirm } from 'primevue/useconfirm'
import { AlertTriangle, Boxes, KeyRound, Link2, PackageCheck, Plug, RefreshCw, Unplug } from '@lucide/vue'
import { integrationsApi } from '@/modules/integrations/api'
import type {
  Integration,
  IntegrationChannel,
  IntegrationLogEntry,
  IntegrationSettingsInput,
} from '@/modules/integrations/types'
import { useNotify } from '@/shared/composables/useNotify'
import { SALES_CHANNELS, type Tone } from '@/shared/config/statuses'
import { formatDate, formatDateTime, formatRelative } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import ChannelBadge from '@/shared/ui/ChannelBadge.vue'
import EmptyState from '@/shared/ui/EmptyState.vue'
import PageHeader from '@/shared/ui/PageHeader.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'
import ToneBadge from '@/pages/stock/ToneBadge.vue'

const session = useSessionStore()
const notify = useNotify()
const confirm = useConfirm()
const qc = useQueryClient()
const canManage = computed(() => session.role === 'seller_owner')

const { data: items, isPending } = useQuery({
  queryKey: computed(() => ['integrations', session.organizationId]),
  queryFn: () => integrationsApi.list(),
})

const STATUS: Record<Integration['status'], { label: string; tone: Tone }> = {
  connected: { label: 'Подключено', tone: 'success' },
  error: { label: 'Ошибка', tone: 'danger' },
  disconnected: { label: 'Не подключено', tone: 'neutral' },
}
const HINT: Record<IntegrationChannel, string> = {
  wb: 'Ключ API из раздела «Доступ к API» личного кабинета продавца WB (категории «Маркетплейс» и «Контент»)',
  ozon: 'Client ID и API-ключ из раздела «Настройки → Seller API» личного кабинета Ozon',
}
const INTERVALS = [5, 15, 30, 60].map((v) => ({ value: v, label: `${v} мин` }))

const done = (title: string) => ({
  onSuccess: () => {
    notify.success(title)
    void qc.invalidateQueries({ queryKey: ['integrations'] })
  },
  onError: (e: unknown) => notify.error(e),
})
const sync = useMutation({
  mutationFn: (ch: IntegrationChannel) => integrationsApi.sync(ch),
  ...done('Синхронизация выполнена'),
})
const save = useMutation({
  mutationFn: (p: { ch: IntegrationChannel; input: IntegrationSettingsInput }) =>
    integrationsApi.save(p.ch, p.input),
  ...done('Настройки обмена сохранены'),
})
const disconnect = useMutation({
  mutationFn: (ch: IntegrationChannel) => integrationsApi.disconnect(ch),
  ...done('Кабинет отключён'),
})
function patch(it: Integration, change: Partial<IntegrationSettingsInput>) {
  save.mutate({
    ch: it.channel,
    input: {
      autoImportFbs: it.autoImportFbs,
      pushStock: it.pushStock,
      syncIntervalMin: it.syncIntervalMin,
      ...change,
    },
  })
}
function askDisconnect(it: Integration) {
  confirm.require({
    header: `Отключить ${SALES_CHANNELS[it.channel].name}?`,
    message: 'Заказы FBS перестанут загружаться автоматически, ключ API будет удалён.',
    acceptProps: { label: 'Отключить', severity: 'danger' },
    rejectProps: { label: 'Отмена', severity: 'secondary', text: true },
    accept: () => disconnect.mutate(it.channel),
  })
}

/* Подключение по ключу */
const connectFor = ref<IntegrationChannel | null>(null)
const apiKey = ref('')
const clientId = ref('')
const keyError = computed(() => {
  if (apiKey.value.trim().length < 20) return 'Ключ API — не менее 20 символов'
  if (connectFor.value === 'ozon' && !/^\d{4,10}$/.test(clientId.value.trim()))
    return 'Client ID — число из 4–10 цифр'
  return null
})
function openConnect(ch: IntegrationChannel) {
  connectFor.value = ch
  apiKey.value = ''
  clientId.value = ''
}
const connect = useMutation({
  mutationFn: () =>
    integrationsApi.connect(connectFor.value!, {
      apiKey: apiKey.value.trim(),
      clientId: connectFor.value === 'ozon' ? clientId.value.trim() : undefined,
    }),
  onSuccess: (it) => {
    notify.success(`${SALES_CHANNELS[it.channel].name} подключён`, 'Первая загрузка заказов уже выполнена')
    connectFor.value = null
    void qc.invalidateQueries({ queryKey: ['integrations'] })
  },
  onError: (e) => notify.error(e),
})

const LOG_ICON: Record<IntegrationLogEntry['kind'], typeof Plug> = {
  orders: PackageCheck,
  stock: Boxes,
  connect: Link2,
  error: AlertTriangle,
}
const log = computed(() =>
  (items.value ?? [])
    .flatMap((it) => it.log.map((e) => ({ ...e, channel: it.channel })))
    .sort((a, b) => b.at.localeCompare(a.at))
    .slice(0, 20),
)
</script>

<template>
  <div>
    <PageHeader
      title="Интеграции"
      description="Подключение кабинетов маркетплейсов по API-ключу: автоматическая загрузка заказов FBS и выгрузка остатков"
    />

    <div v-if="isPending" class="grid gap-4 lg:grid-cols-2">
      <Skeleton v-for="i in 2" :key="i" height="20rem" border-radius="0.75rem" />
    </div>
    <div v-else class="grid gap-4 lg:grid-cols-2">
      <SectionCard v-for="it in items" :key="it.channel">
        <div class="flex flex-col gap-4">
          <div class="flex items-start justify-between gap-3">
            <div class="flex min-w-0 items-center gap-3">
              <span
                class="grid size-11 shrink-0 place-items-center rounded-xl text-sm font-bold text-white"
                :style="{ backgroundColor: SALES_CHANNELS[it.channel].color }"
                >{{ SALES_CHANNELS[it.channel].short }}</span
              >
              <div class="min-w-0">
                <h3 class="truncate text-base font-semibold text-heading">
                  {{ SALES_CHANNELS[it.channel].name }}
                </h3>
                <p class="text-sm text-secondary">
                  {{
                    it.status === 'disconnected'
                      ? 'Кабинет не подключён'
                      : `Синхронизация ${formatRelative(it.lastSyncAt!)}`
                  }}
                </p>
              </div>
            </div>
            <ToneBadge :tone="STATUS[it.status].tone" :label="STATUS[it.status].label" />
          </div>

          <template v-if="it.status !== 'disconnected'">
            <dl class="grid grid-cols-2 gap-3 rounded-lg bg-surface-50 p-3 text-sm dark:bg-surface-900">
              <div>
                <dt class="text-xs text-secondary">Ключ API</dt>
                <dd class="font-mono font-medium text-heading">{{ it.keyMask }}</dd>
              </div>
              <div v-if="it.clientId">
                <dt class="text-xs text-secondary">Client ID</dt>
                <dd class="font-mono font-medium text-heading">{{ it.clientId }}</dd>
              </div>
              <div>
                <dt class="text-xs text-secondary">Подключён</dt>
                <dd class="font-medium text-heading">{{ formatDate(it.connectedAt!) }}</dd>
              </div>
            </dl>

            <div class="flex flex-col divide-y divide-surface-100 dark:divide-surface-800">
              <label class="flex items-center justify-between gap-4 py-2.5">
                <span class="min-w-0">
                  <span class="block text-sm font-medium text-heading">Автозагрузка заказов FBS</span>
                  <span class="block text-xs text-secondary">Новые заказы сразу попадают на сборку в ФЦ</span>
                </span>
                <ToggleSwitch
                  :model-value="it.autoImportFbs"
                  :disabled="!canManage || save.isPending.value"
                  @update:model-value="patch(it, { autoImportFbs: $event })"
                />
              </label>
              <label class="flex items-center justify-between gap-4 py-2.5">
                <span class="min-w-0">
                  <span class="block text-sm font-medium text-heading">Выгрузка остатков</span>
                  <span class="block text-xs text-secondary"
                    >Доступный остаток со складов ФЦ передаётся на маркетплейс</span
                  >
                </span>
                <ToggleSwitch
                  :model-value="it.pushStock"
                  :disabled="!canManage || save.isPending.value"
                  @update:model-value="patch(it, { pushStock: $event })"
                />
              </label>
              <div class="flex flex-wrap items-center justify-between gap-3 py-2.5">
                <span class="text-sm font-medium text-heading">Интервал обмена</span>
                <Select
                  :model-value="it.syncIntervalMin"
                  :options="INTERVALS"
                  option-label="label"
                  option-value="value"
                  :disabled="!canManage"
                  class="w-32"
                  aria-label="Интервал обмена"
                  @update:model-value="patch(it, { syncIntervalMin: $event })"
                />
              </div>
            </div>

            <div class="flex flex-wrap gap-2">
              <Button
                label="Синхронизировать"
                size="small"
                :loading="sync.isPending.value && sync.variables.value === it.channel"
                @click="sync.mutate(it.channel)"
              >
                <template #icon><RefreshCw class="mr-2 size-4" /></template>
              </Button>
              <Button
                v-if="canManage"
                label="Отключить"
                size="small"
                severity="danger"
                text
                @click="askDisconnect(it)"
              >
                <template #icon><Unplug class="mr-2 size-4" /></template>
              </Button>
            </div>
          </template>

          <template v-else>
            <p class="text-sm text-secondary">{{ HINT[it.channel] }}</p>
            <div>
              <Button v-if="canManage" label="Подключить" size="small" @click="openConnect(it.channel)">
                <template #icon><Plug class="mr-2 size-4" /></template>
              </Button>
              <p v-else class="text-sm text-secondary">Подключить кабинет может владелец организации.</p>
            </div>
          </template>
        </div>
      </SectionCard>
    </div>

    <SectionCard
      class="mt-5"
      title="Журнал обмена"
      description="Последние события по всем подключённым кабинетам"
    >
      <EmptyState v-if="!isPending && !log.length" compact :icon="Plug" title="Событий пока нет" />
      <ul v-else class="divide-y divide-surface-100 dark:divide-surface-800">
        <li v-for="e in log" :key="e.id" class="flex items-center gap-3 py-2.5">
          <component
            :is="LOG_ICON[e.kind]"
            class="size-4 shrink-0"
            :class="e.kind === 'error' ? 'text-rose-600' : 'text-surface-400'"
          />
          <ChannelBadge :channel="e.channel" />
          <span
            class="min-w-0 flex-1 break-words text-sm"
            :class="e.kind === 'error' ? 'text-rose-600' : 'text-heading'"
            >{{ e.message }}</span
          >
          <span class="shrink-0 text-xs text-secondary" :title="formatDateTime(e.at)">{{
            formatRelative(e.at)
          }}</span>
        </li>
      </ul>
    </SectionCard>

    <Dialog
      :visible="!!connectFor"
      modal
      :header="`Подключение ${connectFor ? SALES_CHANNELS[connectFor].name : ''}`"
      class="w-[min(30rem,calc(100vw-2rem))]"
      @update:visible="!$event && (connectFor = null)"
    >
      <div v-if="connectFor" class="flex flex-col gap-4">
        <p class="flex gap-2 text-sm text-secondary">
          <KeyRound class="mt-0.5 size-4 shrink-0" />{{ HINT[connectFor] }}
        </p>
        <label v-if="connectFor === 'ozon'" class="flex flex-col gap-1.5 text-sm">
          <span class="font-medium text-heading">Client ID</span>
          <InputText v-model="clientId" inputmode="numeric" class="w-full font-mono" />
        </label>
        <label class="flex flex-col gap-1.5 text-sm">
          <span class="font-medium text-heading">Ключ API</span>
          <InputText v-model="apiKey" type="password" autocomplete="off" class="w-full font-mono" />
        </label>
        <Message v-if="keyError && apiKey" severity="warn" size="small" variant="simple">{{
          keyError
        }}</Message>
      </div>
      <template #footer>
        <Button label="Отмена" severity="secondary" text @click="connectFor = null" />
        <Button
          label="Проверить и подключить"
          :disabled="!!keyError"
          :loading="connect.isPending.value"
          @click="connect.mutate()"
        />
      </template>
    </Dialog>
  </div>
</template>
