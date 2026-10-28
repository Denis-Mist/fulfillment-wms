<script setup lang="ts">
import type { CalcMeta } from '@/modules/analytics/types'
import { formatTime } from '@/shared/lib/format'

defineProps<{ meta?: CalcMeta }>()
</script>

<template>
  <p v-if="meta" class="text-xs text-secondary">
    Алгоритм <span class="font-mono">{{ meta.algorithm }} v{{ meta.version }}</span> · данные
    <span class="font-mono">{{ meta.dataVersion }}</span> · seed
    <span class="font-mono">{{ meta.seed }}</span> · расчёт {{ meta.durationMs }} мс в
    {{ formatTime(meta.computedAt) }}
  </p>
</template>
