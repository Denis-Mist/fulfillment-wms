<script setup lang="ts">
import { BrainCircuit, ScanLine, Warehouse } from '@lucide/vue'
import AppLogo from '@/shared/ui/AppLogo.vue'
import ThemeToggle from './components/ThemeToggle.vue'

const features = [
  { icon: Warehouse, title: 'Много складов и клиентов', text: 'Остатки каждого селлера на каждом складе — в одной системе' },
  { icon: ScanLine, title: 'Приёмка, отбор и отгрузка', text: 'Пошаговые задания для сотрудников склада со сканером' },
  { icon: BrainCircuit, title: 'Прогнозы и оптимизация', text: 'Прогноз нагрузки, оптимальные маршруты и размещение товара' },
]

const bars = [42, 58, 51, 66, 73, 61, 84, 78, 92, 88]
</script>

<template>
  <div class="grid min-h-screen bg-white lg:grid-cols-[1.05fr_1fr] dark:bg-surface-950">
    <aside
      class="relative hidden flex-col justify-between overflow-hidden bg-gradient-to-br from-indigo-600 via-indigo-600 to-violet-700 p-12 text-white lg:flex"
    >
      <div
        class="pointer-events-none absolute inset-0 opacity-[0.15] [background-image:linear-gradient(rgba(255,255,255,.5)_1px,transparent_1px),linear-gradient(90deg,rgba(255,255,255,.5)_1px,transparent_1px)] [background-size:44px_44px] [mask-image:radial-gradient(ellipse_at_top_left,black_30%,transparent_75%)]"
      />
      <div class="pointer-events-none absolute -right-24 -bottom-24 size-96 rounded-full bg-fuchsia-400/30 blur-3xl" />
      <div class="pointer-events-none absolute top-1/3 -left-16 size-72 rounded-full bg-sky-400/20 blur-3xl" />

      <AppLogo inverted class="relative" />

      <div class="relative max-w-lg">
        <h1 class="text-4xl leading-tight font-semibold tracking-tight">
          Операционная платформа для фулфилмент-центров и селлеров
        </h1>
        <p class="mt-4 text-lg text-white/75">
          Склад, заказы, поставки и аналитика — понятно для каждого сотрудника с первого дня.
        </p>
        <ul class="mt-10 space-y-5">
          <li v-for="feature in features" :key="feature.title" class="flex gap-4">
            <span class="grid size-10 shrink-0 place-items-center rounded-xl bg-white/15 ring-1 ring-white/20">
              <component :is="feature.icon" class="size-5" />
            </span>
            <span>
              <span class="block font-semibold">{{ feature.title }}</span>
              <span class="block text-sm text-white/70">{{ feature.text }}</span>
            </span>
          </li>
        </ul>
      </div>

      <div class="relative flex items-end justify-between gap-6">
        <div class="w-72 rounded-2xl bg-white/10 p-5 ring-1 ring-white/20 backdrop-blur-md">
          <div class="flex items-center justify-between text-sm text-white/75">
            Отгружено за 10 дней
            <span class="rounded-md bg-emerald-400/25 px-1.5 py-0.5 text-xs font-semibold text-emerald-100">+18%</span>
          </div>
          <div class="tabular mt-1 text-3xl font-semibold">12 480</div>
          <div class="mt-4 flex h-14 items-end gap-1.5">
            <div
              v-for="(bar, index) in bars"
              :key="index"
              class="flex-1 rounded-t bg-white/70"
              :style="{ height: `${bar}%`, opacity: 0.45 + index * 0.055 }"
            />
          </div>
        </div>
        <div class="text-xs text-white/60">© 2026 · Дипломный проект</div>
      </div>
    </aside>

    <main class="relative flex flex-col">
      <div class="flex items-center justify-between p-6">
        <AppLogo class="lg:invisible" />
        <ThemeToggle />
      </div>
      <div class="flex flex-1 items-center justify-center px-6 pb-12">
        <div class="w-full max-w-md">
          <RouterView v-slot="{ Component }">
            <Transition name="page" mode="out-in">
              <component :is="Component" />
            </Transition>
          </RouterView>
        </div>
      </div>
    </main>
  </div>
</template>
