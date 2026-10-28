<script setup lang="ts">
import { computed, useTemplateRef } from 'vue'
import { useElementSize } from '@vueuse/core'
import VChart from 'vue-echarts'
import { CHART_COLORS, useChartTheme } from '@/shared/composables/useChartTheme'
import './chart'
import type { ChartOption, TooltipComponentOption } from './chart'

const props = withDefaults(defineProps<{ option: ChartOption; height?: string }>(), { height: '280px' })
const theme = useChartTheme()
// ECharts нельзя инициализировать в скрытом контейнере (0×0): ждём, пока у обёртки появится ширина
const box = useTemplateRef<HTMLElement>('box')
const { width } = useElementSize(box)
const visible = computed(() => width.value > 0)

const merged = computed<ChartOption>(() => ({
  color: CHART_COLORS,
  textStyle: { fontFamily: 'Inter Variable, system-ui, sans-serif', color: theme.value.text },
  animationDuration: 500,
  ...props.option,
  tooltip: {
    backgroundColor: theme.value.surface,
    borderColor: theme.value.border,
    borderWidth: 1,
    padding: [8, 12],
    textStyle: { color: theme.value.text, fontSize: 12 },
    extraCssText: 'border-radius: 10px; box-shadow: 0 12px 32px -12px rgba(16,24,40,.25);',
    ...(props.option.tooltip as TooltipComponentOption | undefined),
  },
}))
</script>

<template>
  <div ref="box" :style="{ height }">
    <VChart v-if="visible" :option="merged" autoresize :style="{ height }" />
  </div>
</template>
