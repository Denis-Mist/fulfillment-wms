<script setup lang="ts">
/**
 * Ловит ошибки всех активных запросов страницы (useQuery) и показывает ErrorState с повтором
 * вместо вводящего в заблуждение «пусто». Страница остаётся смонтированной (v-show), чтобы
 * её наблюдатели запросов не пропадали и повтор не зацикливался.
 * Запросы шапки помечаются meta.shell, запросы со своей обработкой ошибки — meta.localError.
 */
import { computed, onBeforeUnmount, ref, shallowRef } from 'vue'
import { useQueryClient, type Query } from '@tanstack/vue-query'
import ErrorState from './ErrorState.vue'

const client = useQueryClient()
const cache = client.getQueryCache()
const failed = shallowRef<Query[]>([])
const retrying = ref(false)

const isTracked = (q: Query) =>
  q.state.status === 'error' && q.getObserversCount() > 0 && !q.meta?.shell && !q.meta?.localError

function collect() {
  failed.value = cache.getAll().filter(isTracked)
}
const unsubscribe = cache.subscribe(() => queueMicrotask(collect))
onBeforeUnmount(unsubscribe)
collect()

const error = computed(() => failed.value[0]?.state.error)

async function retry() {
  retrying.value = true
  try {
    await client.refetchQueries({ predicate: (q) => isTracked(q as Query) })
  } finally {
    retrying.value = false
  }
}
</script>

<template>
  <ErrorState v-if="failed.length" :error="error" :retrying="retrying" @retry="retry" />
  <div v-show="!failed.length">
    <slot />
  </div>
</template>
