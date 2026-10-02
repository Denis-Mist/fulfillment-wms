<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Lock, Mail } from '@lucide/vue'
import { errorMessage } from '@/shared/api/http'
import { useSessionStore } from '@/stores/session'
import { DEMO_ACCOUNTS, DEMO_PASSWORD, type DemoAccount } from './demoAccounts'

const session = useSessionStore()
const router = useRouter()
const route = useRoute()

const email = ref('')
const password = ref('')
const error = ref('')
const loading = ref(false)
const loadingDemo = ref<string | null>(null)
const mocksEnabled = import.meta.env.VITE_USE_MOCKS === 'true'

async function signIn(credentials = { email: email.value, password: password.value }) {
  error.value = ''
  if (!credentials.email || !credentials.password) {
    error.value = 'Введите email и пароль'
    return
  }
  try {
    await session.login(credentials)
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
    await router.replace(redirect)
  } catch (e) {
    error.value = errorMessage(e)
  }
}

async function submit() {
  loading.value = true
  await signIn()
  loading.value = false
}

async function signInDemo(account: DemoAccount) {
  loadingDemo.value = account.email
  await signIn({ email: account.email, password: DEMO_PASSWORD })
  loadingDemo.value = null
}
</script>

<template>
  <div>
    <h1 class="text-[28px] font-semibold tracking-tight text-heading">С возвращением</h1>
    <p class="mt-1.5 text-secondary">Войдите, чтобы продолжить работу</p>

    <form class="mt-8 space-y-4" @submit.prevent="submit">
      <div class="space-y-1.5">
        <label for="email" class="text-sm font-medium text-heading">Email</label>
        <IconField>
          <InputIcon><Mail class="size-4" /></InputIcon>
          <InputText id="email" v-model="email" type="email" autocomplete="email" placeholder="you@company.ru" fluid />
        </IconField>
      </div>
      <div class="space-y-1.5">
        <label for="password" class="text-sm font-medium text-heading">Пароль</label>
        <IconField>
          <InputIcon class="z-10"><Lock class="size-4" /></InputIcon>
          <Password
            v-model="password"
            input-id="password"
            :feedback="false"
            toggle-mask
            placeholder="Ваш пароль"
            fluid
            :input-props="{ autocomplete: 'current-password' }"
            :pt="{ pcInputText: { root: { class: '!pl-10' } } }"
          />
        </IconField>
      </div>

      <Message v-if="error" severity="error" :closable="false" size="small">{{ error }}</Message>

      <Button type="submit" label="Войти" :loading="loading" fluid size="large" />
    </form>

    <template v-if="mocksEnabled">
      <div class="my-7 flex items-center gap-3 text-xs text-surface-400">
        <span class="h-px flex-1 bg-surface-200 dark:bg-surface-800" />
        или войдите как демо-пользователь
        <span class="h-px flex-1 bg-surface-200 dark:bg-surface-800" />
      </div>
      <div class="grid grid-cols-2 gap-2.5">
        <button
          v-for="account in DEMO_ACCOUNTS"
          :key="account.email"
          type="button"
          :disabled="!!loadingDemo"
          class="group flex items-center gap-3 rounded-xl border border-surface-200 p-3 text-left transition-all hover:-translate-y-0.5 hover:border-primary-300 hover:shadow-md hover:shadow-primary-500/10 disabled:opacity-60 dark:border-surface-800 dark:hover:border-primary-500/50"
          @click="signInDemo(account)"
        >
          <span
            class="grid size-9 shrink-0 place-items-center rounded-lg bg-surface-100 text-surface-500 transition-colors group-hover:bg-primary-50 group-hover:text-primary dark:bg-surface-800 dark:group-hover:bg-primary-500/15"
          >
            <ProgressSpinner v-if="loadingDemo === account.email" style="width: 16px; height: 16px" stroke-width="6" />
            <component :is="account.icon" v-else class="size-[18px]" />
          </span>
          <span class="min-w-0">
            <span class="block truncate text-sm font-semibold text-heading">{{ account.title }}</span>
            <span class="block truncate text-xs text-secondary">{{ account.description }}</span>
          </span>
        </button>
      </div>
    </template>

    <p class="mt-8 text-center text-sm text-secondary">
      Нет аккаунта?
      <RouterLink :to="{ name: 'register' }" class="font-semibold text-primary hover:underline">
        Зарегистрировать организацию
      </RouterLink>
    </p>
  </div>
</template>
