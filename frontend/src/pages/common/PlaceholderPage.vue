<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { CircleCheck, Rocket } from '@lucide/vue'
import PageHeader from '@/shared/ui/PageHeader.vue'

const route = useRoute()
const planned = computed(() => route.meta.planned ?? [])
const isWorker = computed(() => route.meta.cabinet === 'worker')
</script>

<template>
  <div>
    <PageHeader v-if="!isWorker" :title="route.meta.title ?? 'Раздел'" :description="route.meta.description" />

    <div class="surface-card relative overflow-hidden p-8 sm:p-10">
      <div class="pointer-events-none absolute -top-24 -right-24 size-72 rounded-full bg-primary-100/60 blur-3xl dark:bg-primary-500/10" />
      <div class="relative max-w-2xl">
        <span
          class="mb-5 grid size-12 place-items-center rounded-2xl bg-gradient-to-br from-indigo-500 to-violet-500 text-white shadow-lg shadow-indigo-500/25"
        >
          <Rocket class="size-6" />
        </span>
        <h2 class="text-xl font-semibold text-heading">Раздел в разработке</h2>
        <p class="mt-1.5 text-secondary">
          Здесь появится функциональность на следующих этапах. Ниже — что именно планируется в разделе «{{ route.meta.title }}».
        </p>
        <ul v-if="planned.length" class="mt-6 space-y-3">
          <li v-for="item in planned" :key="item" class="flex items-start gap-3">
            <CircleCheck class="mt-0.5 size-5 shrink-0 text-primary" />
            <span class="text-surface-700 dark:text-surface-200">{{ item }}</span>
          </li>
        </ul>
      </div>
    </div>
  </div>
</template>
