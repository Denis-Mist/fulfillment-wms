<script setup lang="ts">
import { Search, X } from '@lucide/vue'

withDefaults(defineProps<{ placeholder?: string }>(), { placeholder: 'Поиск' })
const model = defineModel<string>({ default: '' })
</script>

<template>
  <div class="relative w-full sm:w-80">
    <Search class="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-surface-400" />
    <InputText v-model="model" :placeholder="placeholder" class="w-full !pr-9 !pl-9" />
    <button
      v-if="model"
      type="button"
      class="absolute top-1/2 right-2.5 grid size-5 -translate-y-1/2 place-items-center rounded-full text-surface-400 hover:bg-surface-100 hover:text-surface-600 dark:hover:bg-surface-800"
      aria-label="Очистить поиск"
      @click="model = ''"
    >
      <X class="size-3.5" />
    </button>
  </div>
</template>
