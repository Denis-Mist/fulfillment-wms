<script setup lang="ts">
import { useStorage } from '@vueuse/core'
import PageHeader from '@/shared/ui/PageHeader.vue'
import ForecastTab from './ForecastTab.vue'
import KpiTab from './KpiTab.vue'
import SlottingTab from './SlottingTab.vue'

const tab = useStorage('wms.analytics.tab', 'forecast')
</script>

<template>
  <div>
    <PageHeader
      title="Аналитика и прогнозы"
      description="Рекомендации применяются только после подтверждения; любой алгоритм можно заменить базовым"
    />
    <Tabs v-model:value="tab">
      <TabList>
        <Tab value="forecast">Прогноз и персонал</Tab>
        <Tab value="slotting">Размещение SKU</Tab>
        <Tab value="kpi">Отчёты и KPI</Tab>
      </TabList>
      <TabPanels class="!bg-transparent !px-0 !pt-5">
        <TabPanel value="forecast"><ForecastTab /></TabPanel>
        <TabPanel value="slotting"><SlottingTab /></TabPanel>
        <TabPanel value="kpi"><KpiTab /></TabPanel>
      </TabPanels>
    </Tabs>
  </div>
</template>
