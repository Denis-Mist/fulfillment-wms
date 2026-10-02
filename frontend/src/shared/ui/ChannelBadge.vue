<script setup lang="ts">
import { computed } from 'vue'
import type { SalesChannel } from '@/modules/orders/types'
import { SALES_CHANNELS } from '@/shared/config/statuses'

const props = defineProps<{ channel: SalesChannel }>()
const meta = computed(() => SALES_CHANNELS[props.channel])
</script>

<template>
  <span
    v-tooltip.top="meta.label"
    class="inline-flex items-center rounded-md px-2 py-0.5 text-xs font-semibold"
    :style="{ color: meta.color, backgroundColor: `color-mix(in srgb, ${meta.color} 12%, transparent)` }"
  >
    {{ meta.short }}
  </span>
</template>
