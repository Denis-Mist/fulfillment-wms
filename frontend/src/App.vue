<script setup lang="ts">
import { ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import { useTheme } from '@/shared/composables/useTheme'
import { useSessionStore } from '@/stores/session'
import ErrorState from '@/shared/ui/ErrorState.vue'

useTheme()

const route = useRoute()
const router = useRouter()
const toast = useToast()
const session = useSessionStore()
const retrying = ref(false)

// FE-S2-1: /auth/me упал не с 401/403 — вместо белого экрана предлагаем повторить без перезагрузки
async function retrySession() {
  retrying.value = true
  try {
    await session.retryInit()
    if (!session.initError) await router.replace(route.fullPath)
  } finally {
    retrying.value = false
  }
}

function toLogin() {
  session.logout()
  void router.replace({ name: 'login', query: { redirect: route.fullPath } })
}

// Роутер при отказе в доступе добавляет ?denied=<путь> (BUG-06): объясняем, почему открылась главная
watch(
  () => route.query.denied,
  (denied) => {
    if (typeof denied !== 'string' || !denied) return
    const title = router.resolve(denied).meta.title
    toast.add({
      severity: 'warn',
      summary: 'Нет доступа к разделу',
      detail: `${title ? `«${title}»` : denied} недоступен для вашей роли. Обратитесь к владельцу организации.`,
      life: 6000,
    })
    const query = { ...route.query }
    delete query.denied
    void router.replace({ query })
  },
)
</script>

<template>
  <main v-if="session.initError && !session.user" class="grid min-h-dvh place-items-center p-4">
    <div class="w-full max-w-md">
      <ErrorState
        :error="session.initError"
        title="Не удалось загрузить профиль"
        :retrying="retrying"
        @retry="retrySession"
      />
      <div class="mt-3 text-center">
        <Button label="Войти заново" text class="min-h-11" @click="toLogin" />
      </div>
    </div>
  </main>
  <RouterView v-else />
  <Toast position="top-right" />
  <ConfirmDialog />
</template>
