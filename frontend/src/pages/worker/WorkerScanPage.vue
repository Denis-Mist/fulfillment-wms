<script setup lang="ts">
import { ref } from 'vue'
import { useMutation } from '@tanstack/vue-query'
import { Boxes, MapPin, ScanBarcode } from '@lucide/vue'
import { tasksApi } from '@/modules/tasks/api'
import type { LookupResult } from '@/modules/tasks/types'
import { ApiError } from '@/shared/api/http'
import { formatNumber } from '@/shared/lib/format'
import EmptyState from '@/shared/ui/EmptyState.vue'

const code = ref('')
const result = ref<LookupResult | null>(null)
const error = ref<string | null>(null)

const lookup = useMutation({
  mutationFn: (value: string) => tasksApi.lookup(value),
  onSuccess: (r) => {
    result.value = r
    error.value = null
  },
  onError: (e) => {
    result.value = null
    error.value =
      e instanceof ApiError ? [e.problem.title, e.problem.detail].filter(Boolean).join('. ') : 'Ошибка'
  },
})
function submit() {
  if (code.value.trim()) lookup.mutate(code.value.trim())
}
</script>

<template>
  <div class="space-y-4">
    <form class="flex gap-2" @submit.prevent="submit">
      <InputText
        v-model="code"
        placeholder="Ячейка, штрихкод или SKU"
        class="min-w-0 flex-1 !py-3"
        autofocus
        autocomplete="off"
        aria-label="Код"
      />
      <Button type="submit" :loading="lookup.isPending.value" aria-label="Найти"
        ><ScanBarcode class="size-5"
      /></Button>
    </form>
    <Message v-if="error" severity="error" :closable="false">{{ error }}</Message>
    <div v-if="result" class="surface-card p-4">
      <div class="flex items-start gap-3">
        <component
          :is="result.kind === 'location' ? MapPin : Boxes"
          class="mt-0.5 size-6 shrink-0 text-primary"
        />
        <div class="min-w-0">
          <div class="font-semibold text-heading">{{ result.title }}</div>
          <div class="text-xs text-secondary">{{ result.subtitle }}</div>
        </div>
      </div>
      <ul v-if="result.rows.length" class="mt-4 divide-y divide-default">
        <li v-for="r in result.rows" :key="r.id" class="flex items-center justify-between gap-3 py-2 text-sm">
          <div class="min-w-0">
            <div class="truncate text-heading" :class="result.kind === 'product' && 'font-mono'">
              {{ r.primary }}
            </div>
            <div class="truncate text-xs text-secondary">{{ r.secondary }}</div>
          </div>
          <div class="text-right">
            <div class="tabular font-semibold text-heading">{{ formatNumber(r.quantity) }} шт</div>
            <div v-if="r.reserved" class="tabular text-xs text-amber-600">резерв {{ r.reserved }}</div>
          </div>
        </li>
      </ul>
      <p v-else class="mt-3 text-sm text-secondary">Пусто — товара нет.</p>
    </div>
    <EmptyState
      v-if="!result && !error"
      :icon="ScanBarcode"
      title="Отсканируйте код"
      description="Покажем содержимое ячейки или где лежит товар"
    />
  </div>
</template>
