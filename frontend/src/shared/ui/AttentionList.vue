<script setup lang="ts">
import { ChevronRight, CircleCheck } from '@lucide/vue'
import type { AttentionItem, AttentionTone } from '@/modules/dashboard/types'
import { TONE_CLASSES, type Tone } from '@/shared/config/statuses'
import { useCabinet } from '@/shared/composables/useCabinet'
import { formatNumber } from '@/shared/lib/format'

defineProps<{ items: AttentionItem[]; loading?: boolean }>()
const { to } = useCabinet()

const toneMap: Record<AttentionTone, Tone> = { info: 'info', warn: 'warn', danger: 'danger', success: 'success' }
const tone = (value: AttentionTone) => TONE_CLASSES[toneMap[value]]
</script>

<template>
  <div v-if="loading" class="space-y-2">
    <Skeleton v-for="i in 4" :key="i" height="3.5rem" border-radius="12px" />
  </div>
  <div v-else-if="!items.length" class="flex items-center gap-3 rounded-xl bg-emerald-50 p-4 dark:bg-emerald-500/10">
    <CircleCheck class="size-5 text-emerald-600 dark:text-emerald-400" />
    <div>
      <div class="text-sm font-semibold text-emerald-800 dark:text-emerald-200">Всё под контролем</div>
      <div class="text-xs text-emerald-700/80 dark:text-emerald-300/80">Срочных задач нет</div>
    </div>
  </div>
  <ul v-else class="space-y-2">
    <li v-for="item in items" :key="item.key">
      <RouterLink
        :to="to(item.section, undefined, item.query)"
        class="group flex items-center gap-3 rounded-xl border border-surface-200/80 p-3 transition-all hover:border-surface-300 hover:bg-surface-50 dark:border-surface-800 dark:hover:border-surface-700 dark:hover:bg-surface-800/50"
      >
        <span
          class="tabular grid h-10 min-w-10 place-items-center rounded-lg px-2 text-base font-semibold"
          :class="[tone(item.tone).soft, tone(item.tone).text]"
        >
          {{ formatNumber(item.count) }}
        </span>
        <span class="min-w-0 flex-1">
          <span class="block break-words text-sm font-medium text-heading">{{ item.title }}</span>
          <span class="block break-words text-xs text-secondary">{{ item.description }}</span>
        </span>
        <ChevronRight class="size-4 text-surface-400 transition-transform group-hover:translate-x-0.5" />
      </RouterLink>
    </li>
  </ul>
</template>
