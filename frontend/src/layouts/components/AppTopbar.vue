<script setup lang="ts">
import { Menu, Search } from '@lucide/vue'
import ContextSwitcher from './ContextSwitcher.vue'
import NotificationsButton from './NotificationsButton.vue'
import ThemeToggle from './ThemeToggle.vue'
import UserMenu from './UserMenu.vue'

const emit = defineEmits<{ openNav: []; openSearch: [] }>()
</script>

<template>
  <header
    class="sticky top-0 z-20 flex h-16 shrink-0 items-center gap-2 border-b border-surface-200/80 bg-white/80 px-4 backdrop-blur-md sm:gap-3 sm:px-6 lg:px-8 dark:border-surface-800 dark:bg-surface-900/80"
  >
    <Button
      text
      rounded
      severity="secondary"
      aria-label="Меню"
      class="!size-10 shrink-0 !p-0 lg:!hidden"
      @click="emit('openNav')"
    >
      <Menu class="size-5" />
    </Button>

    <button
      type="button"
      aria-label="Поиск"
      class="flex size-10 shrink-0 items-center justify-center gap-2.5 rounded-xl border border-surface-200 bg-surface-50 text-sm text-surface-400 transition-colors hover:border-surface-300 hover:text-surface-500 sm:w-auto sm:max-w-md sm:flex-1 sm:justify-start sm:px-3 dark:border-surface-700 dark:bg-surface-800/60 dark:hover:border-surface-600"
      @click="emit('openSearch')"
    >
      <Search class="size-4 shrink-0" />
      <span class="hidden truncate sm:inline">Поиск по заказам, поставкам, товарам…</span>
      <kbd
        class="ml-auto hidden rounded-md border border-surface-200 bg-white px-1.5 py-0.5 font-sans text-[11px] text-surface-400 md:block dark:border-surface-600 dark:bg-surface-900"
      >
        Ctrl K
      </kbd>
    </button>

    <div class="ml-auto flex min-w-0 items-center gap-1 sm:gap-1.5">
      <ContextSwitcher />
      <ThemeToggle class="hidden sm:inline-flex" />
      <NotificationsButton />
      <UserMenu />
    </div>
  </header>
</template>
