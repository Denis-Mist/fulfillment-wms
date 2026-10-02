<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { z } from 'zod'
import { ArrowLeft, Check, Store, Warehouse } from '@lucide/vue'
import type { Marketplace, OrganizationType } from '@/modules/auth/types'
import { errorMessage } from '@/shared/api/http'
import { useSessionStore } from '@/stores/session'

const session = useSessionStore()
const router = useRouter()

const step = ref(0)
const steps = ['Тип организации', 'Организация', 'Аккаунт']
const submitting = ref(false)
const submitError = ref('')

const form = reactive({
  organizationType: undefined as OrganizationType | undefined,
  name: '',
  inn: '',
  city: '',
  warehousesCount: 1 as number | null,
  marketplaces: ['wb', 'ozon'] as Marketplace[],
  fullName: '',
  email: '',
  phone: '',
  password: '',
  agree: false,
})
const errors = ref<Record<string, string>>({})

watch(
  () => ({ ...form }),
  (next, prev) => {
    for (const key of Object.keys(errors.value)) {
      if (next[key as keyof typeof form] !== prev[key as keyof typeof form]) delete errors.value[key]
    }
  },
)

const typeOptions = [
  {
    value: 'fulfillment' as const,
    icon: Warehouse,
    title: 'Я фулфилмент-центр',
    text: 'Храню и обрабатываю товары клиентов, у меня один или несколько складов',
  },
  {
    value: 'seller' as const,
    icon: Store,
    title: 'Я селлер',
    text: 'Продаю на маркетплейсах и хочу передать склад и логистику фулфилменту',
  },
]

const marketplaceOptions = [
  { value: 'wb', label: 'Wildberries' },
  { value: 'ozon', label: 'Ozon' },
  { value: 'yandex', label: 'Яндекс Маркет' },
]

const stepSchemas = [
  z.object({ organizationType: z.enum(['fulfillment', 'seller'], { error: 'Выберите тип организации' }) }),
  z.object({
    name: z.string().trim().min(2, 'Укажите название организации'),
    inn: z.string().regex(/^(\d{10}|\d{12})$/, 'ИНН — 10 или 12 цифр'),
    city: z.string().trim().min(2, 'Укажите город'),
  }),
  z.object({
    fullName: z.string().trim().min(3, 'Укажите имя и фамилию'),
    email: z.string().trim().regex(/^[^\s@]+@[^\s@]+\.[^\s@]+$/, 'Некорректный email'),
    phone: z.string().regex(/^\+?[\d\s()-]{10,18}$/, 'Некорректный номер телефона'),
    password: z.string().min(6, 'Минимум 6 символов'),
    agree: z.literal(true, { error: 'Необходимо согласие' }),
  }),
]

function validate(index: number) {
  const result = stepSchemas[index]!.safeParse(form)
  if (result.success) {
    errors.value = {}
    return true
  }
  errors.value = Object.fromEntries(result.error.issues.map((issue) => [String(issue.path[0]), issue.message]))
  return false
}

function next() {
  if (!validate(step.value)) return
  if (step.value < steps.length - 1) step.value++
  else void submit()
}

function back() {
  errors.value = {}
  step.value = Math.max(0, step.value - 1)
}

function selectType(value: OrganizationType) {
  form.organizationType = value
  errors.value = {}
  step.value = 1
}

const isFulfillment = computed(() => form.organizationType === 'fulfillment')

async function submit() {
  submitting.value = true
  submitError.value = ''
  try {
    await session.register({
      organizationType: form.organizationType!,
      organization: {
        name: form.name,
        inn: form.inn,
        city: form.city,
        warehousesCount: isFulfillment.value ? (form.warehousesCount ?? 1) : undefined,
        marketplaces: isFulfillment.value ? undefined : form.marketplaces,
      },
      user: { fullName: form.fullName, email: form.email, phone: form.phone, password: form.password },
    })
    await router.replace({ name: 'pending' })
  } catch (e) {
    submitError.value = errorMessage(e)
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div>
    <h1 class="text-[28px] font-semibold tracking-tight text-heading">Регистрация</h1>
    <p class="mt-1.5 text-secondary">Три коротких шага — и заявка отправлена на проверку</p>

    <ol class="mt-7 flex items-center gap-2">
      <li v-for="(label, index) in steps" :key="label" class="flex flex-1 items-center gap-2">
        <span
          class="grid size-6 shrink-0 place-items-center rounded-full text-xs font-semibold transition-colors"
          :class="
            index < step
              ? 'bg-primary text-primary-contrast'
              : index === step
                ? 'bg-primary-50 text-primary ring-2 ring-primary dark:bg-primary-500/15'
                : 'bg-surface-100 text-surface-400 dark:bg-surface-800'
          "
        >
          <Check v-if="index < step" class="size-3.5" />
          <template v-else>{{ index + 1 }}</template>
        </span>
        <span class="hidden truncate text-xs font-medium sm:block" :class="index <= step ? 'text-heading' : 'text-secondary'">
          {{ label }}
        </span>
        <span v-if="index < steps.length - 1" class="h-px flex-1 bg-surface-200 dark:bg-surface-800" />
      </li>
    </ol>

    <form class="mt-7" @submit.prevent="next">
      <div v-if="step === 0" class="space-y-3">
        <button
          v-for="option in typeOptions"
          :key="option.value"
          type="button"
          class="flex w-full items-start gap-4 rounded-2xl border-2 p-4 text-left transition-all hover:border-primary-300"
          :class="
            form.organizationType === option.value
              ? 'border-primary bg-primary-50/50 dark:bg-primary-500/10'
              : 'border-surface-200 dark:border-surface-800'
          "
          @click="selectType(option.value)"
        >
          <span class="grid size-11 shrink-0 place-items-center rounded-xl bg-primary-50 text-primary dark:bg-primary-500/15">
            <component :is="option.icon" class="size-5" />
          </span>
          <span>
            <span class="block font-semibold text-heading">{{ option.title }}</span>
            <span class="mt-0.5 block text-sm text-secondary">{{ option.text }}</span>
          </span>
        </button>
        <small v-if="errors.organizationType" class="text-rose-600">{{ errors.organizationType }}</small>
      </div>

      <div v-else-if="step === 1" class="space-y-4">
        <div class="space-y-1.5">
          <label class="text-sm font-medium text-heading" for="org-name">
            {{ isFulfillment ? 'Название фулфилмент-центра' : 'Название магазина или компании' }}
          </label>
          <InputText id="org-name" v-model="form.name" :invalid="!!errors.name" fluid placeholder="ООО «Пример»" />
          <small v-if="errors.name" class="text-rose-600">{{ errors.name }}</small>
        </div>
        <div class="grid grid-cols-2 gap-3">
          <div class="space-y-1.5">
            <label class="text-sm font-medium text-heading" for="org-inn">ИНН</label>
            <InputText id="org-inn" v-model="form.inn" :invalid="!!errors.inn" fluid placeholder="7700000000" />
            <small v-if="errors.inn" class="text-rose-600">{{ errors.inn }}</small>
          </div>
          <div class="space-y-1.5">
            <label class="text-sm font-medium text-heading" for="org-city">Город</label>
            <InputText id="org-city" v-model="form.city" :invalid="!!errors.city" fluid placeholder="Москва" />
            <small v-if="errors.city" class="text-rose-600">{{ errors.city }}</small>
          </div>
        </div>
        <div v-if="isFulfillment" class="space-y-1.5">
          <label class="text-sm font-medium text-heading" for="org-wh">Количество складов</label>
          <InputNumber v-model="form.warehousesCount" input-id="org-wh" :min="1" :max="50" show-buttons fluid />
          <small class="text-secondary">Склады и ячейки можно будет настроить после проверки</small>
        </div>
        <div v-else class="space-y-1.5">
          <span class="text-sm font-medium text-heading">Где вы продаёте</span>
          <SelectButton
            v-model="form.marketplaces"
            :options="marketplaceOptions"
            option-label="label"
            option-value="value"
            multiple
            fluid
          />
        </div>
      </div>

      <div v-else class="space-y-4">
        <div class="space-y-1.5">
          <label class="text-sm font-medium text-heading" for="u-name">Имя и фамилия</label>
          <InputText id="u-name" v-model="form.fullName" :invalid="!!errors.fullName" fluid placeholder="Иван Иванов" />
          <small v-if="errors.fullName" class="text-rose-600">{{ errors.fullName }}</small>
        </div>
        <div class="grid gap-3 sm:grid-cols-2">
          <div class="space-y-1.5">
            <label class="text-sm font-medium text-heading" for="u-email">Email</label>
            <InputText id="u-email" v-model="form.email" type="email" :invalid="!!errors.email" fluid placeholder="you@company.ru" />
            <small v-if="errors.email" class="text-rose-600">{{ errors.email }}</small>
          </div>
          <div class="space-y-1.5">
            <label class="text-sm font-medium text-heading" for="u-phone">Телефон</label>
            <InputText id="u-phone" v-model="form.phone" :invalid="!!errors.phone" fluid placeholder="+7 900 000-00-00" />
            <small v-if="errors.phone" class="text-rose-600">{{ errors.phone }}</small>
          </div>
        </div>
        <div class="space-y-1.5">
          <label class="text-sm font-medium text-heading" for="u-password">Пароль</label>
          <Password
            v-model="form.password"
            input-id="u-password"
            toggle-mask
            :invalid="!!errors.password"
            fluid
            prompt-label="Придумайте пароль"
            weak-label="Слабый"
            medium-label="Средний"
            strong-label="Надёжный"
          />
          <small v-if="errors.password" class="text-rose-600">{{ errors.password }}</small>
        </div>
        <label class="flex cursor-pointer items-start gap-2.5 text-sm text-secondary">
          <Checkbox v-model="form.agree" binary :invalid="!!errors.agree" class="mt-0.5" />
          Согласен с условиями использования платформы и обработкой персональных данных
        </label>
        <small v-if="errors.agree" class="block text-rose-600">{{ errors.agree }}</small>
        <Message v-if="submitError" severity="error" :closable="false" size="small">{{ submitError }}</Message>
      </div>

      <div v-if="step > 0" class="mt-7 flex items-center gap-3">
        <Button type="button" severity="secondary" outlined @click="back">
          <ArrowLeft class="size-4" />
          Назад
        </Button>
        <Button
          type="submit"
          class="flex-1"
          :label="step === steps.length - 1 ? 'Отправить заявку' : 'Далее'"
          :loading="submitting"
        />
      </div>
    </form>

    <p class="mt-8 text-center text-sm text-secondary">
      Уже зарегистрированы?
      <RouterLink :to="{ name: 'login' }" class="font-semibold text-primary hover:underline">Войти</RouterLink>
    </p>
  </div>
</template>
