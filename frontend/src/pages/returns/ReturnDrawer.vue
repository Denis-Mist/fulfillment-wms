<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { Camera } from '@lucide/vue'
import { returnsApi } from '@/modules/returns/api'
import { RETURN_GRADES, RETURN_REASONS, RETURN_STATUSES } from '@/modules/returns/labels'
import type { ReturnGrade } from '@/modules/returns/types'
import { useNotify } from '@/shared/composables/useNotify'
import { formatDateTime } from '@/shared/lib/format'
import ChannelBadge from '@/shared/ui/ChannelBadge.vue'
import InfoList from '@/shared/ui/InfoList.vue'
import ToneBadge from '@/pages/stock/ToneBadge.vue'

const props = defineProps<{ id: string | null; canOperate: boolean }>()
const visible = defineModel<boolean>('visible', { required: true })
const notify = useNotify()
const qc = useQueryClient()

const { data: r, isPending } = useQuery({
  queryKey: computed(() => ['return', props.id]),
  queryFn: () => returnsApi.get(props.id!),
  enabled: computed(() => !!props.id && visible.value),
})

const grades = ref<Record<string, ReturnGrade>>({})
const comment = ref('')
watch(r, (v) => {
  grades.value = Object.fromEntries(
    (v?.lines ?? []).filter((l) => l.grade).map((l) => [l.productId, l.grade!]),
  )
  comment.value = v?.comment ?? ''
})
const gradeOptions = Object.entries(RETURN_GRADES).map(([value, m]) => ({ value, label: m.label }))

const done = (title: string) => ({
  onSuccess: () => {
    notify.success(title)
    void qc.invalidateQueries({ queryKey: ['returns'] })
    void qc.invalidateQueries({ queryKey: ['return'] })
    void qc.invalidateQueries({ queryKey: ['returns-summary'] })
  },
  onError: (e: unknown) => notify.error(e),
})
const receive = useMutation({
  mutationFn: () => returnsApi.receive(props.id!),
  ...done('Возврат принят на склад'),
})
const inspect = useMutation({
  mutationFn: () => returnsApi.inspect(props.id!, { grades: grades.value, comment: comment.value }),
  ...done('Оценка сохранена'),
})
const close = useMutation({
  mutationFn: () => returnsApi.close(props.id!),
  ...done('Возврат закрыт: годный товар возвращён в остаток'),
})

const info = computed(() =>
  r.value
    ? [
        { label: 'Заказ', value: r.value.orderNumber },
        { label: 'Клиент', value: r.value.sellerName },
        { label: 'Склад', value: r.value.warehouseName },
        { label: 'Причина', value: RETURN_REASONS[r.value.reason] },
        { label: 'Создан', value: formatDateTime(r.value.createdAt) },
        { label: 'Принят', value: r.value.receivedAt ? formatDateTime(r.value.receivedAt) : '—' },
        {
          label: 'Оценил',
          value: r.value.inspectedBy
            ? `${r.value.inspectedBy}, ${formatDateTime(r.value.inspectedAt!)}`
            : '—',
        },
      ]
    : [],
)
</script>

<template>
  <Drawer
    v-model:visible="visible"
    position="right"
    class="!w-full sm:!w-[32rem]"
    :header="r ? `Возврат ${r.number}` : 'Возврат'"
  >
    <div v-if="isPending && !r" class="space-y-3"><Skeleton v-for="i in 6" :key="i" height="2rem" /></div>
    <div v-else-if="r" class="space-y-5">
      <div class="flex flex-wrap items-center gap-2">
        <ToneBadge :tone="RETURN_STATUSES[r.status].tone" :label="RETURN_STATUSES[r.status].label" />
        <ChannelBadge :channel="r.channel" />
        <span v-if="r.photos" class="inline-flex items-center gap-1 text-xs text-secondary"
          ><Camera class="size-3.5" /> {{ r.photos }} фото</span
        >
      </div>
      <InfoList :items="info" />

      <div>
        <h3 class="mb-2 text-sm font-semibold text-heading">Позиции и состояние</h3>
        <ul class="divide-y divide-default rounded-lg border border-default">
          <li
            v-for="l in r.lines"
            :key="l.productId"
            class="flex flex-wrap items-center justify-between gap-2 p-3"
          >
            <div class="min-w-0 flex-1">
              <div class="truncate text-sm font-medium text-heading">{{ l.name }}</div>
              <div class="text-xs text-secondary">{{ l.sku }} · {{ l.quantity }} шт</div>
            </div>
            <Select
              v-if="canOperate && r.status === 'received'"
              v-model="grades[l.productId]"
              :options="gradeOptions"
              option-label="label"
              option-value="value"
              placeholder="Состояние"
              class="w-full sm:w-44"
            />
            <ToneBadge
              v-else-if="l.grade"
              :tone="RETURN_GRADES[l.grade].tone"
              :label="RETURN_GRADES[l.grade].label"
            />
            <span v-else class="text-xs text-secondary">не оценён</span>
          </li>
        </ul>
      </div>

      <div v-if="canOperate && r.status === 'received'">
        <label class="mb-1 block text-sm font-medium">Комментарий к оценке</label>
        <Textarea
          v-model="comment"
          rows="2"
          class="w-full"
          placeholder="Например: вскрыта упаковка, следов использования нет"
        />
      </div>
      <Message v-else-if="r.comment" severity="secondary" size="small">{{ r.comment }}</Message>

      <div v-if="canOperate" class="flex flex-wrap gap-2">
        <Button
          v-if="r.status === 'expected'"
          label="Принять на склад"
          :loading="receive.isPending.value"
          @click="receive.mutate()"
        />
        <Button
          v-if="r.status === 'received'"
          label="Сохранить оценку"
          :loading="inspect.isPending.value"
          @click="inspect.mutate()"
        />
        <Button
          v-if="r.status === 'inspected'"
          label="Закрыть и вернуть в остаток"
          :loading="close.isPending.value"
          @click="close.mutate()"
        />
      </div>
    </div>
  </Drawer>
</template>
