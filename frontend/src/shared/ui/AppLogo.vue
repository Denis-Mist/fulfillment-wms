<script setup lang="ts">
withDefaults(defineProps<{ compact?: boolean; inverted?: boolean }>(), { compact: false, inverted: false })
</script>

<template>
  <div class="flex items-center gap-2.5 select-none">
    <div
      class="grid size-9 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-indigo-500 to-violet-500 shadow-md shadow-indigo-500/25"
    >
      <svg viewBox="0 0 32 32" class="size-6" fill="none" stroke="white" stroke-width="2" stroke-linejoin="round">
        <path d="M9 12.5 16 9l7 3.5v7L16 23l-7-3.5z" />
        <path d="M9 12.5 16 16l7-3.5M16 16v7" />
      </svg>
    </div>
    <div v-if="!compact" class="leading-tight">
      <div class="text-[15px] font-semibold tracking-tight" :class="inverted ? 'text-white' : 'text-heading'">
        Fulfillment WMS
      </div>
      <div class="text-[11px]" :class="inverted ? 'text-white/70' : 'text-secondary'">Управление складом</div>
    </div>
  </div>
</template>
