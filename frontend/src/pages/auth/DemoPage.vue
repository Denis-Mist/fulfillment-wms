<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useQuery } from '@tanstack/vue-query'
import { ArrowLeft } from '@lucide/vue'
import { authApi, type DemoPersona } from '@/modules/auth/api'
import { DEMO_PERSONA_GROUPS } from '@/modules/auth/demoPersonas'
import { errorMessage } from '@/shared/api/http'
import AppLogo from '@/shared/ui/AppLogo.vue'
import ThemeToggle from '@/layouts/components/ThemeToggle.vue'
import { useSessionStore } from '@/stores/session'

const session = useSessionStore()
const router = useRouter()
const error = ref('')
const loading = ref<DemoPersona | null>(null)

const { data: status, isPending, isError, refetch } = useQuery({
  queryKey: ['auth-demo-status'],
  queryFn: () => authApi.demoStatus(),
  retry: 1,
})

const enabled = computed(() => status.value?.enabled === true)

async function enter(persona: DemoPersona) {
  error.value = ''
  loading.value = persona
  try {
    await session.loginDemo(persona)
    await router.replace('/')
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    loading.value = null
  }
}
</script>

<template>
  <div class="min-h-screen bg-surface-50 dark:bg-surface-950">
    <header
      class="sticky top-0 z-20 flex items-center justify-between border-b border-surface-200/80 bg-white/90 px-4 py-3 backdrop-blur-md sm:px-6 dark:border-surface-800 dark:bg-surface-900/90"
    >
      <AppLogo />
      <ThemeToggle />
    </header>

    <main class="mx-auto w-full max-w-6xl px-4 py-8 sm:px-6 sm:py-12 lg:py-16">
      <RouterLink
        :to="{ name: 'login' }"
        class="mb-6 inline-flex min-h-11 items-center gap-1.5 text-sm font-medium text-secondary hover:text-heading"
      >
        <ArrowLeft class="size-4" />На страницу входа
      </RouterLink>

      <div v-if="isPending" class="space-y-4" aria-busy="true" aria-label="Загрузка демо">
        <Skeleton width="20rem" height="2.2rem" />
        <Skeleton width="28rem" height="1.2rem" />
        <div class="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          <Skeleton v-for="i in 6" :key="i" height="8rem" border-radius="1rem" />
        </div>
      </div>

      <div v-else-if="isError || !enabled" class="mx-auto max-w-lg text-center">
        <h1 class="text-[28px] font-semibold tracking-tight text-heading sm:text-3xl">Демо временно выключено</h1>
        <p class="mt-3 text-secondary">
          Сейчас стенд недоступен для гостевого входа. Попробуйте позже или войдите со своим аккаунтом.
        </p>
        <div class="mt-6 flex flex-col gap-2 sm:flex-row sm:justify-center">
          <Button label="Проверить снова" outlined @click="refetch()" />
          <Button label="На страницу входа" @click="router.push({ name: 'login' })" />
        </div>
      </div>

      <template v-else>
        <h1 class="text-[28px] font-semibold tracking-tight text-heading sm:text-4xl">Попробуйте систему в демо</h1>
        <p class="mt-3 max-w-3xl text-base text-secondary sm:text-lg">
          Выберите роль — войдёте сразу, без пароля. Данные общие и каждую ночь возвращаются к исходному состоянию.
        </p>
        <p class="mt-2 max-w-3xl text-sm text-secondary">
          Стенд общий: около 03:00 по московскому времени данные снова становятся такими, как в учебном наборе.
        </p>

        <Message v-if="error" severity="error" :closable="false" class="mt-6" size="small">{{ error }}</Message>

        <section v-for="group in DEMO_PERSONA_GROUPS" :key="group.label" class="mt-10">
          <h2 class="mb-3 text-sm font-semibold tracking-wider text-secondary uppercase">{{ group.label }}</h2>
          <div class="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
            <button
              v-for="card in group.items"
              :key="card.persona"
              type="button"
              :disabled="!!loading"
              class="group flex min-h-[7.5rem] items-start gap-3 rounded-2xl border border-surface-200 bg-white p-4 text-left transition-all hover:-translate-y-0.5 hover:border-primary-300 hover:shadow-md hover:shadow-primary-500/10 disabled:opacity-60 dark:border-surface-800 dark:bg-surface-900 dark:hover:border-primary-500/50"
              @click="enter(card.persona)"
            >
              <span
                class="grid size-11 shrink-0 place-items-center rounded-xl bg-primary-50 text-primary-700 dark:bg-primary-500/15 dark:text-primary-300"
              >
                <ProgressSpinner v-if="loading === card.persona" style="width: 20px; height: 20px" stroke-width="6" />
                <component :is="card.icon" v-else class="size-5" />
              </span>
              <span class="min-w-0">
                <span class="block text-base font-semibold text-heading">{{ card.title }}</span>
                <span class="mt-1 block text-sm text-secondary">{{ card.description }}</span>
              </span>
            </button>
          </div>
        </section>
      </template>
    </main>
  </div>
</template>
