<script setup lang="ts">
import { computed } from 'vue'
import { initials } from '@/shared/lib/format'

const props = withDefaults(defineProps<{ name: string; size?: 'sm' | 'md' | 'lg' }>(), { size: 'md' })

const GRADIENTS = [
  'from-indigo-500 to-violet-500',
  'from-sky-500 to-indigo-500',
  'from-emerald-500 to-teal-500',
  'from-amber-500 to-orange-500',
  'from-rose-500 to-pink-500',
  'from-fuchsia-500 to-purple-500',
]

const gradient = computed(() => {
  const hash = [...props.name].reduce((acc, char) => (acc * 31 + char.charCodeAt(0)) >>> 0, 7)
  return GRADIENTS[hash % GRADIENTS.length]
})
const sizeClass = computed(() => ({ sm: 'size-7 text-[11px]', md: 'size-9 text-xs', lg: 'size-12 text-base' })[props.size])
</script>

<template>
  <span
    class="grid shrink-0 place-items-center rounded-full bg-gradient-to-br font-semibold text-white"
    :class="[gradient, sizeClass]"
  >
    {{ initials(name) }}
  </span>
</template>
