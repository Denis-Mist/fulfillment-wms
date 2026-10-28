<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { billingApi } from '@/modules/billing/api'
import { TARIFF_FIELDS } from '@/modules/billing/labels'
import type { Tariff } from '@/modules/billing/types'
import { useNotify } from '@/shared/composables/useNotify'
import { formatMoney } from '@/shared/lib/format'
import SectionCard from '@/shared/ui/SectionCard.vue'

const props = defineProps<{ contractId: string; title: string; editable: boolean }>()
const notify = useNotify()
const qc = useQueryClient()

const { data, isPending } = useQuery({
  queryKey: computed(() => ['tariff', props.contractId]),
  queryFn: () => billingApi.tariff(props.contractId),
})
const draft = ref<Tariff | null>(null)
watch(data, (v) => (draft.value = v ? { ...v } : null), { immediate: true })
const dirty = computed(
  () =>
    !!data.value && !!draft.value && TARIFF_FIELDS.some((f) => data.value![f.key] !== draft.value![f.key]),
)
const save = useMutation({
  mutationFn: () => billingApi.saveTariff(props.contractId, draft.value!),
  onSuccess: () => {
    notify.success('Тариф сохранён', 'Новые ставки применяются к начислениям')
    for (const key of ['tariff', 'billing-summary', 'billing-charges', 'billing-acts'])
      void qc.invalidateQueries({ queryKey: [key] })
  },
  onError: (e: unknown) => notify.error(e),
})
</script>

<template>
  <SectionCard :title="title" description="Ставки договора, по которым считаются автоначисления">
    <template v-if="editable && dirty" #actions>
      <Button
        label="Отменить"
        text
        severity="secondary"
        size="small"
        @click="draft = data ? { ...data } : null"
      />
      <Button label="Сохранить" size="small" :loading="save.isPending.value" @click="save.mutate()" />
    </template>
    <div v-if="isPending || !draft" class="space-y-2">
      <Skeleton v-for="i in 6" :key="i" height="2.25rem" />
    </div>
    <ul v-else class="divide-y divide-default">
      <li
        v-for="f in TARIFF_FIELDS"
        :key="f.key"
        class="flex flex-wrap items-center justify-between gap-2 py-2.5"
      >
        <div>
          <div class="text-sm font-medium text-heading">{{ f.label }}</div>
          <div class="text-xs text-secondary">{{ f.unit }}</div>
        </div>
        <InputNumber
          v-if="editable"
          v-model="draft[f.key]"
          :min="0"
          :step="f.step"
          :min-fraction-digits="2"
          :max-fraction-digits="2"
          show-buttons
          button-layout="horizontal"
          input-class="!w-24 text-right"
          size="small"
          :aria-label="f.label"
        />
        <span v-else class="font-medium text-heading tabular">{{ formatMoney(draft[f.key]) }}</span>
      </li>
    </ul>
  </SectionCard>
</template>
