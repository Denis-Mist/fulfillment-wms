<script setup lang="ts">
import { watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import { useTheme } from '@/shared/composables/useTheme'

useTheme()

const route = useRoute()
const router = useRouter()
const toast = useToast()

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
  <RouterView />
  <Toast position="top-right" />
  <ConfirmDialog />
</template>
