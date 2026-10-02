import { computed } from 'vue'
import { useTheme } from './useTheme'

export const CHART_COLORS = ['#6366f1', '#22c55e', '#f59e0b', '#0ea5e9', '#a855f7', '#f43f5e', '#14b8a6']

export function useChartTheme() {
  const { isDark } = useTheme()
  return computed(() => ({
    isDark: isDark.value,
    text: isDark.value ? '#cbd5e1' : '#475569',
    muted: isDark.value ? '#64748b' : '#94a3b8',
    grid: isDark.value ? 'rgba(148,163,184,0.12)' : 'rgba(148,163,184,0.22)',
    surface: isDark.value ? '#0f172a' : '#ffffff',
    border: isDark.value ? '#1e293b' : '#e2e8f0',
  }))
}
