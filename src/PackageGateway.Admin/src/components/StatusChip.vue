<template>
  <Badge :variant="colors[props.status.toUpperCase()] || 'secondary'" class="status-chip uppercase">
    {{ te(`status.${status.toUpperCase()}`) ? t(`status.${status.toUpperCase()}`) : status.replace(/_/g, ' ') }}
  </Badge>
</template>

<script setup lang="ts">
import type { BadgeVariants } from '@aditify/ui';
import { Badge } from '@aditify/ui';
import { useI18n } from 'vue-i18n';

const props = defineProps<{ status: string }>();
const { t, te } = useI18n({ useScope: 'local' });
const colors: Record<string, BadgeVariants['variant']> = { APPROVED: 'success', HEALTHY: 'success', BLOCKED: 'destructive', QUARANTINED: 'destructive', MANUAL_REVIEW: 'warning', PENDING: 'info', SCANNING: 'info' };
</script>

<style scoped>
.status-chip {
  font-size: 0.6875rem;
  letter-spacing: 0.04em;
}
</style>

<i18n lang="json">
{"en":{"status":{"APPROVED":"approved","HEALTHY":"healthy","BLOCKED":"blocked","QUARANTINED":"quarantined","MANUAL_REVIEW":"manual review","PENDING":"pending","SCANNING":"scanning","UNKNOWN":"unknown"}},"sv":{"status":{"APPROVED":"godkänd","HEALTHY":"fungerar","BLOCKED":"blockerad","QUARANTINED":"i karantän","MANUAL_REVIEW":"manuell granskning","PENDING":"väntande","SCANNING":"skannas","UNKNOWN":"okänd"}}}
</i18n>
