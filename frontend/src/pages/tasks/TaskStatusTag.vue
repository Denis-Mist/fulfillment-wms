<script setup lang="ts">
import { computed } from 'vue'
import { TASK_STATUSES } from '@/modules/tasks/labels'
import type { TaskStatus } from '@/modules/tasks/types'
import { TONE_CLASSES } from '@/shared/config/statuses'

const props = defineProps<{ status: TaskStatus }>()
const meta = computed(() => TASK_STATUSES[props.status])
const tone = computed(() => TONE_CLASSES[meta.value.tone])
</script>

<template>
  <span
    class="inline-flex items-center gap-1.5 whitespace-nowrap rounded-full px-2 py-0.5 text-xs font-medium"
    :class="tone.badge"
  >
    <span class="size-1.5 rounded-full" :class="tone.dot" />
    {{ meta.label }}
  </span>
</template>
