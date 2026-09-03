<template>
  <div class="page">
    <div class="flex flex-wrap items-end justify-between gap-4">
      <div>
        <p class="eyebrow">
          {{ t('eyebrow') }}
        </p><h1>{{ t('title') }}</h1><p class="page-lead">
          {{ t('lead') }}
        </p>
      </div>
      <div class="audit-filters flex gap-3">
        <Field class="w-[300px]">
          <FieldLabel for="entity-filter">
            {{ t('entityType') }}
          </FieldLabel><Select :model-value="filter || '__all'" @update:model-value="selectEntityType">
            <SelectTrigger id="entity-filter">
              <SelectValue :placeholder="t('allEntityTypes')" />
            </SelectTrigger><SelectContent>
              <SelectItem value="__all">
                {{ t('allEntityTypes') }}
              </SelectItem><SelectItem v-for="entityType in entityTypes" :key="entityType" :value="entityType">
                {{ entityType }}
              </SelectItem>
            </SelectContent>
          </Select>
        </Field>
        <Button variant="outline" :disabled="loading" @click="load">
          <Spinner v-if="loading" /><RefreshCw v-else />{{ t('refresh') }}
        </Button>
      </div>
    </div><Alert v-if="error" variant="destructive" class="mt-5">
      <CircleAlert /><AlertDescription>{{ error }}</AlertDescription>
    </Alert><Card class="mt-6 py-0">
      <Table>
        <TableHeader><TableRow><TableHead>{{ t('time') }}</TableHead><TableHead>{{ t('actor') }}</TableHead><TableHead>{{ t('action') }}</TableHead><TableHead>{{ t('entity') }}</TableHead><TableHead>{{ t('details') }}</TableHead></TableRow></TableHeader><TableBody>
          <TableRow v-for="event in events" :key="event.id">
            <TableCell class="audit-time">
              {{ formatDateTime(event.timestamp) }}
            </TableCell><TableCell>{{ event.actor }}</TableCell><TableCell><strong>{{ event.action }}</strong></TableCell><TableCell class="audit-entity">
              <Badge variant="secondary">
                {{ event.entityType }}
              </Badge><code class="audit-entity-id">{{ event.entityId }}</code>
            </TableCell><TableCell>{{ event.description }}</TableCell>
          </TableRow><TableRow v-if="!events.length">
            <TableCell colspan="5" class="audit-empty text-muted-foreground">
              {{ t('empty') }}
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Card>
  </div>
</template>

<script setup lang="ts">
import type { AcceptableValue } from 'reka-ui';
import { Alert, AlertDescription, Badge, Button, Card, Field, FieldLabel, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Spinner, Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@aditify/ui';
import { CircleAlert, RefreshCw } from '@lucide/vue';
import { onMounted, ref } from 'vue'; import { useI18n } from 'vue-i18n'; import { graphql } from '@/api/graphql'; import { formatDateTime } from '@/utils/dateTime';

const { t } = useI18n({ useScope: 'local' });

interface Event { id: string; timestamp: string; actor: string; action: string; entityType: string; entityId: string; description: string }

const events = ref<Event[]>([]); const entityTypes = ref<string[]>([]); const filter = ref<string>(); const error = ref(''); const loading = ref(false);

async function load() {
  loading.value = true;

  try {
    const result = await graphql<{ auditEventEntityTypes: string[]; auditEvents: { nodes: Event[] } }>(`query($type:String){auditEventEntityTypes auditEvents(entityType:$type,first:100){nodes{id timestamp actor action entityType entityId description}}}`, { type: filter.value || null });

    entityTypes.value = result.auditEventEntityTypes;
    events.value = result.auditEvents.nodes;
  }
  catch (e) { error.value = (e as Error).message; }
  finally { loading.value = false; }
}
async function selectEntityType(value: AcceptableValue) {
  filter.value = typeof value === 'string' && value !== '__all' ? value : undefined;
  await load();
}
onMounted(load);
</script>

<style scoped>
.audit-filters {
  align-items: flex-end;
}

.audit-entity-id {
  display: block;
  margin-top: 0.25rem;
  color: var(--muted-foreground);
  font-size: 0.75rem;
}

.audit-time,
.audit-entity {
  white-space: nowrap;
}

.audit-empty {
  padding-block: 2rem;
  text-align: center;
}
</style>

<i18n lang="json">
{"en":{"eyebrow":"Immutable accountability","title":"Audit history","lead":"Administrative decisions and package state changes are committed with their corresponding operation.","entityType":"Entity type","allEntityTypes":"All entity types","noEntityTypes":"No entity types available","refresh":"Refresh","clearFilter":"Show all entity types","time":"Time","actor":"Actor","action":"Action","entity":"Entity","details":"Details","empty":"No audit events match the current filter."},"sv":{"eyebrow":"Oföränderlig spårbarhet","title":"Revisionshistorik","lead":"Administrativa beslut och ändringar av paketstatus sparas tillsammans med motsvarande åtgärd.","entityType":"Entitetstyp","allEntityTypes":"Alla entitetstyper","noEntityTypes":"Inga entitetstyper tillgängliga","refresh":"Uppdatera","clearFilter":"Visa alla entitetstyper","time":"Tid","actor":"Aktör","action":"Åtgärd","entity":"Entitet","details":"Detaljer","empty":"Inga revisionshändelser matchar det aktuella filtret."}}
</i18n>
