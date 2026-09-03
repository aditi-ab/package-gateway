<template>
  <div v-if="loading" class="flex justify-center py-5">
    <Spinner />
  </div><Alert v-else-if="error" variant="destructive" class="mt-5">
    <CircleAlert /><AlertDescription>{{ error }}</AlertDescription>
  </Alert><Accordion v-else type="multiple" class="mt-5 rounded-lg border px-4">
    <AccordionItem value="policy-reasons">
      <AccordionTrigger>{{ t('policyReasons', { count: rules.length }) }}</AccordionTrigger><AccordionContent>
        <ItemGroup v-if="rules.length">
          <Item v-for="rule in rules" :key="rule.id" size="sm">
            <ItemContent><ItemTitle>{{ ruleName(rule) }}</ItemTitle><ItemDescription>{{ rule.reason }}</ItemDescription></ItemContent><ItemActions>
              <Badge class="action-chip uppercase" :variant="rule.isHardBlock || rule.action === 'BLOCK' ? 'destructive' : rule.action === 'QUARANTINE' || rule.action === 'MANUAL_REVIEW' ? 'warning' : 'secondary'">
                {{ actionLabel(rule.action) }}
              </Badge>
            </ItemActions>
          </Item>
        </ItemGroup><div v-else class="text-muted-foreground">
          {{ t('noPolicyReasons') }}
        </div>
      </AccordionContent>
    </AccordionItem><AccordionItem value="findings">
      <AccordionTrigger>{{ t('findings', { count: findings.length }) }}</AccordionTrigger><AccordionContent>
        <ItemGroup v-if="findings.length">
          <Item v-for="finding in findings" :key="finding.id" size="sm">
            <ItemMedia>
              <Badge variant="outline">
                +{{ finding.riskScore }}
              </Badge>
            </ItemMedia><ItemContent><ItemTitle>{{ finding.title }}</ItemTitle><ItemDescription>{{ finding.description }} · {{ finding.source }}</ItemDescription></ItemContent><ItemActions>
              <Badge :variant="finding.isHardBlock || finding.severity === 'CRITICAL' ? 'destructive' : finding.severity === 'HIGH' ? 'warning' : 'secondary'">
                {{ finding.severity }}
              </Badge>
            </ItemActions>
          </Item>
        </ItemGroup><div v-else class="text-muted-foreground">
          {{ t('noFindings') }}
        </div>
      </AccordionContent>
    </AccordionItem><AccordionItem value="audit">
      <AccordionTrigger>{{ t('audit', { count: events.length }) }}</AccordionTrigger><AccordionContent>
        <ItemGroup v-if="events.length">
          <Item v-for="event in events" :key="event.id" size="sm">
            <ItemContent><ItemTitle>{{ event.action }}</ItemTitle><ItemDescription>{{ event.description }} · {{ event.actor }} · {{ formatDateTime(event.timestamp) }}</ItemDescription></ItemContent>
          </Item>
        </ItemGroup><div v-else class="text-muted-foreground">
          {{ t('noAudit') }}
        </div>
      </AccordionContent>
    </AccordionItem>
  </Accordion>
</template>

<script setup lang="ts">
import { Accordion, AccordionContent, AccordionItem, AccordionTrigger, Alert, AlertDescription, Badge, Item, ItemActions, ItemContent, ItemDescription, ItemGroup, ItemMedia, ItemTitle, Spinner } from '@aditify/ui';
import { CircleAlert } from '@lucide/vue';
import { ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { graphql } from '@/api/graphql';
import { formatDateTime } from '@/utils/dateTime';

const props = defineProps<{ packageVersionId: string }>();
const { t, te } = useI18n({ useScope: 'local' });

interface RuleResult { id: string; policyId?: string; rule: string; action: string; reason: string; isHardBlock: boolean }
interface Finding { id: string; severity: string; title: string; description: string; source: string; isHardBlock: boolean; riskScore: number }
interface AuditEvent { id: string; timestamp: string; actor: string; action: string; description: string }
interface PolicyInfo { id: string; name: string }

const loading = ref(false); const error = ref(''); const rules = ref<RuleResult[]>([]); const findings = ref<Finding[]>([]); const events = ref<AuditEvent[]>([]); const policies = ref<PolicyInfo[]>([]);

async function load() {
  loading.value = true; error.value = ''; rules.value = []; findings.value = []; events.value = []; policies.value = [];

  try {
    const data = await graphql<{ policyRuleResults: { nodes: RuleResult[] }; securityFindings: { nodes: Finding[] }; auditEvents: { nodes: AuditEvent[] }; policies: { nodes: PolicyInfo[] } }>(`query PackageDecisionDetails($id:UUID!,$entityId:String!){policyRuleResults(packageVersionId:$id,first:100){nodes{id policyId rule action reason isHardBlock}} securityFindings(packageVersionId:$id,first:100){nodes{id severity title description source isHardBlock riskScore}} auditEvents(entityType:"PackageVersion",entityId:$entityId,first:25){nodes{id timestamp actor action description}} policies(first:100){nodes{id name}}}`, { id: props.packageVersionId, entityId: props.packageVersionId });

    rules.value = data.policyRuleResults.nodes; findings.value = data.securityFindings.nodes; events.value = data.auditEvents.nodes; policies.value = data.policies.nodes;
  }
  catch (e) { error.value = (e as Error).message; }
  finally { loading.value = false; }
}
function ruleName(rule: RuleResult) { return policies.value.find(policy => policy.id === rule.policyId)?.name || rule.rule.replace(/Policy$/, '').replace(/([a-z])([A-Z])/g, '$1 $2'); }
function actionLabel(action: string) {
  const key = `actions.${action}`;

  return te(key) ? t(key) : action.replace(/_/g, ' ');
}
watch(() => props.packageVersionId, load, { immediate: true });
</script>

<style scoped>
.action-chip {
  font-size: 0.6875rem;
  letter-spacing: 0.04em;
}
</style>

<i18n lang="json">
{"en":{"policyReasons":"Policy reasons ({count})","findings":"Security findings ({count})","audit":"Decision history ({count})","noPolicyReasons":"No policy rule results are recorded.","noFindings":"No security findings are recorded.","noAudit":"No decision history is recorded.","actions":{"ALLOW":"allow","WARN":"warn","MANUAL_REVIEW":"manual review","QUARANTINE":"quarantine","BLOCK":"block"}},"sv":{"policyReasons":"Policyorsaker ({count})","findings":"Säkerhetsfynd ({count})","audit":"Beslutshistorik ({count})","noPolicyReasons":"Inga resultat från policyregler har registrerats.","noFindings":"Inga säkerhetsfynd har registrerats.","noAudit":"Ingen beslutshistorik har registrerats.","actions":{"ALLOW":"tillåt","WARN":"varna","MANUAL_REVIEW":"manuell granskning","QUARANTINE":"karantän","BLOCK":"blockera"}}}
</i18n>
