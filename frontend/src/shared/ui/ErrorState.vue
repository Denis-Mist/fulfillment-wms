<script setup lang="ts">
import { computed } from 'vue'
import { CloudAlert, RotateCw } from '@lucide/vue'
import { ApiError } from '@/shared/api/http'

const props = withDefaults(
  defineProps<{ error?: unknown; title?: string; compact?: boolean; retrying?: boolean }>(),
  { title: 'Не удалось загрузить данные', compact: false, retrying: false },
)
defineEmits<{ retry: [] }>()

const detail = computed(() => {
  const e = props.error
  if (e instanceof ApiError) return `Сервер ответил ошибкой ${e.status}${e.message ? `: ${e.message}` : ''}.`
  if (e instanceof Error && e.message) return e.message
  return 'Проверьте подключение к сети и попробуйте ещё раз.'
})
</script>

<template>
  <div
    role="alert"
    class="flex flex-col items-center justify-center text-center"
    :class="compact ? 'py-8' : 'surface-card py-16'"
    data-testid="error-state"
  >
    <div
      class="mb-4 grid size-12 place-items-center rounded-2xl bg-red-50 text-red-500 dark:bg-red-500/10 dark:text-red-400"
    >
      <CloudAlert class="size-6" />
    </div>
    <div class="text-base font-semibold text-heading">{{ title }}</div>
    <p class="mt-1 max-w-sm text-sm text-secondary">{{ detail }}</p>
    <Button class="mt-5 min-h-11" label="Повторить" :loading="retrying" @click="$emit('retry')">
      <template #icon><RotateCw class="size-4" /></template>
    </Button>
  </div>
</template>
