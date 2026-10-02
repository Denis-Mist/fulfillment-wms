<script setup lang="ts" generic="T extends string">
import { formatNumber } from '@/shared/lib/format'
import type { ChipOption } from './types'

defineProps<{ options: ChipOption<T>[] }>()
const model = defineModel<T | undefined>()
</script>

<template>
  <div class="no-scrollbar -mx-1 flex gap-1.5 overflow-x-auto px-1 py-0.5">
    <button
      v-for="option in options"
      :key="option.value ?? 'all'"
      type="button"
      class="inline-flex shrink-0 items-center gap-2 rounded-full border px-3.5 py-1.5 text-sm font-medium transition-all"
      :class="
        model === option.value
          ? 'border-primary bg-primary text-primary-contrast shadow-sm shadow-primary/30'
          : option.danger && option.count
            ? 'border-rose-200 bg-rose-50 text-rose-700 hover:border-rose-300 dark:border-rose-500/30 dark:bg-rose-500/10 dark:text-rose-300'
            : 'border-surface-200 bg-white text-surface-600 hover:border-surface-300 hover:text-surface-900 dark:border-surface-700 dark:bg-surface-900 dark:text-surface-300 dark:hover:text-surface-0'
      "
      @click="model = option.value"
    >
      {{ option.label }}
      <span
        v-if="option.count !== undefined"
        class="tabular rounded-full px-1.5 text-xs"
        :class="model === option.value ? 'bg-white/20' : 'bg-surface-100 dark:bg-surface-800'"
      >
        {{ formatNumber(option.count) }}
      </span>
    </button>
  </div>
</template>
