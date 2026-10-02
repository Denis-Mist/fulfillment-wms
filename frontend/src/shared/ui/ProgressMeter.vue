<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(defineProps<{ value: number; max?: number; tone?: 'auto' | 'primary' | 'success' | 'warn' }>(), {
  max: 100,
  tone: 'primary',
})

const percent = computed(() => (props.max > 0 ? Math.min(100, Math.max(0, (props.value / props.max) * 100)) : 0))

const barClass = computed(() => {
  if (props.tone === 'success') return 'bg-emerald-500'
  if (props.tone === 'warn') return 'bg-amber-500'
  if (props.tone === 'auto') {
    if (percent.value >= 90) return 'bg-rose-500'
    if (percent.value >= 75) return 'bg-amber-500'
    return 'bg-emerald-500'
  }
  return 'bg-primary'
})
</script>

<template>
  <div class="h-1.5 w-full overflow-hidden rounded-full bg-surface-100 dark:bg-surface-800">
    <div class="h-full rounded-full transition-[width] duration-500" :class="barClass" :style="{ width: `${percent}%` }" />
  </div>
</template>
