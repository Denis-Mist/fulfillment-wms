<script setup lang="ts">
import QueryErrorBoundary from '@/shared/ui/QueryErrorBoundary.vue'
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { cabinetDef, routeName } from '@/app/sections'
import { useSessionStore } from '@/stores/session'
import AppLogo from '@/shared/ui/AppLogo.vue'
import ThemeToggle from './components/ThemeToggle.vue'
import UserMenu from './components/UserMenu.vue'

const route = useRoute()
const session = useSessionStore()
const sections = computed(() => cabinetDef('worker').groups.flatMap((g) => g.sections))
</script>

<template>
  <div class="flex min-h-screen flex-col">
    <header
      class="sticky top-0 z-20 flex h-14 items-center justify-between border-b border-surface-200/80 bg-white/90 px-4 backdrop-blur-md dark:border-surface-800 dark:bg-surface-900/90"
    >
      <AppLogo compact />
      <div class="min-w-0 px-3 text-center leading-tight">
        <div class="truncate text-sm font-semibold text-heading">{{ route.meta.title }}</div>
        <div class="truncate text-[11px] text-secondary">
          {{ session.activeMembership?.organizationName }}
        </div>
      </div>
      <div class="flex items-center">
        <ThemeToggle />
        <UserMenu compact />
      </div>
    </header>

    <main class="mx-auto w-full max-w-xl flex-1 p-4 pb-24">
      <QueryErrorBoundary>
        <RouterView v-slot="{ Component }">
          <Transition name="page" mode="out-in">
            <component :is="Component" />
          </Transition>
        </RouterView>
      </QueryErrorBoundary>
    </main>

    <nav
      class="fixed inset-x-0 bottom-0 z-20 grid border-t border-surface-200/80 bg-white/95 pb-[env(safe-area-inset-bottom)] backdrop-blur-md dark:border-surface-800 dark:bg-surface-900/95"
      :style="{ gridTemplateColumns: `repeat(${sections.length}, minmax(0, 1fr))` }"
    >
      <RouterLink
        v-for="section in sections"
        :key="section.name"
        :to="{ name: routeName('worker', section.name) }"
        class="flex flex-col items-center gap-1 py-2.5 text-xs font-medium"
        :class="
          route.name === routeName('worker', section.name)
            ? 'text-primary'
            : 'text-surface-500 dark:text-surface-400'
        "
      >
        <component :is="section.icon" class="size-6" />
        {{ section.title }}
      </RouterLink>
    </nav>
  </div>
</template>
