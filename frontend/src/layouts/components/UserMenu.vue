<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useConfirm } from 'primevue/useconfirm'
import { ChevronDown, LogOut, Moon, RotateCcw, Sun } from '@lucide/vue'
import { ROLE_LABELS } from '@/modules/auth/roles'
import { useTheme } from '@/shared/composables/useTheme'
import { useSessionStore } from '@/stores/session'
import UserAvatar from '@/shared/ui/UserAvatar.vue'

withDefaults(defineProps<{ compact?: boolean }>(), { compact: false })

const session = useSessionStore()
const router = useRouter()
const confirm = useConfirm()
const { isDark, toggle: toggleTheme } = useTheme()
const popover = ref()

const name = computed(() => session.user?.fullName ?? '')
const role = computed(() => (session.role ? ROLE_LABELS[session.role] : ''))
const mocksEnabled = import.meta.env.VITE_USE_MOCKS === 'true'

function logout() {
  popover.value?.hide()
  session.logout()
  void router.push({ name: 'login' })
}

function resetDemo() {
  popover.value?.hide()
  confirm.require({
    header: 'Сбросить демо-данные?',
    message: 'Все изменения, сделанные в демо-режиме, будут удалены. Данные вернутся к исходному состоянию.',
    acceptProps: { label: 'Сбросить', severity: 'danger' },
    rejectProps: { label: 'Отмена', severity: 'secondary', outlined: true },
    accept: async () => {
      const { resetMockData } = await import('@/mocks/browser')
      resetMockData()
      window.location.reload()
    },
  })
}
</script>

<template>
  <div>
    <button
      type="button"
      class="flex items-center gap-2.5 rounded-xl py-1 pr-2 pl-1 transition-colors hover:bg-surface-100 dark:hover:bg-surface-800"
      @click="popover?.toggle($event)"
    >
      <UserAvatar :name="name" />
      <span v-if="!compact" class="hidden text-left leading-tight xl:block">
        <span class="block max-w-40 truncate text-sm font-medium text-heading">{{ name }}</span>
        <span class="block max-w-40 truncate text-xs text-secondary">{{ role }}</span>
      </span>
      <ChevronDown class="hidden size-4 text-surface-400 xl:block" />
    </button>

    <Popover ref="popover" class="w-72" :pt="{ content: { class: '!p-1.5' } }">
      <div class="flex items-center gap-3 px-2.5 py-2.5">
        <UserAvatar :name="name" size="lg" />
        <div class="min-w-0">
          <div class="truncate font-semibold text-heading">{{ name }}</div>
          <div class="truncate text-xs text-secondary">{{ session.user?.email }}</div>
          <div class="truncate text-xs text-secondary">{{ role }}</div>
        </div>
      </div>
      <div class="my-1 border-t border-surface-100 dark:border-surface-800" />
      <button
        type="button"
        class="flex w-full items-center gap-2.5 rounded-lg px-2.5 py-2 text-sm text-surface-700 hover:bg-surface-100 dark:text-surface-200 dark:hover:bg-surface-800"
        @click="toggleTheme()"
      >
        <component :is="isDark ? Sun : Moon" class="size-4 text-surface-400" />
        {{ isDark ? 'Светлая тема' : 'Тёмная тема' }}
      </button>
      <button
        v-if="mocksEnabled"
        type="button"
        class="flex w-full items-center gap-2.5 rounded-lg px-2.5 py-2 text-sm text-surface-700 hover:bg-surface-100 dark:text-surface-200 dark:hover:bg-surface-800"
        @click="resetDemo"
      >
        <RotateCcw class="size-4 text-surface-400" />
        Сбросить демо-данные
      </button>
      <button
        type="button"
        class="flex w-full items-center gap-2.5 rounded-lg px-2.5 py-2 text-sm text-rose-600 hover:bg-rose-50 dark:text-rose-400 dark:hover:bg-rose-500/10"
        @click="logout"
      >
        <LogOut class="size-4" />
        Выйти
      </button>
    </Popover>
  </div>
</template>
