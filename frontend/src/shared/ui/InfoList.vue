<script setup lang="ts">
import type { InfoItem } from './types'

defineProps<{ items: InfoItem[] }>()
</script>

<template>
  <dl class="divide-y divide-surface-100 dark:divide-surface-800">
    <div v-for="item in items" :key="item.label" class="flex items-start justify-between gap-4 py-2.5 first:pt-0 last:pb-0">
      <dt class="shrink-0 text-sm text-secondary">{{ item.label }}</dt>
      <dd class="text-right text-sm font-medium text-heading">{{ item.value ?? '—' }}</dd>
    </div>
  </dl>
</template>
