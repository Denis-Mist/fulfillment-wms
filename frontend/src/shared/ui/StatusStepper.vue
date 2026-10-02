<script setup lang="ts">
import { Check, X } from '@lucide/vue'
import { formatDateTime } from '@/shared/lib/format'
import type { Step } from './types'

defineProps<{
  steps: Step[]
  /** Индекс последнего достигнутого шага; -1 — процесс прерван */
  current: number
  cancelledLabel?: string
}>()
</script>

<template>
  <div v-if="current < 0" class="flex items-center gap-3 rounded-xl bg-surface-100 p-4 dark:bg-surface-800">
    <span class="grid size-8 place-items-center rounded-full bg-surface-300 text-white dark:bg-surface-600">
      <X class="size-4" />
    </span>
    <span class="text-sm font-medium text-surface-600 dark:text-surface-300">{{ cancelledLabel ?? 'Процесс прерван' }}</span>
  </div>
  <ol v-else class="flex w-full items-start overflow-x-auto pb-1">
    <li v-for="(step, index) in steps" :key="step.key" class="relative flex min-w-24 flex-1 flex-col items-center">
      <div
        v-if="index > 0"
        class="absolute top-4 right-1/2 h-0.5 w-full -translate-y-1/2"
        :class="index <= current ? 'bg-primary' : 'bg-surface-200 dark:bg-surface-700'"
      />
      <span
        class="relative z-10 grid size-8 place-items-center rounded-full text-sm font-semibold transition-colors"
        :class="
          index <= current
            ? 'bg-primary text-primary-contrast'
            : index === current + 1
              ? 'bg-white text-primary ring-4 ring-primary-100 outline-2 outline-primary dark:bg-surface-900 dark:ring-primary-500/20'
              : 'bg-surface-100 text-surface-400 dark:bg-surface-800'
        "
      >
        <Check v-if="index <= current" class="size-4" />
        <template v-else>{{ index + 1 }}</template>
      </span>
      <span class="mt-2 text-center text-xs font-medium" :class="index <= current + 1 ? 'text-heading' : 'text-secondary'">
        {{ step.label }}
      </span>
      <span v-if="step.at && index <= current" class="tabular mt-0.5 text-center text-[11px] text-secondary">
        {{ formatDateTime(step.at) }}
      </span>
      <span v-else-if="index === current + 1" class="mt-0.5 text-center text-[11px] font-medium text-primary">
        следующий шаг
      </span>
    </li>
  </ol>
</template>
