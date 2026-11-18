<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useConfirm } from 'primevue/useconfirm'
import { ArrowLeft } from '@lucide/vue'
import type { DemoPersona } from '@/modules/auth/api'
import { DEMO_PERSONA_GROUPS } from '@/modules/auth/demoPersonas'
import { errorMessage } from '@/shared/api/http'
import AppLogo from '@/shared/ui/AppLogo.vue'
import ThemeToggle from '@/layouts/components/ThemeToggle.vue'
import { useSessionStore } from '@/stores/session'

const session = useSessionStore()
const router = useRouter()
const route = useRoute()
const confirm = useConfirm()
const error = ref('')
const loading = ref<DemoPersona | null>(null)

const expired = computed(() => route.query.expired === '1' || session.demoExpired)

async function enter(persona: DemoPersona) {
  error.value = ''
  loading.value = persona
  try {
    await session.loginDemo(persona)
    await router.replace('/')
  } catch (e) {
    const failedStart = e instanceof Error && e.message === 'Не удалось запустить демо в этом браузере'
    error.value = failedStart ? e.message : errorMessage(e)
  } finally {
    loading.value = null
  }
}

function resetData() {
  confirm.require({
    header: 'Сбросить демо-данные?',
    message: 'Учебная база в этом браузере вернётся к исходному состоянию.',
    acceptProps: { label: 'Сбросить', severity: 'danger' },
    rejectProps: { label: 'Отмена', severity: 'secondary', outlined: true },
    accept: async () => {
      await session.resetDemo()
    },
  })
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

      <h1 class="text-[28px] font-semibold tracking-tight text-heading sm:text-4xl">Попробуйте систему в демо</h1>
      <p class="mt-3 max-w-3xl text-base text-secondary sm:text-lg">
        Выберите роль — войдёте сразу, без пароля. Данные хранятся только в этом браузере: это ваша личная песочница,
        она не затрагивает настоящую WMS и не видна другим посетителям.
      </p>
      <p class="mt-2 max-w-3xl text-sm text-secondary">
        Можно свободно пробовать операции. Сброс возвращает учебную базу к исходному состоянию.
      </p>

      <div class="mt-5">
        <Button label="Сбросить демо-данные" severity="secondary" outlined size="small" data-testid="demo-reset-page" @click="resetData" />
      </div>

      <Message v-if="expired && !error" severity="warn" :closable="false" class="mt-6" size="small">
        Сессия демо прервалась. Выберите роль, чтобы войти снова.
      </Message>
      <Message v-if="error" severity="error" :closable="false" class="mt-6" size="small" data-testid="demo-start-error">
        {{ error }}
        <template v-if="error.includes('Не удалось запустить')">
          <div class="mt-2">
            <Button label="На страницу входа" size="small" @click="router.push({ name: 'login' })" />
          </div>
        </template>
      </Message>

      <section v-for="group in DEMO_PERSONA_GROUPS" :key="group.label" class="mt-10">
        <h2 class="mb-3 text-sm font-semibold tracking-wider text-secondary uppercase">{{ group.label }}</h2>
        <div class="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          <button
            v-for="card in group.items"
            :key="card.persona"
            type="button"
            :data-testid="`demo-persona-${card.persona}`"
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
    </main>
  </div>
</template>
