<script setup lang="ts">
import { TONE_CLASSES, type Tone } from '@/shared/config/statuses'

defineProps<{ tone: Tone; label: string }>()
</script>

<template>
  <span
    class="inline-flex items-center gap-1.5 whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs font-medium"
    :class="TONE_CLASSES[tone].badge"
  >
    <span class="size-1.5 rounded-full" :class="TONE_CLASSES[tone].dot" />
    {{ label }}
  </span>
</template>
