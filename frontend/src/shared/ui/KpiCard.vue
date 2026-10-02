<script setup lang="ts">
import { computed, type Component } from 'vue'
import { RouterLink, type RouteLocationRaw } from 'vue-router'
import { TrendingDown, TrendingUp } from '@lucide/vue'
import { TONE_CLASSES, type Tone } from '@/shared/config/statuses'
import { formatNumber } from '@/shared/lib/format'

const props = withDefaults(
  defineProps<{
    label: string
    value?: string
    icon: Component
    tone?: Tone
    hint?: string
    current?: number
    previous?: number
    deltaLabel?: string
    /** Рост — это плохо (например, просрочки) */
    invert?: boolean
    loading?: boolean
    to?: RouteLocationRaw
  }>(),
  { tone: 'progress', deltaLabel: 'к вчера', invert: false, loading: false },
)

const toneClasses = computed(() => TONE_CLASSES[props.tone])

const delta = computed(() => {
  if (props.current === undefined || !props.previous) return null
  return ((props.current - props.previous) / props.previous) * 100
})

const isGood = computed(() => (delta.value ?? 0) === 0 || (props.invert ? delta.value! < 0 : delta.value! > 0))
</script>

<template>
  <component
    :is="to ? RouterLink : 'div'"
    :to="to"
    class="surface-card group flex flex-col gap-3 p-5 transition-shadow"
    :class="to && 'hover:shadow-elevated'"
  >
    <div class="flex items-center justify-between gap-3">
      <span class="line-clamp-2 text-sm leading-snug font-medium text-secondary">{{ label }}</span>
      <span class="grid size-9 shrink-0 place-items-center rounded-xl" :class="[toneClasses.soft, toneClasses.text]">
        <component :is="icon" class="size-[18px]" />
      </span>
    </div>
    <Skeleton v-if="loading" height="2rem" width="55%" />
    <div v-else class="tabular truncate text-[26px] leading-none font-semibold tracking-tight text-heading">
      {{ value }}
    </div>
    <div class="flex min-h-5 items-center gap-1.5 text-xs">
      <template v-if="!loading && delta !== null">
        <span
          class="inline-flex items-center gap-1 rounded-md px-1.5 py-0.5 font-semibold"
          :class="
            isGood
              ? 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300'
              : 'bg-rose-50 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300'
          "
        >
          <component :is="delta >= 0 ? TrendingUp : TrendingDown" class="size-3.5" />
          {{ delta >= 0 ? '+' : '−' }}{{ formatNumber(Math.abs(delta), 0) }}%
        </span>
        <span class="text-secondary">{{ deltaLabel }}</span>
      </template>
      <span v-else-if="hint && !loading" class="text-secondary">{{ hint }}</span>
    </div>
  </component>
</template>
