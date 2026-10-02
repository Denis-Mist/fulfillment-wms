<script setup lang="ts">
import { useTheme } from '@/shared/composables/useTheme'

useTheme()
</script>

<template>
  <RouterView />
  <Toast position="top-right" />
  <ConfirmDialog />
</template>
