<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { Bell, BellOff } from '@lucide/vue'
import { notificationsApi, type AppNotification } from '@/modules/notifications/api'
import { TONE_CLASSES, type Tone } from '@/shared/config/statuses'
import { useCabinet } from '@/shared/composables/useCabinet'
import { formatRelative } from '@/shared/lib/format'
import { useSessionStore } from '@/stores/session'

const session = useSessionStore()
const router = useRouter()
const queryClient = useQueryClient()
const { to } = useCabinet()
const popover = ref()

const queryKey = computed(() => ['notifications', session.organizationId])
const { data } = useQuery({
  queryKey,
  queryFn: () => notificationsApi.list(),
  refetchInterval: 60_000,
})

const unread = computed(() => data.value?.filter((n) => !n.read).length ?? 0)

const markAll = useMutation({
  mutationFn: () => notificationsApi.markAllRead(),
  onSuccess: () => queryClient.invalidateQueries({ queryKey: ['notifications'] }),
})

const toneOf = (n: AppNotification) => TONE_CLASSES[n.tone as Tone]

function open(n: AppNotification) {
  if (n.link?.type === 'inbound') {
    popover.value?.hide()
    void router.push(to('inbound-detail', { id: n.link.id }))
  }
}
</script>

<template>
  <div>
    <Button
      v-tooltip.bottom="'Уведомления'"
      text
      rounded
      severity="secondary"
      aria-label="Уведомления"
      class="relative !size-10 !p-0"
      @click="popover?.toggle($event)"
    >
      <Bell class="size-[18px]" />
      <span
        v-if="unread"
        class="absolute top-1.5 right-1.5 grid h-4 min-w-4 place-items-center rounded-full bg-rose-500 px-1 text-[10px] font-bold text-white ring-2 ring-white dark:ring-surface-900"
      >
        {{ unread > 9 ? '9+' : unread }}
      </span>
    </Button>

    <Popover ref="popover" class="w-[380px] max-w-[calc(100vw-2rem)]" :pt="{ content: { class: '!p-0' } }">
      <div class="flex items-center justify-between border-b border-surface-100 px-4 py-3 dark:border-surface-800">
        <div class="font-semibold text-heading">Уведомления</div>
        <Button
          v-if="unread"
          label="Прочитать все"
          text
          size="small"
          :loading="markAll.isPending.value"
          @click="markAll.mutate()"
        />
      </div>
      <div class="max-h-[420px] overflow-y-auto">
        <div v-if="!data?.length" class="flex flex-col items-center gap-2 py-10 text-sm text-secondary">
          <BellOff class="size-6 text-surface-300" />
          Новых уведомлений нет
        </div>
        <button
          v-for="n in data"
          :key="n.id"
          type="button"
          class="flex w-full gap-3 px-4 py-3 text-left transition-colors hover:bg-surface-50 dark:hover:bg-surface-800/60"
          :class="n.link ? 'cursor-pointer' : 'cursor-default'"
          @click="open(n)"
        >
          <span class="mt-1.5 size-2 shrink-0 rounded-full" :class="n.read ? 'bg-transparent' : toneOf(n).dot" />
          <span class="min-w-0 flex-1">
            <span class="block text-sm font-medium text-heading">{{ n.title }}</span>
            <span class="block truncate text-xs text-secondary">{{ n.text }}</span>
            <span class="mt-0.5 block text-[11px] text-surface-400">{{ formatRelative(n.at) }}</span>
          </span>
        </button>
      </div>
    </Popover>
  </div>
</template>
