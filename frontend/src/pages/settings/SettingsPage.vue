<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { MailPlus, UserX } from '@lucide/vue'
import { useConfirm } from 'primevue/useconfirm'
import { FC_OFFICE_ROLES, ROLE_LABELS, SELLER_ROLES, WORKER_ROLES } from '@/modules/auth/roles'
import type { Role } from '@/modules/auth/types'
import { settingsApi } from '@/modules/settings/api'
import type { CardRules, NotificationPrefs, OrgProfile, TeamMember } from '@/modules/settings/types'
import { useCabinet } from '@/shared/composables/useCabinet'
import { useNotify } from '@/shared/composables/useNotify'
import { formatRelative } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'
import PageHeader from '@/shared/ui/PageHeader.vue'
import RegistryTable from '@/shared/ui/RegistryTable.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'
import ToneBadge from '@/pages/stock/ToneBadge.vue'

const session = useSessionStore()
const { isFulfillment } = useCabinet()
const notify = useNotify()
const confirm = useConfirm()
const qc = useQueryClient()

const tab = ref('profile')
const { data: settings, isPending } = useQuery({
  queryKey: computed(() => ['settings', session.organizationId]),
  queryFn: () => settingsApi.get(),
})
const profile = ref<OrgProfile | null>(null)
const rules = ref<CardRules | null>(null)
const prefs = ref<NotificationPrefs | null>(null)
watch(
  settings,
  (s) => {
    profile.value = s ? { ...s.profile } : null
    rules.value = s ? { ...s.cardRules } : null
    prefs.value = s ? { ...s.notifications } : null
  },
  { immediate: true },
)
const canAdmin = computed(
  () => !!session.role && ['fc_owner', 'fc_manager', 'seller_owner'].includes(session.role),
)
const canRules = computed(() => !!session.role && ['fc_owner', 'fc_manager'].includes(session.role))

const onSaved = (title: string) => ({
  onSuccess: () => {
    notify.success(title)
    void qc.invalidateQueries({ queryKey: ['settings'] })
  },
  onError: (e: unknown) => notify.error(e),
})
const saveProfile = useMutation({
  mutationFn: () => settingsApi.saveProfile(profile.value!),
  ...onSaved('Реквизиты сохранены'),
})
const saveRules = useMutation({
  mutationFn: () => settingsApi.saveCardRules(rules.value!),
  ...onSaved('Требования к карточке сохранены'),
})
const savePrefs = useMutation({
  mutationFn: (p: NotificationPrefs) => settingsApi.saveNotifications(p),
  ...onSaved('Настройки уведомлений сохранены'),
})

const PROFILE_FIELDS: {
  key: keyof OrgProfile
  label: string
  group: 'org' | 'bank'
  readonly?: boolean
  mono?: boolean
}[] = [
  { key: 'name', label: 'Название', group: 'org' },
  { key: 'inn', label: 'ИНН', group: 'org', readonly: true, mono: true },
  { key: 'kpp', label: 'КПП', group: 'org', mono: true },
  { key: 'ogrn', label: 'ОГРН', group: 'org', mono: true },
  { key: 'legalAddress', label: 'Юридический адрес', group: 'org' },
  { key: 'contactEmail', label: 'Email для документов', group: 'org' },
  { key: 'contactPhone', label: 'Телефон', group: 'org' },
  { key: 'bankName', label: 'Банк', group: 'bank' },
  { key: 'bik', label: 'БИК', group: 'bank', mono: true },
  { key: 'account', label: 'Расчётный счёт', group: 'bank', mono: true },
  { key: 'corrAccount', label: 'Корр. счёт', group: 'bank', mono: true },
]
const RULES: { key: keyof CardRules; label: string; hint: string }[] = [
  { key: 'requireBarcode', label: 'Штрихкод EAN-13', hint: 'Без штрихкода товар нельзя принять на склад' },
  { key: 'requireDimensions', label: 'Габариты', hint: 'Нужны для расчёта хранения и подбора ячейки' },
  { key: 'requireWeight', label: 'Вес', hint: 'Используется при упаковке и расчёте отгрузки' },
  { key: 'requirePhoto', label: 'Фото товара', hint: 'Помогает комплектовщику при отборе' },
  {
    key: 'requireExpiry',
    label: 'Срок годности для продуктов',
    hint: 'Включает учёт партий и FEFO при отборе',
  },
  {
    key: 'requireMarking',
    label: 'Признак маркировки «Честный знак»',
    hint: 'Обязателен для маркируемых категорий',
  },
  {
    key: 'autoApprove',
    label: 'Автоматически одобрять карточки',
    hint: 'Без ручной модерации менеджером ФЦ',
  },
]
const PREFS: { key: keyof NotificationPrefs; label: string; fc?: boolean }[] = [
  { key: 'email', label: 'Дублировать уведомления на email' },
  { key: 'inboundStatus', label: 'Статусы поставок и приёмок' },
  { key: 'lowStock', label: 'Заканчивается товар' },
  { key: 'slaRisk', label: 'Риск нарушения SLA отгрузки', fc: true },
  { key: 'billing', label: 'Новые акты и задолженность' },
]

const { data: team, isPending: teamPending } = useQuery({
  queryKey: computed(() => ['team', session.organizationId]),
  queryFn: () => settingsApi.team(),
})
const roleOptions = computed(() =>
  (isFulfillment.value ? [...FC_OFFICE_ROLES, ...WORKER_ROLES] : SELLER_ROLES).map((r) => ({
    value: r,
    label: ROLE_LABELS[r],
  })),
)
const inviteEmail = ref('')
const inviteRole = ref<Role | null>(null)
const invite = useMutation({
  mutationFn: () => settingsApi.invite({ email: inviteEmail.value, role: inviteRole.value! }),
  onSuccess: (m) => {
    notify.success('Приглашение отправлено', m.email)
    inviteEmail.value = ''
    void qc.invalidateQueries({ queryKey: ['team'] })
  },
  onError: (e: unknown) => notify.error(e),
})
const revoke = useMutation({
  mutationFn: (id: string) => settingsApi.revoke(id),
  onSuccess: () => {
    notify.success('Приглашение отозвано')
    void qc.invalidateQueries({ queryKey: ['team'] })
  },
  onError: (e: unknown) => notify.error(e),
})
function askRevoke(m: TeamMember) {
  confirm.require({
    header: 'Отозвать приглашение?',
    message: `Ссылка для ${m.email} перестанет работать.`,
    acceptProps: { label: 'Отозвать', severity: 'danger' },
    rejectProps: { label: 'Отмена', severity: 'secondary', outlined: true },
    accept: () => revoke.mutate(m.id),
  })
}
const invited = computed(() => (team.value ?? []).filter((m) => m.status === 'invited').length)
</script>

<template>
  <div>
    <PageHeader
      title="Настройки"
      :description="
        isFulfillment
          ? 'Реквизиты фулфилмента, требования к карточкам товаров, команда и уведомления'
          : 'Реквизиты компании, команда и уведомления'
      "
    />
    <Tabs v-model:value="tab">
      <TabList>
        <Tab value="profile">Организация</Tab>
        <Tab v-if="isFulfillment" value="catalog">Карточка товара</Tab>
        <Tab value="team"
          >Команда
          <span v-if="invited" class="ml-1.5 rounded-full bg-primary-500 px-1.5 py-0.5 text-xs text-white">{{
            invited
          }}</span></Tab
        >
        <Tab value="notifications">Уведомления</Tab>
      </TabList>
      <TabPanels class="!px-0">
        <TabPanel value="profile">
          <div v-if="isPending || !profile" class="grid gap-4 lg:grid-cols-2">
            <Skeleton height="22rem" /><Skeleton height="16rem" />
          </div>
          <form v-else class="space-y-4" @submit.prevent="saveProfile.mutate()">
            <div class="grid gap-4 lg:grid-cols-2">
              <SectionCard
                v-for="g in ['org', 'bank'] as const"
                :key="g"
                :title="g === 'org' ? 'Юридические данные' : 'Банковские реквизиты'"
                :description="g === 'bank' ? 'Подставляются в акты и счета' : undefined"
              >
                <div class="grid gap-3 sm:grid-cols-2">
                  <div
                    v-for="f in PROFILE_FIELDS.filter((x) => x.group === g)"
                    :key="f.key"
                    :class="['name', 'legalAddress', 'bankName'].includes(f.key) && 'sm:col-span-2'"
                  >
                    <label :for="`f-${f.key}`" class="mb-1 block text-sm font-medium">{{ f.label }}</label>
                    <InputText
                      :id="`f-${f.key}`"
                      v-model="profile[f.key]"
                      :disabled="f.readonly || !canAdmin"
                      :class="f.mono && 'font-mono'"
                      class="w-full"
                    />
                  </div>
                </div>
              </SectionCard>
            </div>
            <div v-if="canAdmin" class="flex gap-2">
              <Button type="submit" label="Сохранить реквизиты" :loading="saveProfile.isPending.value" />
              <Button
                label="Сбросить"
                text
                severity="secondary"
                @click="profile = settings ? { ...settings.profile } : null"
              />
            </div>
            <Message v-else severity="secondary" size="small"
              >Изменять реквизиты может владелец организации</Message
            >
          </form>
        </TabPanel>

        <TabPanel v-if="isFulfillment" value="catalog">
          <SectionCard
            title="Обязательные поля карточки"
            description="Применяются при модерации товаров клиентов и при приёмке"
          >
            <template v-if="canRules" #actions>
              <Button
                label="Сохранить"
                size="small"
                :loading="saveRules.isPending.value"
                @click="saveRules.mutate()"
              />
            </template>
            <ul v-if="rules" class="divide-y divide-default">
              <li v-for="r in RULES" :key="r.key" class="flex items-center justify-between gap-4 py-3">
                <label :for="`r-${r.key}`" class="min-w-0">
                  <span class="block text-sm font-medium text-heading">{{ r.label }}</span>
                  <span class="text-xs text-secondary">{{ r.hint }}</span>
                </label>
                <ToggleSwitch :input-id="`r-${r.key}`" v-model="rules[r.key]" :disabled="!canRules" />
              </li>
            </ul>
          </SectionCard>
        </TabPanel>

        <TabPanel value="team">
          <SectionCard v-if="canAdmin" title="Пригласить сотрудника" class="mb-4">
            <form class="flex flex-wrap gap-2.5" @submit.prevent="invite.mutate()">
              <InputText
                v-model="inviteEmail"
                type="email"
                placeholder="email@company.ru"
                aria-label="Email"
                class="w-full sm:w-72"
              />
              <Select
                v-model="inviteRole"
                :options="roleOptions"
                option-label="label"
                option-value="value"
                placeholder="Роль"
                class="w-full sm:w-60"
              />
              <Button type="submit" :disabled="!inviteEmail || !inviteRole" :loading="invite.isPending.value">
                <MailPlus class="size-4" /> Пригласить
              </Button>
            </form>
          </SectionCard>
          <RegistryTable
            :items="team ?? []"
            :total="team?.length ?? 0"
            :loading="teamPending"
            :first="0"
            :rows="200"
            empty-title="В команде пока никого"
          >
            <Column header="Пользователь" class="min-w-56">
              <template #body="{ data: m }">
                <div class="font-medium text-heading">{{ m.fullName || m.email }}</div>
                <div v-if="m.fullName" class="text-xs text-secondary">{{ m.email }}</div>
              </template>
            </Column>
            <Column header="Роль" class="min-w-44">
              <template #body="{ data: m }"
                ><span class="whitespace-nowrap">{{ ROLE_LABELS[m.role as Role] }}</span></template
              >
            </Column>
            <Column header="Статус" class="min-w-40">
              <template #body="{ data: m }">
                <ToneBadge
                  :tone="m.status === 'active' ? 'success' : 'warn'"
                  :label="m.status === 'active' ? 'Активен' : `Приглашён ${formatRelative(m.invitedAt)}`"
                />
              </template>
            </Column>
            <Column v-if="canAdmin" class="w-16">
              <template #body="{ data: m }">
                <Button
                  v-if="m.status === 'invited'"
                  text
                  severity="danger"
                  size="small"
                  aria-label="Отозвать приглашение"
                  @click="askRevoke(m)"
                >
                  <UserX class="size-4" />
                </Button>
              </template>
            </Column>
          </RegistryTable>
        </TabPanel>

        <TabPanel value="notifications">
          <SectionCard title="Уведомления" description="Что показывать в колокольчике и отправлять на почту">
            <ul v-if="prefs" class="divide-y divide-default">
              <li
                v-for="p in PREFS.filter((x) => !x.fc || isFulfillment)"
                :key="p.key"
                class="flex items-center justify-between gap-4 py-3"
              >
                <label :for="`n-${p.key}`" class="text-sm text-heading">{{ p.label }}</label>
                <ToggleSwitch
                  :input-id="`n-${p.key}`"
                  v-model="prefs[p.key]"
                  @update:model-value="savePrefs.mutate({ ...prefs!, [p.key]: $event })"
                />
              </li>
            </ul>
          </SectionCard>
        </TabPanel>
      </TabPanels>
    </Tabs>
  </div>
</template>
