<script setup lang="ts">
import type { Component } from 'vue'
import { Inbox } from '@lucide/vue'

withDefaults(defineProps<{ icon?: Component; title: string; description?: string; compact?: boolean }>(), {
  icon: () => Inbox,
  compact: false,
})
</script>

<template>
  <div class="flex flex-col items-center justify-center text-center" :class="compact ? 'py-8' : 'py-16'">
    <div
      class="mb-4 grid size-12 place-items-center rounded-2xl bg-surface-100 text-surface-400 dark:bg-surface-800 dark:text-surface-500"
    >
      <component :is="icon" class="size-6" />
    </div>
    <div class="text-base font-semibold text-heading">{{ title }}</div>
    <p v-if="description" class="mt-1 max-w-sm text-sm text-secondary">{{ description }}</p>
    <div v-if="$slots.default" class="mt-5">
      <slot />
    </div>
  </div>
</template>
