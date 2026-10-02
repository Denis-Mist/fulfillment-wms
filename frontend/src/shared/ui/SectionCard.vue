<script setup lang="ts">
withDefaults(defineProps<{ title?: string; description?: string; flush?: boolean }>(), { flush: false })
</script>

<template>
  <section class="surface-card flex flex-col overflow-hidden">
    <header
      v-if="title || $slots.actions"
      class="flex flex-wrap items-start justify-between gap-3 px-5 pt-5"
      :class="flush && 'pb-4'"
    >
      <div>
        <h2 v-if="title" class="text-[15px] font-semibold text-heading">{{ title }}</h2>
        <p v-if="description" class="mt-0.5 text-sm text-secondary">{{ description }}</p>
      </div>
      <div v-if="$slots.actions" class="flex items-center gap-2">
        <slot name="actions" />
      </div>
    </header>
    <div class="flex-1" :class="flush ? '' : 'p-5'">
      <slot />
    </div>
  </section>
</template>
