<script setup lang="ts">
import { ref, watch } from 'vue'
import { useMutation, useQueryClient } from '@tanstack/vue-query'
import { Download, FileUp } from '@lucide/vue'
import { catalogApi } from '@/modules/catalog/api'
import type { ImportResult, ProductInput } from '@/modules/catalog/types'
import { useNotify } from '@/shared/composables/useNotify'

const visible = defineModel<boolean>('visible', { required: true })
const notify = useNotify()
const queryClient = useQueryClient()

const COLUMNS = [
  'sku',
  'name',
  'category',
  'barcode',
  'mp_barcode',
  'price',
  'weight_g',
  'volume_l',
  'expiry',
  'marking',
] as const
const TEMPLATE = `${COLUMNS.join(';')}\nTS-001;Термокружка 350 мл;Посуда;4601234567893;;890;320;0,6;нет;нет\n`

const text = ref('')
const fileName = ref('')
const parseError = ref<string | null>(null)
const result = ref<ImportResult | null>(null)

watch(visible, (open) => {
  if (!open) return
  text.value = ''
  fileName.value = ''
  parseError.value = null
  result.value = null
})

const yes = (v: string | undefined) =>
  ['да', 'yes', '1', 'true', '+'].includes((v ?? '').trim().toLowerCase())
const num = (v: string | undefined) => Number((v ?? '').replace(/\s/g, '').replace(',', '.')) || 0

function parse(source: string): ProductInput[] {
  const lines = source.split(/\r?\n/).filter((l) => l.trim())
  if (lines.length < 2) throw new Error('Файл пуст — нужна строка заголовка и хотя бы одна строка товара')
  const delimiter = lines[0]!.includes(';') ? ';' : ','
  const header = lines[0]!.split(delimiter).map((h) => h.trim().toLowerCase())
  const missing = ['sku', 'name', 'barcode'].filter((c) => !header.includes(c))
  if (missing.length) throw new Error(`Нет обязательных колонок: ${missing.join(', ')}`)
  return lines.slice(1).map((line) => {
    const cells = line.split(delimiter)
    const get = (key: string) => cells[header.indexOf(key)]?.trim()
    return {
      sku: get('sku') ?? '',
      name: get('name') ?? '',
      category: get('category') ?? '',
      barcode: get('barcode') ?? '',
      wbBarcode: get('mp_barcode') || undefined,
      price: num(get('price')),
      weightG: num(get('weight_g')),
      volumeLiters: num(get('volume_l')),
      tracksExpiry: yes(get('expiry')),
      requiresMarking: yes(get('marking')),
    }
  })
}

async function onFile(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]
  if (!file) return
  fileName.value = file.name
  text.value = await file.text()
}

function downloadTemplate() {
  const url = URL.createObjectURL(new Blob([`\uFEFF${TEMPLATE}`], { type: 'text/csv;charset=utf-8' }))
  const a = document.createElement('a')
  a.href = url
  a.download = 'products-template.csv'
  a.click()
  URL.revokeObjectURL(url)
}

const mutation = useMutation({
  mutationFn: (rows: ProductInput[]) => catalogApi.import(rows),
  onSuccess: (res) => {
    result.value = res
    if (res.created || res.updated) {
      notify.success('Импорт завершён', `Добавлено ${res.created}, обновлено ${res.updated}`)
      void queryClient.invalidateQueries({ queryKey: ['catalog'] })
    }
  },
  onError: (e) => notify.error(e, 'Импорт не выполнен'),
})

function submit() {
  parseError.value = null
  result.value = null
  try {
    mutation.mutate(parse(text.value))
  } catch (e) {
    parseError.value = (e as Error).message
  }
}
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    header="Импорт товаров из CSV"
    class="w-[calc(100vw-2rem)] max-w-2xl"
  >
    <div class="space-y-4">
      <p class="text-sm text-secondary">
        Выгрузите таблицу из Excel в CSV (разделитель «;» или «,»). Существующие артикулы обновятся, новые —
        добавятся.
      </p>
      <div class="flex flex-wrap gap-2">
        <label
          class="inline-flex cursor-pointer items-center gap-2 rounded-lg border border-default px-3 py-2 text-sm font-medium text-heading hover:bg-surface-50 dark:hover:bg-surface-800"
        >
          <FileUp class="size-4" />
          {{ fileName || 'Выбрать файл' }}
          <input type="file" accept=".csv,text/csv" class="sr-only" @change="onFile" />
        </label>
        <Button text severity="secondary" @click="downloadTemplate"><Download class="size-4" />Шаблон</Button>
      </div>
      <Textarea
        v-model="text"
        rows="6"
        class="w-full font-mono text-xs"
        placeholder="…или вставьте содержимое CSV сюда"
      />
      <Message v-if="parseError" severity="error" size="small">{{ parseError }}</Message>
      <div v-if="result" class="space-y-2">
        <Message :severity="result.errors.length ? 'warn' : 'success'" size="small">
          Добавлено {{ result.created }}, обновлено {{ result.updated }}, с ошибками
          {{ result.errors.length }}
        </Message>
        <ul v-if="result.errors.length" class="max-h-40 space-y-1 overflow-auto text-xs">
          <li v-for="e in result.errors" :key="e.row" class="text-red-600">
            Строка {{ e.row }}: {{ e.message }}
          </li>
        </ul>
      </div>
      <div class="flex justify-end gap-2">
        <Button label="Закрыть" severity="secondary" text @click="visible = false" />
        <Button
          label="Загрузить"
          :disabled="!text.trim()"
          :loading="mutation.isPending.value"
          @click="submit"
        />
      </div>
    </div>
  </Dialog>
</template>
