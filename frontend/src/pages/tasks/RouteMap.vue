<script setup lang="ts">
import { computed } from 'vue'
import type { RoutePoint } from '@/modules/tasks/types'

/** Схема обхода ячеек: вход в (0,0), линии — путь комплектовщика */
const props = defineProps<{ points: RoutePoint[]; ghost?: RoutePoint[] }>()

const box = computed(() => {
  const all = [{ x: 0, y: 0 }, ...props.points, ...(props.ghost ?? [])]
  const maxX = Math.max(10, ...all.map((p) => p.x))
  const maxY = Math.max(10, ...all.map((p) => p.y))
  return { w: maxX + 4, h: maxY + 4 }
})
const path = (pts: RoutePoint[]) => {
  const seq = [{ x: 0, y: 0 }, ...pts, { x: 0, y: 0 }]
  // Манхэттенский путь: сначала по проходу (y), затем вдоль (x)
  let d = `M ${seq[0]!.x + 2} ${seq[0]!.y + 2}`
  for (let i = 1; i < seq.length; i++)
    d += ` L ${seq[i - 1]!.x + 2} ${seq[i]!.y + 2} L ${seq[i]!.x + 2} ${seq[i]!.y + 2}`
  return d
}
</script>

<template>
  <svg
    :viewBox="`0 0 ${box.w} ${box.h}`"
    class="h-auto w-full rounded-lg bg-surface-50 dark:bg-surface-800"
    preserveAspectRatio="xMidYMid meet"
    role="img"
    aria-label="Маршрут отбора"
  >
    <path
      v-if="ghost?.length"
      :d="path(ghost)"
      fill="none"
      stroke="currentColor"
      class="text-surface-300 dark:text-surface-600"
      :stroke-width="box.w / 200"
      stroke-dasharray="1 1"
    />
    <path
      :d="path(points)"
      fill="none"
      stroke="#6366f1"
      :stroke-width="box.w / 140"
      stroke-linejoin="round"
    />
    <rect
      :x="2 - box.w / 60"
      :y="2 - box.w / 60"
      :width="box.w / 30"
      :height="box.w / 30"
      :rx="box.w / 150"
      fill="#10b981"
    />
    <g v-for="(p, i) in points" :key="p.code + i">
      <circle :cx="p.x + 2" :cy="p.y + 2" :r="box.w / 70" fill="#6366f1" />
      <text
        :x="p.x + 2"
        :y="p.y + 2 + box.w / 220"
        text-anchor="middle"
        :font-size="box.w / 60"
        fill="white"
        font-weight="600"
      >
        {{ i + 1 }}
      </text>
    </g>
  </svg>
</template>
