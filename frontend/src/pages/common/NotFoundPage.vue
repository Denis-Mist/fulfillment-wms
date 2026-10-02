<script setup lang="ts">
import { MapPinOff } from '@lucide/vue'
</script>

<template>
  <div class="grid min-h-screen place-items-center p-6">
    <div class="text-center">
      <span class="mx-auto mb-6 grid size-16 place-items-center rounded-2xl bg-surface-100 text-surface-400 dark:bg-surface-800">
        <MapPinOff class="size-8" />
      </span>
      <div class="tabular text-6xl font-semibold tracking-tight text-heading">404</div>
      <p class="mt-2 text-secondary">Такой страницы нет. Возможно, ссылка устарела.</p>
      <RouterLink to="/">
        <Button label="На главную" class="mt-6" />
      </RouterLink>
    </div>
  </div>
</template>
