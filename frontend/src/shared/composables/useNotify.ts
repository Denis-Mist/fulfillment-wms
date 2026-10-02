import { useToast } from 'primevue/usetoast'
import { errorMessage } from '@/shared/api/http'

export function useNotify() {
  const toast = useToast()
  return {
    success: (summary: string, detail?: string) => toast.add({ severity: 'success', summary, detail, life: 3500 }),
    info: (summary: string, detail?: string) => toast.add({ severity: 'info', summary, detail, life: 3500 }),
    error: (error: unknown, summary = 'Не удалось выполнить действие') =>
      toast.add({ severity: 'error', summary, detail: errorMessage(error), life: 5000 }),
  }
}
