<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { useConfirm } from 'primevue/useconfirm'
import { ROLE_LABELS } from '@/modules/auth/roles'
import { useSessionStore } from '@/stores/session'

const session = useSessionStore()
const router = useRouter()
const confirm = useConfirm()
const roleLabel = computed(() => (session.role ? ROLE_LABELS[session.role] : 'гость'))

function switchRole() {
  void router.push({ name: 'demo' })
}

function resetData() {
  confirm.require({
    header: 'Сбросить данные?',
    message: 'Учебная база в этом браузере вернётся к исходному состоянию. Текущая роль сохранится.',
    acceptProps: { label: 'Сбросить', severity: 'danger' },
    rejectProps: { label: 'Отмена', severity: 'secondary', outlined: true },
    accept: async () => {
      const dest = await session.resetDemo()
      if (dest === 'demo') void router.replace({ name: 'demo' })
    },
  })
}

async function logout() {
  const dest = await session.logout()
  void router.replace(dest === 'home' ? '/' : { name: 'login' })
}
</script>

<template>
  <div
    v-if="session.isDemo"
    role="status"
    data-testid="demo-banner"
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
      @click="resetData"
    >
      Сбросить данные
    </button>
    <span class="text-amber-700/70 dark:text-amber-200/60" aria-hidden="true">·</span>
    <button
      type="button"
      class="min-h-11 px-1 font-semibold text-amber-900 underline-offset-2 hover:underline dark:text-amber-50"
      @click="logout"
    >
      Выйти
    </button>
    <span class="basis-full text-xs text-amber-800/80 dark:text-amber-100/70">
      Это учебный стенд в браузере, на настоящую WMS не влияет.
    </span>
  </div>
</template>
