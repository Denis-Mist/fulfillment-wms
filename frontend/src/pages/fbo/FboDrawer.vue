<script setup lang="ts">
import { computed } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { Printer } from '@lucide/vue'
import { useConfirm } from 'primevue/useconfirm'
import { fboApi } from '@/modules/fbo/api'
import { FBO_NEXT, FBO_STATUSES } from '@/modules/fbo/labels'
import { useNotify } from '@/shared/composables/useNotify'
import { formatDate, formatDateTime } from '@/shared/lib/format'
import ChannelBadge from '@/shared/ui/ChannelBadge.vue'
import InfoList from '@/shared/ui/InfoList.vue'
import ToneBadge from '@/pages/stock/ToneBadge.vue'

const props = defineProps<{ id: string | null; canOperate: boolean; canManage: boolean }>()
const visible = defineModel<boolean>('visible', { required: true })
const notify = useNotify()
const confirm = useConfirm()
const qc = useQueryClient()

const { data: r, isPending } = useQuery({
  queryKey: computed(() => ['fbo-item', props.id]),
  queryFn: () => fboApi.get(props.id!),
  enabled: computed(() => !!props.id && visible.value),
})

const done = (title: string) => ({
  onSuccess: () => {
    notify.success(title)
    for (const key of ['fbo', 'fbo-item', 'fbo-summary']) void qc.invalidateQueries({ queryKey: [key] })
  },
  onError: (e: unknown) => notify.error(e),
})
const next = computed(() => (r.value ? FBO_NEXT[r.value.status] : undefined))
const advance = useMutation({
  mutationFn: () => fboApi.advance(props.id!, next.value!.to),
  ...done('Статус заявки обновлён'),
})
const submit = useMutation({
  mutationFn: () => fboApi.submit(props.id!),
  ...done('Заявка отправлена на фулфилмент'),
})
const cancel = useMutation({ mutationFn: () => fboApi.cancel(props.id!), ...done('Заявка отменена') })
function askCancel() {
  confirm.require({
    header: 'Отменить заявку?',
    message: 'Фулфилмент не будет собирать эту поставку.',
    acceptProps: { label: 'Отменить заявку', severity: 'danger' },
    rejectProps: { label: 'Назад', severity: 'secondary', outlined: true },
    accept: () => cancel.mutate(),
  })
}
function printLabels() {
  notify.success(
    'Этикетки сформированы',
    `${r.value?.boxes ?? 0} коробов · ШК поставки ${r.value?.supplyId ?? ''}`,
  )
}

const info = computed(() =>
  r.value
    ? [
        { label: 'Маркетплейс', value: r.value.marketplace === 'wb' ? 'Wildberries' : 'Ozon' },
        { label: 'Склад назначения', value: r.value.destination },
        { label: 'Клиент', value: r.value.sellerName },
        { label: 'Склад ФЦ', value: r.value.warehouseName },
        { label: 'Плановая отгрузка', value: formatDate(r.value.plannedAt) },
        { label: 'Номер поставки МП', value: r.value.supplyId ?? '—' },
        { label: 'Коробов', value: String(r.value.boxes) },
      ]
    : [],
)
</script>

<template>
  <Drawer
    v-model:visible="visible"
    position="right"
    class="!w-full sm:!w-[34rem]"
    :header="r ? `Поставка ${r.number}` : 'Поставка FBO'"
  >
    <div v-if="isPending && !r" class="space-y-3"><Skeleton v-for="i in 6" :key="i" height="2rem" /></div>
    <div v-else-if="r" class="space-y-5">
      <div class="flex flex-wrap items-center gap-2">
        <ToneBadge :tone="FBO_STATUSES[r.status].tone" :label="FBO_STATUSES[r.status].label" />
        <ChannelBadge :channel="r.marketplace" />
        <span class="text-xs text-secondary">собрано {{ r.packed }} из {{ r.units }} шт</span>
      </div>
      <InfoList :items="info" />

      <div>
        <h3 class="mb-2 text-sm font-semibold text-heading">Состав поставки</h3>
        <ul class="divide-y divide-default rounded-lg border border-default">
          <li v-for="l in r.lines" :key="l.productId" class="flex items-center justify-between gap-3 p-3">
            <div class="min-w-0 flex-1">
              <div class="truncate text-sm font-medium text-heading">{{ l.name }}</div>
              <div class="text-xs text-secondary">
                {{ l.sku
                }}<template v-if="r.status !== 'shipped'"> · на складе {{ l.available }} шт</template>
              </div>
            </div>
            <div class="text-right text-sm whitespace-nowrap tabular">
              <span :class="l.packed >= l.quantity ? 'text-emerald-600' : 'text-heading'">{{
                l.packed
              }}</span>
              <span class="text-secondary"> / {{ l.quantity }}</span>
            </div>
          </li>
        </ul>
      </div>

      <div>
        <h3 class="mb-2 text-sm font-semibold text-heading">История</h3>
        <ol class="space-y-1.5 text-sm">
          <li v-for="(h, i) in r.history" :key="i" class="flex flex-wrap justify-between gap-x-3">
            <span class="text-heading">{{ FBO_STATUSES[h.status].label }}</span>
            <span class="text-xs text-secondary">{{ h.by }} · {{ formatDateTime(h.at) }}</span>
          </li>
        </ol>
      </div>

      <div class="flex flex-wrap gap-2">
        <template v-if="canOperate">
          <Button
            v-if="next"
            :label="next.label"
            :loading="advance.isPending.value"
            @click="advance.mutate()"
          />
          <Button
            v-if="['assembling', 'ready'].includes(r.status)"
            severity="secondary"
            outlined
            @click="printLabels"
          >
            <Printer class="size-4" /> Этикетки коробов
          </Button>
        </template>
        <template v-if="canManage">
          <Button
            v-if="r.status === 'draft'"
            label="Отправить на ФЦ"
            :loading="submit.isPending.value"
            @click="submit.mutate()"
          />
          <Button
            v-if="['draft', 'confirmed'].includes(r.status)"
            label="Отменить"
            severity="danger"
            outlined
            :loading="cancel.isPending.value"
            @click="askCancel"
          />
        </template>
      </div>
    </div>
  </Drawer>
</template>
