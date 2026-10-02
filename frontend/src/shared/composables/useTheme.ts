import { useDark, useToggle } from '@vueuse/core'
import { DARK_MODE_CLASS } from '@/app/theme'

const isDark = useDark({
  selector: 'html',
  attribute: 'class',
  valueDark: DARK_MODE_CLASS,
  valueLight: '',
  storageKey: 'wms.theme',
})

const toggle = useToggle(isDark)

export function useTheme() {
  return { isDark, toggle }
}
