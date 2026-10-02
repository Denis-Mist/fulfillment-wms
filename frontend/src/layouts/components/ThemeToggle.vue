<script setup lang="ts">
import { Moon, Sun } from '@lucide/vue'
import { useTheme } from '@/shared/composables/useTheme'

const { isDark, toggle } = useTheme()
</script>

<template>
  <Button
    v-tooltip.bottom="isDark ? 'Светлая тема' : 'Тёмная тема'"
    text
    rounded
    severity="secondary"
    :aria-label="isDark ? 'Светлая тема' : 'Тёмная тема'"
    class="!size-10 !p-0"
    @click="toggle()"
  >
    <component :is="isDark ? Sun : Moon" class="size-[18px]" />
  </Button>
</template>
