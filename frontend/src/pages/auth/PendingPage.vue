<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useIntervalFn } from '@vueuse/core'
import { Ban, Check, Clock, ShieldCheck, XCircle } from '@lucide/vue'
import { CABINET_HOME } from '@/modules/auth/roles'
import { ORGANIZATION_TYPES } from '@/shared/config/statuses'
import { useSessionStore } from '@/stores/session'

const session = useSessionStore()
const router = useRouter()
const checking = ref(false)

const membership = computed(() => session.activeMembership)
const status = computed(() => membership.value?.organizationStatus ?? 'pending')

async function check() {
  checking.value = true
  try {
    await session.refreshUser()
    if (!session.isRestricted && session.cabinet) await router.replace({ name: CABINET_HOME[session.cabinet] })
  } finally {
    checking.value = false
  }
}

useIntervalFn(() => {
  if (status.value === 'pending') void check()
}, 10_000)

function logout() {
  session.logout()
  void router.push({ name: 'login' })
}

const steps = computed(() => [
  { title: 'Заявка отправлена', text: 'Данные организации получены', done: true },
  {
    title: 'Проверка администратором',
    text: 'Обычно занимает до одного рабочего дня',
    done: false,
    current: true,
  },
  { title: 'Доступ к кабинету', text: 'Сразу после одобрения', done: false },
])
</script>

<template>
  <div>
    <template v-if="status === 'pending'">
      <span class="grid size-14 place-items-center rounded-2xl bg-amber-50 text-amber-600 dark:bg-amber-500/15 dark:text-amber-400">
        <Clock class="size-7" />
      </span>
      <h1 class="mt-5 text-[28px] font-semibold tracking-tight text-heading">Заявка на проверке</h1>
      <p class="mt-1.5 text-secondary">
        {{ membership?.organizationName }} · {{ membership && ORGANIZATION_TYPES[membership.organizationType] }}
      </p>

      <ol class="mt-8 space-y-0">
        <li v-for="(item, index) in steps" :key="item.title" class="relative flex gap-4 pb-6 last:pb-0">
          <span
            v-if="index < steps.length - 1"
            class="absolute top-8 left-[15px] h-[calc(100%-2rem)] w-0.5 bg-surface-200 dark:bg-surface-800"
          />
          <span
            class="relative grid size-8 shrink-0 place-items-center rounded-full"
            :class="
              item.done
                ? 'bg-primary text-primary-contrast'
                : item.current
                  ? 'bg-amber-50 text-amber-600 ring-2 ring-amber-400 dark:bg-amber-500/15'
                  : 'bg-surface-100 text-surface-400 dark:bg-surface-800'
            "
          >
            <Check v-if="item.done" class="size-4" />
            <ShieldCheck v-else-if="item.current" class="size-4" />
            <span v-else class="text-xs font-semibold">{{ index + 1 }}</span>
          </span>
          <span class="pt-1">
            <span class="block text-sm font-semibold text-heading">{{ item.title }}</span>
            <span class="block text-sm text-secondary">{{ item.text }}</span>
          </span>
        </li>
      </ol>

      <Message severity="info" :closable="false" class="mt-8" size="small">
        Страница обновится автоматически, как только заявку одобрят. В демо-режиме одобрить её можно, войдя как
        администратор в разделе «Модерация».
      </Message>
    </template>

    <template v-else>
      <span class="grid size-14 place-items-center rounded-2xl bg-rose-50 text-rose-600 dark:bg-rose-500/15 dark:text-rose-400">
        <component :is="status === 'blocked' ? Ban : XCircle" class="size-7" />
      </span>
      <h1 class="mt-5 text-[28px] font-semibold tracking-tight text-heading">
        {{ status === 'blocked' ? 'Доступ приостановлен' : 'Заявка отклонена' }}
      </h1>
      <p class="mt-1.5 text-secondary">
        {{ membership?.organizationName }}. Свяжитесь с поддержкой платформы, чтобы уточнить причину.
      </p>
    </template>

    <div class="mt-8 flex gap-3">
      <Button label="Проверить статус" :loading="checking" class="flex-1" @click="check" />
      <Button label="Выйти" severity="secondary" outlined @click="logout" />
    </div>
  </div>
</template>
