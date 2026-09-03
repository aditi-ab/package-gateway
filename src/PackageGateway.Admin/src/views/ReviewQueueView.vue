<template>
  <div :class="{ page: !embedded }">
    <div v-if="!embedded" class="flex flex-wrap items-end justify-between gap-4">
      <div>
        <p class="eyebrow">
          {{ t('eyebrow') }}
        </p><h1>{{ t('title') }}</h1><p class="page-lead">
          {{ t('lead') }}
        </p>
      </div>
      <Button variant="outline" :disabled="loading" @click="load">
        <Spinner v-if="loading" /><RefreshCw v-else />{{ t('refresh') }}
      </Button>
    </div>
    <div v-else class="mb-5 flex items-center">
      <div>
        <div class="section-heading">
          {{ t("title") }}
        </div>
        <div class="text-sm text-muted-foreground">
          {{ t("lead") }}
        </div>
      </div>
      <Button class="ml-auto" variant="outline" :disabled="loading" @click="load">
        <Spinner v-if="loading" /><RefreshCw v-else />{{ t('refresh') }}
      </Button>
    </div>
    <Alert v-if="error" variant="destructive" class="mb-5 mt-5">
      <CircleAlert /><AlertDescription>{{ error }}</AlertDescription>
    </Alert><Card :class="embedded ? 'py-0' : 'mt-6 py-0'">
      <Table>
        <TableHeader><TableRow><TableHead>{{ t('package') }}</TableHead><TableHead>{{ t('version') }}</TableHead><TableHead>{{ t('decision') }}</TableHead><TableHead>{{ t('risk') }}</TableHead><TableHead>{{ t('signals') }}</TableHead><TableHead /></TableRow></TableHeader>
        <TableBody>
          <TableRow v-for="item in items" :key="item.id">
            <TableCell class="font-medium">
              {{ item.package?.name || t("unknown") }}
            </TableCell>
            <TableCell class="mono">
              {{ item.version }}
            </TableCell>
            <TableCell><StatusChip :status="item.status" /></TableCell>
            <TableCell>{{ item.riskScore }}</TableCell>
            <TableCell>
              <Badge
                v-if="item.hasHardBlock"
                variant="destructive"
                class="mr-2"
              >
                {{ t("hardGuard") }}
              </Badge><Badge
                v-if="item.hasInstallScripts"
                variant="warning"
              >
                {{ t("installScripts") }}
              </Badge><span
                v-if="!item.hasHardBlock && !item.hasInstallScripts"
                class="text-muted-foreground"
              >{{ item.license || item.signatureStatus }}</span>
            </TableCell>
            <TableCell class="text-right">
              <Button variant="outline" @click="review(item)">
                {{ t("review") }}
              </Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Card><Dialog v-model:open="dialog">
      <DialogContent v-if="selected" size="2xl" scrollable>
        <DialogHeader>
          <DialogTitle>
            {{
              t("reviewItem", {
                name: selected.package?.name,
                version: selected.version,
              })
            }}
          </DialogTitle>
        </DialogHeader><div data-slot="dialog-body" class="dialog-scroll-body">
          <Alert
            v-if="selected.hasHardBlock"
            variant="destructive"
            class="mb-4"
          >
            <CircleAlert /><AlertDescription>{{ t("hardGuardWarning") }}</AlertDescription>
          </Alert>
          <p>{{ selected.decisionExplanation }}</p>
          <PackageDecisionDetails
            :package-version-id="selected.id"
          /><Field class="mt-5">
            <FieldLabel for="review-reason">
              {{ t('reason') }}
            </FieldLabel><Textarea id="review-reason" v-model="reason" rows="3" />
          </Field>
        </div><DialogFooter class="flex-wrap sm:justify-start">
          <Button variant="outline" @click="rescan">
            <RefreshCw />
            {{
              t("rescan")
            }}
          </Button><Button variant="destructive" @click="decide('block')">
            {{
              t("block")
            }}
          </Button><Button
            variant="outline"
            class="border-warning/50 text-warning"
            @click="decide('quarantine')"
          >
            {{ t("quarantine") }}
          </Button><Button class="sm:ml-auto" variant="outline" @click="dialog = false">
            {{
              t("cancel")
            }}
          </Button><Button
            :disabled="selected.hasHardBlock"
            @click="decide('approve')"
          >
            {{ t("approve") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>

<script setup lang="ts">
import { Alert, AlertDescription, Badge, Button, Card, Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle, Field, FieldLabel, Spinner, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, Textarea } from '@aditify/ui';
import { CircleAlert, RefreshCw } from '@lucide/vue';
import { onMounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { graphql, mutationError } from '@/api/graphql';
import PackageDecisionDetails from '@/components/PackageDecisionDetails.vue';
import StatusChip from '@/components/StatusChip.vue';

const props = withDefaults(
  defineProps<{ repositoryId?: string; embedded?: boolean }>(),
  { embedded: false },
);
const { t } = useI18n({ useScope: 'local' });

interface Item {
  id: string;
  version: string;
  status: string;
  riskScore: number;
  hasHardBlock: boolean;
  license?: string;
  signatureStatus: string;
  hasInstallScripts: boolean;
  decisionExplanation: string;
  package?: { name: string };
}

const items = ref<Item[]>([]);
const selected = ref<Item>();
const dialog = ref(false);
const reason = ref('');
const error = ref('');
const loading = ref(false);

async function load() {
  loading.value = true;
  error.value = '';

  try {
    items.value = (
      await graphql<{ packageVersions: { nodes: Item[] } }>(
        `
          query ReviewQueue($repositoryId: UUID) {
            packageVersions(
              repositoryId: $repositoryId
              status: MANUAL_REVIEW
              first: 100
              sortBy: RISK_SCORE
              direction: DESCENDING
            ) {
              nodes {
                id
                version
                status
                riskScore
                hasHardBlock
                license
                signatureStatus
                hasInstallScripts
                decisionExplanation
                package {
                  name
                }
              }
            }
          }
        `,
        { repositoryId: props.repositoryId || null },
      )
    ).packageVersions.nodes;
  }
  catch (e) {
    error.value = (e as Error).message;
  }
  finally {
    loading.value = false;
  }
}
function review(x: Item) {
  selected.value = x;
  reason.value = '';
  dialog.value = true;
}
async function decide(action: 'approve' | 'block' | 'quarantine') {
  if (!selected.value)
    return;

  const names = {
    approve: 'approvePackageVersion',
    block: 'blockPackageVersion',
    quarantine: 'quarantinePackageVersion',
  } as const;
  const operation = names[action];
  const data = await graphql<
    Record<string, { errors: Array<{ code: string; message: string }> }>
  >(
    `mutation($id:UUID!,$reason:String!){ ${operation}(id:$id, reason:$reason) { errors { code message } } }`,
    {
      id: selected.value.id,
      reason: reason.value || t('defaultReason', { action: t(action) }),
    },
  );

  mutationError(data[operation]!.errors);
  dialog.value = false;
  await load();
}
async function rescan() {
  if (!selected.value)
    return;

  const data = await graphql<{
    rescanPackageVersion: { errors: Array<{ code: string; message: string }> };
  }>(
    `
      mutation ($id: UUID!) {
        rescanPackageVersion(id: $id) {
          errors {
            code
            message
          }
        }
      }
    `,
    { id: selected.value.id },
  );

  mutationError(data.rescanPackageVersion.errors);
  dialog.value = false;
  await load();
}
onMounted(load);
watch(
  () => props.repositoryId,
  () => void load(),
);
</script>

<i18n lang="json">
{
  "en": {
    "eyebrow": "Decision workflow",
    "title": "Review queue",
    "lead": "Inspect security context before approving, quarantining, or blocking package bytes.",
    "refresh": "Refresh",
    "package": "Package",
    "version": "Version",
    "decision": "Decision",
    "risk": "Risk",
    "signals": "Signals",
    "unknown": "Unknown",
    "hardGuard": "Hard guard",
    "installScripts": "Install scripts",
    "review": "Review",
    "reviewItem": "Review {name} {version}",
    "hardGuardWarning": "A non-waivable hard guard is active. Approval will be rejected.",
    "reason": "Decision reason",
    "rescan": "Rescan",
    "block": "Block",
    "quarantine": "Quarantine",
    "cancel": "Cancel",
    "approve": "Approve",
    "defaultReason": "{action} through management console."
  },
  "sv": {
    "eyebrow": "Beslutsflöde",
    "title": "Granskningskö",
    "lead": "Granska säkerhetsinformationen innan paketdata godkänns, sätts i karantän eller blockeras.",
    "refresh": "Uppdatera",
    "package": "Paket",
    "version": "Version",
    "decision": "Beslut",
    "risk": "Risk",
    "signals": "Signaler",
    "unknown": "Okänt",
    "hardGuard": "Hård spärr",
    "installScripts": "Installationsskript",
    "review": "Granska",
    "reviewItem": "Granska {name} {version}",
    "hardGuardWarning": "En hård spärr som inte kan undantas är aktiv. Godkännandet kommer att avvisas.",
    "reason": "Orsak till beslut",
    "rescan": "Skanna igen",
    "block": "Blockera",
    "quarantine": "Sätt i karantän",
    "cancel": "Avbryt",
    "approve": "Godkänn",
    "defaultReason": "{action} via administrationskonsolen."
  }
}
</i18n>
