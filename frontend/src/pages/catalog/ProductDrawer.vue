<script setup lang="ts">
import { computed } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { Archive, ArchiveRestore, Pencil } from '@lucide/vue'
import { catalogApi } from '@/modules/catalog/api'
import type { ProductListItem } from '@/modules/catalog/types'
import { useNotify } from '@/shared/composables/useNotify'
import { formatMoney, formatNumber } from '@/shared/lib/format'
import EmptyState from '@/shared/ui/EmptyState.vue'
import InfoList from '@/shared/ui/InfoList.vue'

const props = defineProps<{ product: ProductListItem | null; editable: boolean }>()
const visible = defineModel<boolean>('visible', { required: true })
const emit = defineEmits<{ edit: [ProductListItem] }>()

const notify = useNotify()
const queryClient = useQueryClient()
const id = computed(() => props.product?.id)

const { data, isPending } = useQuery({
  queryKey: computed(() => ['catalog', 'product', id.value]),
  queryFn: () => catalogApi.product(id.value!),
  enabled: computed(() => !!id.value && visible.value),
})
const p = computed(() => (data.value?.id === id.value ? data.value : undefined))

const info = computed(() =>
  p.value
    ? [
        { label: 'Клиент', value: p.value.sellerName },
        { label: 'Категория', value: p.value.category },
        { label: 'Штрихкод', value: p.value.barcode },
        { label: 'ШК маркетплейса', value: p.value.wbBarcode ?? '—' },
        { label: 'Цена', value: formatMoney(p.value.price) },
        {
          label: 'Вес / объём',
          value: `${formatNumber(p.value.weightG)} г · ${formatNumber(p.value.volumeLiters)} л`,
        },
        { label: 'Сроки годности', value: p.value.tracksExpiry ? 'Учитываются (FEFO)' : 'Не учитываются' },
        { label: 'Маркировка', value: p.value.requiresMarking ? '«Честный знак»' : 'Не требуется' },
        { label: 'Продано за 30 дней', value: `${formatNumber(p.value.sold30d)} шт` },
      ]
    : [],
)

const archive = useMutation({
  mutationFn: () => catalogApi.setArchived(id.value!, !p.value!.archived),
  onSuccess: (item) => {
    notify.success(item.archived ? 'Товар в архиве' : 'Товар восстановлен', item.sku)
    void queryClient.invalidateQueries({ queryKey: ['catalog'] })
  },
  onError: (e) => notify.error(e),
})
</script>

<template>
  <Drawer
    v-model:visible="visible"
    position="right"
    class="!w-full sm:!w-[30rem]"
    :header="product?.sku ?? 'Товар'"
  >
    <div v-if="isPending && !p" class="space-y-3"><Skeleton v-for="i in 6" :key="i" height="2rem" /></div>
    <div v-else-if="p" class="space-y-5">
      <div>
        <div class="text-base font-semibold text-heading">{{ p.name }}</div>
        <div class="mt-2 flex flex-wrap gap-1.5">
          <Tag v-if="p.archived" value="В архиве" severity="secondary" />
          <Tag v-if="p.requiresMarking" value="Маркировка" severity="info" />
          <Tag v-if="p.tracksExpiry" value="Срок годности" severity="warn" />
        </div>
      </div>
      <div v-if="editable" class="flex flex-wrap gap-2">
        <Button size="small" severity="secondary" outlined @click="emit('edit', p)"
          ><Pencil class="size-4" />Редактировать</Button
        >
        <Button
          size="small"
          severity="secondary"
          outlined
          :loading="archive.isPending.value"
          @click="archive.mutate()"
        >
          <component :is="p.archived ? ArchiveRestore : Archive" class="size-4" />{{
            p.archived ? 'Вернуть из архива' : 'В архив'
          }}
        </Button>
      </div>
      <InfoList :items="info" />
      <div>
        <h3 class="mb-2 text-sm font-semibold text-heading">Остатки по складам</h3>
        <EmptyState v-if="!p.stock.length" compact title="Товара нет на складах" />
        <ul v-else class="divide-y divide-default rounded-lg border border-default">
          <li
            v-for="s in p.stock"
            :key="s.warehouseId"
            class="flex items-center justify-between gap-3 px-3 py-2.5 text-sm"
          >
            <span>
              <span class="block text-heading">{{ s.warehouseName }}</span>
              <span class="text-xs text-secondary">{{ s.locations }} яч.</span>
            </span>
            <span class="tabular text-right">
              <span class="block font-medium text-heading">{{ formatNumber(s.quantity) }} шт</span>
              <span v-if="s.reserved" class="text-xs text-sky-600"
                >резерв {{ formatNumber(s.reserved) }}</span
              >
            </span>
          </li>
        </ul>
      </div>
    </div>
  </Drawer>
</template>
