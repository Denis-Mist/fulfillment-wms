<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { Plus } from '@lucide/vue'
import { adminApi } from '@/modules/admin/api'
import { REFERENCE_KINDS } from '@/modules/admin/labels'
import type { ReferenceInput, ReferenceKind } from '@/modules/admin/types'
import { z } from 'zod'
import { useFormValidation } from '@/shared/composables/useFormValidation'
import { useNotify } from '@/shared/composables/useNotify'
import PageHeader from '@/shared/ui/PageHeader.vue'
import SectionCard from '@/shared/ui/SectionCard.vue'

const queryClient = useQueryClient()
const notify = useNotify()

const kind = ref<ReferenceKind>('units')
const kindOptions = (Object.keys(REFERENCE_KINDS) as ReferenceKind[]).map((value) => ({
  value,
  label: REFERENCE_KINDS[value].label,
}))

const { data, isPending } = useQuery({
  queryKey: computed(() => ['admin-references', kind.value]),
  queryFn: () => adminApi.references(kind.value),
})

const dialog = ref(false)
const form = reactive<ReferenceInput>({ kind: 'units', code: '', name: '', note: '' })
const schema = z.object({
  kind: z.string().min(1, 'Выберите справочник'),
  code: z
    .string()
    .trim()
    .min(1, 'Укажите код')
    .refine((v) => /^[A-Za-z0-9_]{2,12}$/.test(v), 'Код: 2–12 латинских букв, цифр или «_»'),
  name: z.string().trim().min(1, 'Укажите название').max(80, 'Не длиннее 80 символов'),
})
const v = useFormValidation(schema, form)
watch(dialog, (open) => {
  if (!open) return
  Object.assign(form, { kind: kind.value, code: '', name: '', note: '' })
  v.reset()
})
function submit() {
  if (v.validate()) create.mutate()
}

const invalidate = () => queryClient.invalidateQueries({ queryKey: ['admin-references'] })
const create = useMutation({
  mutationFn: () => adminApi.createReference(form),
  onSuccess: (item) => {
    void invalidate()
    notify.success(`Добавлено: ${item.name}`)
    dialog.value = false
  },
  onError: (error) => v.setServerError(error, 'code') || notify.error(error),
})
const toggle = useMutation({
  mutationFn: adminApi.toggleReference,
  onSuccess: () => void invalidate(),
  onError: (error) => notify.error(error),
})
</script>

<template>
  <div>
    <PageHeader
      title="Справочники"
      description="Общие для платформы справочники: используются всеми организациями"
    >
      <template #actions>
        <Button label="Запись" @click="dialog = true">
          <template #icon><Plus class="size-4" /></template>
        </Button>
      </template>
    </PageHeader>

    <div class="no-scrollbar -mx-1 mb-4 overflow-x-auto px-1">
      <SelectButton
        v-model="kind"
        :options="kindOptions"
        option-label="label"
        option-value="value"
        :allow-empty="false"
        class="whitespace-nowrap"
      />
    </div>

    <SectionCard :title="REFERENCE_KINDS[kind].label" :description="REFERENCE_KINDS[kind].description" flush>
      <div v-if="isPending" class="space-y-2 p-5"><Skeleton v-for="i in 5" :key="i" height="2.5rem" /></div>
      <ul v-else class="divide-y divide-surface-100 dark:divide-surface-800">
        <li
          v-for="item in data"
          :key="item.id"
          class="flex items-center gap-3 px-5 py-3"
          :class="!item.isActive && 'opacity-55'"
        >
          <span class="w-20 shrink-0 truncate font-mono text-xs font-semibold text-secondary">{{
            item.code
          }}</span>
          <div class="min-w-0 flex-1">
            <div class="truncate text-sm font-medium text-heading">{{ item.name }}</div>
            <div v-if="item.note" class="truncate text-xs text-secondary">{{ item.note }}</div>
          </div>
          <ToggleSwitch
            :model-value="item.isActive"
            :aria-label="`${item.name}: ${item.isActive ? 'активна' : 'в архиве'}`"
            @update:model-value="toggle.mutate(item.id)"
          />
        </li>
      </ul>
    </SectionCard>

    <Dialog
      v-model:visible="dialog"
      modal
      header="Новая запись справочника"
      class="w-[calc(100vw-2rem)] max-w-md"
    >
      <form id="ref-form" class="space-y-4" novalidate @submit.prevent="submit">
        <div class="space-y-1.5">
          <label for="ref-kind" class="text-sm font-medium text-heading">Справочник</label>
          <Select
            id="ref-kind"
            v-model="form.kind"
            :options="kindOptions"
            option-label="label"
            option-value="value"
            fluid
          />
        </div>
        <div class="space-y-1.5">
          <label for="ref-code" class="text-sm font-medium text-heading">Код</label>
          <InputText
            id="ref-code"
            v-model="form.code"
            placeholder="Например, PALLET_EU"
            fluid
            :invalid="!!v.show('code')"
            @blur="v.touched.code = true"
          />
          <small v-if="v.show('code')" class="text-red-600">{{ v.show('code') }}</small>
        </div>
        <div class="space-y-1.5">
          <label for="ref-name" class="text-sm font-medium text-heading">Название</label>
          <InputText
            id="ref-name"
            v-model="form.name"
            fluid
            :invalid="!!v.show('name')"
            @blur="v.touched.name = true"
          />
          <small v-if="v.show('name')" class="text-red-600">{{ v.show('name') }}</small>
        </div>
        <div class="space-y-1.5">
          <label for="ref-note" class="text-sm font-medium text-heading">Примечание</label>
          <InputText id="ref-note" v-model="form.note" fluid />
        </div>
      </form>
      <template #footer>
        <Button label="Отмена" severity="secondary" outlined @click="dialog = false" />
        <Button label="Добавить" type="submit" form="ref-form" :loading="create.isPending.value" />
      </template>
    </Dialog>
  </div>
</template>
