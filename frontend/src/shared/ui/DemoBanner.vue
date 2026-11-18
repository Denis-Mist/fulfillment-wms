<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { ROLE_LABELS } from '@/modules/auth/roles'
import { useSessionStore } from '@/stores/session'

const session = useSessionStore()
const router = useRouter()
const roleLabel = computed(() => (session.role ? ROLE_LABELS[session.role] : 'гость'))

function switchRole() {
  void router.push({ name: 'demo' })
}

function logout() {
  session.logout()
  void router.push({ name: 'login' })
}
</script>

<template>
  <div
    v-if="session.isDemo"
    role="status"
    class="flex flex-wrap items-center justify-center gap-x-2 gap-y-1 bg-amber-50 px-3 py-2 text-center text-sm text-amber-950 dark:bg-amber-500/15 dark:text-amber-100"
  >
    <span>Вы в демо-режиме как {{ roleLabel }}</span>
    <span class="text-amber-700/70 dark:text-amber-200/60" aria-hidden="true">·</span>
    <button
      type="button"
      class="min-h-11 px-1 font-semibold text-amber-900 underline-offset-2 hover:underline dark:text-amber-50"
      @click="switchRole"
    >
      Сменить роль
    </button>
    <span class="text-amber-700/70 dark:text-amber-200/60" aria-hidden="true">·</span>
    <button
      type="button"
      class="min-h-11 px-1 font-semibold text-amber-900 underline-offset-2 hover:underline dark:text-amber-50"
      @click="logout"
    >
      Выйти
    </button>
  </div>
</template>
