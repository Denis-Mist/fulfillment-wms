<script setup lang="ts">
import { ref } from 'vue'
import { onKeyStroke, useStorage } from '@vueuse/core'
import AppSidebar from './components/AppSidebar.vue'
import AppTopbar from './components/AppTopbar.vue'
import CommandPalette from './components/CommandPalette.vue'
import QueryErrorBoundary from '@/shared/ui/QueryErrorBoundary.vue'

const collapsed = useStorage('wms.sidebarCollapsed', false)
const mobileNav = ref(false)
const searchOpen = ref(false)

onKeyStroke('k', (event) => {
  if (!event.ctrlKey && !event.metaKey) return
  event.preventDefault()
  searchOpen.value = !searchOpen.value
})
</script>

<template>
  <div class="flex min-h-screen">
    <AppSidebar class="hidden lg:flex" :collapsed="collapsed" @toggle="collapsed = !collapsed" />

    <Drawer
      v-model:visible="mobileNav"
      class="!w-[280px]"
      :show-close-icon="false"
      :pt="{ content: { class: '!p-0' } }"
    >
      <template #container>
        <AppSidebar mobile @navigate="mobileNav = false" />
      </template>
    </Drawer>

    <div class="flex min-w-0 flex-1 flex-col">
      <AppTopbar @open-nav="mobileNav = true" @open-search="searchOpen = true" />
      <main class="mx-auto w-full max-w-[1480px] flex-1 px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
        <QueryErrorBoundary>
          <RouterView v-slot="{ Component, route }">
            <Transition name="page" mode="out-in">
              <component :is="Component" v-if="Component" :key="route.path" />
              <div v-else class="space-y-6" aria-busy="true" aria-label="Загрузка раздела">
                <Skeleton width="14rem" height="2rem" />
                <div class="grid grid-cols-2 gap-3 lg:grid-cols-4">
                  <Skeleton v-for="i in 4" :key="i" height="6.5rem" border-radius="1rem" />
                </div>
                <Skeleton height="20rem" border-radius="1rem" />
              </div>
            </Transition>
          </RouterView>
        </QueryErrorBoundary>
      </main>
    </div>

    <CommandPalette v-model:visible="searchOpen" />
  </div>
</template>
