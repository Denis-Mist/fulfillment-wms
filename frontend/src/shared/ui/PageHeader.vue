<script setup lang="ts">
import { ArrowLeft } from '@lucide/vue'
import type { RouteLocationRaw } from 'vue-router'

defineProps<{
  title: string
  description?: string
  back?: { to: RouteLocationRaw; label: string }
}>()
</script>

<template>
  <header class="mb-6">
    <RouterLink
      v-if="back"
      :to="back.to"
      class="mb-3 inline-flex items-center gap-1.5 text-sm text-secondary transition-colors hover:text-primary"
    >
      <ArrowLeft class="size-4" />
      {{ back.label }}
    </RouterLink>
    <div class="flex flex-wrap items-start justify-between gap-4">
      <div class="min-w-0">
        <div class="flex flex-wrap items-center gap-3">
          <h1 class="text-2xl font-semibold tracking-tight text-heading">{{ title }}</h1>
          <slot name="status" />
        </div>
        <p v-if="description" class="mt-1 text-[15px] text-secondary">{{ description }}</p>
        <div v-if="$slots.meta" class="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1 text-sm text-secondary">
          <slot name="meta" />
        </div>
      </div>
      <div v-if="$slots.actions" class="flex flex-wrap items-center gap-2">
        <slot name="actions" />
      </div>
    </div>
  </header>
</template>
