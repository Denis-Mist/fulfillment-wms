<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { PanelLeftClose, PanelLeftOpen } from '@lucide/vue'
import { cabinetDef, routeName } from '@/app/sections'
import { CABINET_LABELS, ROLE_LABELS } from '@/modules/auth/roles'
import { useSessionStore } from '@/stores/session'
import AppLogo from '@/shared/ui/AppLogo.vue'

const props = withDefaults(defineProps<{ collapsed?: boolean; mobile?: boolean }>(), {
  collapsed: false,
  mobile: false,
})
const emit = defineEmits<{ toggle: []; navigate: [] }>()

const route = useRoute()
const session = useSessionStore()

const cabinet = computed(() => session.cabinet ?? 'fc')
const groups = computed(() =>
  cabinetDef(cabinet.value)
    .groups.map((group) => ({
      ...group,
      sections: group.sections.filter(
        (section) => !section.hidden && (!section.roles || (session.role && section.roles.includes(session.role))),
      ),
    }))
    .filter((group) => group.sections.length),
)

const isCollapsed = computed(() => props.collapsed && !props.mobile)
const orgName = computed(() => session.activeMembership?.organizationName ?? 'Платформа')
const roleLabel = computed(() => (session.role ? ROLE_LABELS[session.role] : ''))
</script>

<template>
  <aside
    class="flex h-full flex-col bg-white transition-[width] duration-200 dark:bg-surface-900"
    :class="[
      mobile ? 'w-full' : 'sticky top-0 h-screen border-r border-surface-200/80 dark:border-surface-800',
      isCollapsed ? 'w-[76px]' : mobile ? '' : 'w-[264px]',
    ]"
  >
    <div class="flex h-16 shrink-0 items-center px-5">
      <AppLogo :compact="isCollapsed" />
    </div>

    <div v-if="!isCollapsed" class="px-3 pb-3">
      <div class="rounded-xl bg-surface-50 px-3 py-2.5 dark:bg-surface-800/60">
        <div class="text-[11px] font-medium tracking-wide text-secondary uppercase">{{ CABINET_LABELS[cabinet] }}</div>
        <div class="truncate text-sm font-semibold text-heading">{{ orgName }}</div>
        <div class="truncate text-xs text-secondary">{{ roleLabel }}</div>
      </div>
    </div>

    <nav class="flex-1 space-y-5 overflow-y-auto px-3 py-2">
      <div v-for="(group, index) in groups" :key="group.label ?? index">
        <div
          v-if="group.label && !isCollapsed"
          class="mb-1.5 px-3 text-[11px] font-semibold tracking-wider text-surface-400 uppercase"
        >
          {{ group.label }}
        </div>
        <div v-else-if="group.label" class="mx-3 mb-2 border-t border-surface-100 dark:border-surface-800" />
        <div class="space-y-0.5">
          <RouterLink
            v-for="section in group.sections"
            :key="section.name"
            v-tooltip.right="isCollapsed ? section.title : undefined"
            :to="{ name: routeName(cabinet, section.name) }"
            class="flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors"
            :class="[
              route.meta.navName === routeName(cabinet, section.name)
                ? 'bg-primary-50 text-primary-700 dark:bg-primary-500/15 dark:text-primary-300'
                : 'text-surface-600 hover:bg-surface-100 hover:text-surface-900 dark:text-surface-400 dark:hover:bg-surface-800 dark:hover:text-surface-0',
              isCollapsed && 'justify-center',
            ]"
            @click="emit('navigate')"
          >
            <component :is="section.icon" class="size-[18px] shrink-0" />
            <template v-if="!isCollapsed">
              <span class="flex-1 truncate">{{ section.title }}</span>
              <span
                v-if="section.badge"
                class="rounded-md bg-gradient-to-r from-indigo-500 to-violet-500 px-1.5 py-px text-[10px] font-bold text-white"
              >
                {{ section.badge }}
              </span>
            </template>
          </RouterLink>
        </div>
      </div>
    </nav>

    <div v-if="!mobile" class="border-t border-surface-100 p-3 dark:border-surface-800">
      <button
        type="button"
        class="flex w-full items-center gap-3 rounded-lg px-3 py-2 text-sm text-surface-500 transition-colors hover:bg-surface-100 hover:text-surface-800 dark:hover:bg-surface-800 dark:hover:text-surface-100"
        :class="isCollapsed && 'justify-center'"
        @click="emit('toggle')"
      >
        <component :is="isCollapsed ? PanelLeftOpen : PanelLeftClose" class="size-[18px]" />
        <span v-if="!isCollapsed">Свернуть меню</span>
      </button>
    </div>
  </aside>
</template>
