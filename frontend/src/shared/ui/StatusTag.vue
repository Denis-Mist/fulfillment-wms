<script setup lang="ts">
import { computed } from 'vue'
import { STATUS_DOMAINS, TONE_CLASSES, type StatusDomain, type StatusMeta } from '@/shared/config/statuses'

const props = withDefaults(defineProps<{ domain: StatusDomain; status: string; size?: 'sm' | 'md' }>(), {
  size: 'sm',
})

const meta = computed<StatusMeta>(
  () => (STATUS_DOMAINS[props.domain] as Record<string, StatusMeta>)[props.status] ?? { label: props.status, tone: 'neutral' },
)
const tone = computed(() => TONE_CLASSES[meta.value.tone])
</script>

<template>
  <span
    class="inline-flex items-center gap-1.5 whitespace-nowrap rounded-full font-medium"
    :class="[tone.badge, size === 'md' ? 'px-3 py-1 text-sm' : 'px-2.5 py-0.5 text-xs']"
  >
    <span class="size-1.5 rounded-full" :class="tone.dot" />
    {{ meta.label }}
  </span>
</template>
