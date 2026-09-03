<template>
  <div :class="{ page: !embedded }">
    <div v-if="!embedded" class="flex flex-wrap items-end justify-between gap-4">
      <div>
        <p class="eyebrow">
          {{ t('eyebrow') }}
        </p><h1>{{ t('title') }}</h1><p class="page-lead">
          {{ t('lead') }}
        </p>
      </div><Button @click="addDialog = true">
        <Download />{{ t('addPackage') }}
      </Button>
    </div>
    <div v-else class="mb-5">
      <div class="section-heading">
        {{ t("title") }}
      </div>
      <div class="text-sm text-muted-foreground">
        {{ t("lead") }}
      </div>
      <Button class="mt-3" @click="addDialog = true">
        <Download />{{ t('addPackage') }}
      </Button>
    </div>
    <Alert
      v-if="error"
      variant="destructive"
      class="mt-5"
    >
      <CircleAlert /><AlertDescription>{{ error }}</AlertDescription>
    </Alert>
    <Alert
      v-if="success"
      class="mt-5 border-success/40 text-success"
    >
      <CircleCheck /><AlertDescription>{{ success }}</AlertDescription>
    </Alert>
    <Card class="mb-5 mt-5 p-5">
      <form class="grid items-end gap-4 sm:grid-cols-2 lg:grid-cols-5" @submit.prevent="load()">
        <Field>
          <FieldLabel for="package-filter">
            {{ t('filters.package') }}
          </FieldLabel><Input id="package-filter" v-model="search" />
        </Field><Field v-if="!embedded">
          <FieldLabel for="repository-filter">
            {{ t('filters.repository') }}
          </FieldLabel><Select v-model="repositoryFilter">
            <SelectTrigger id="repository-filter">
              <SelectValue />
            </SelectTrigger><SelectContent>
              <SelectItem v-for="option in repositoryOptions" :key="option.id" :value="option.id">
                {{ option.name }}
              </SelectItem>
            </SelectContent>
          </Select>
        </Field><Field>
          <FieldLabel for="format-filter">
            {{ t('filters.format') }}
          </FieldLabel><Select v-model="packageType">
            <SelectTrigger id="format-filter">
              <SelectValue />
            </SelectTrigger><SelectContent>
              <SelectItem v-for="option in packageTypeOptions" :key="option.value" :value="option.value">
                {{ option.title }}
              </SelectItem>
            </SelectContent>
          </Select>
        </Field><Field>
          <FieldLabel for="status-filter">
            {{ t('filters.status') }}
          </FieldLabel><Select v-model="status">
            <SelectTrigger id="status-filter">
              <SelectValue />
            </SelectTrigger><SelectContent>
              <SelectItem v-for="option in statusOptions" :key="option.value" :value="option.value">
                {{ option.title }}
              </SelectItem>
            </SelectContent>
          </Select>
        </Field><div class="flex gap-2">
          <Tooltip>
            <TooltipTrigger as-child>
              <Button type="submit" size="icon" :aria-label="t('search')" :disabled="loading">
                <Spinner v-if="loading" /><Search v-else />
              </Button>
            </TooltipTrigger><TooltipContent>{{ t('search') }}</TooltipContent>
          </Tooltip><Tooltip>
            <TooltipTrigger as-child>
              <Button type="button" variant="outline" size="icon" :aria-label="t('clear')" @click="clearFilters">
                <ListFilterPlus />
              </Button>
            </TooltipTrigger><TooltipContent>{{ t('clear') }}</TooltipContent>
          </Tooltip>
        </div>
      </form>
    </Card>
    <Card class="py-0">
      <div class="flex items-center p-5 border-b">
        <strong>{{
          t("count", { shown: versions.length, total: totalCount })
        }}</strong><Button class="ml-auto" variant="ghost" :disabled="loading" @click="load()">
          <Spinner v-if="loading" /><RefreshCw v-else />{{ t('refresh') }}
        </Button>
      </div>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{{ t("package") }}</TableHead>
            <TableHead>{{ t("version") }}</TableHead>
            <TableHead>{{ t("format") }}</TableHead>
            <TableHead v-if="!embedded">
              {{ t("repository") }}
            </TableHead>
            <TableHead>{{ t("decision") }}</TableHead>
            <TableHead>
              <span class="inline-flex items-center gap-1">{{ t("risk")
              }}<Tooltip><TooltipTrigger as-child><Button variant="ghost" size="icon-sm" :aria-label="t('riskHelp')"><Info /></Button></TooltipTrigger><TooltipContent>{{ t('riskHelp') }}</TooltipContent></Tooltip></span>
            </TableHead>
            <TableHead>{{ t("scanned") }}</TableHead>
            <TableHead />
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="item in versions" :key="item.id">
            <TableCell>
              <div class="font-bold">
                {{ item.package?.name || t("unknown") }}
              </div>
              <div class="mono text-xs text-muted-foreground">
                {{ item.sha256?.slice(0, 16) || t("notStored") }}
              </div>
            </TableCell>
            <TableCell class="mono">
              {{ item.version }}
            </TableCell>
            <TableCell>
              <Badge variant="secondary">
                {{
                  formatName(item.package?.packageType)
                }}
              </Badge>
            </TableCell>
            <TableCell v-if="!embedded">
              {{ repositoryName(item) }}
            </TableCell>
            <TableCell><StatusChip :status="item.status" /></TableCell>
            <TableCell>
              {{ item.riskScore
              }}<Badge
                v-if="item.hasHardBlock"
                variant="destructive"
                class="ml-2"
              >
                {{ t("hardGuard") }}
              </Badge>
            </TableCell>
            <TableCell>
              {{ formatDateTime(item.lastScannedAt || item.firstSeenAt) }}
            </TableCell>
            <TableCell class="text-right">
              <Button variant="outline" size="sm" @click="open(item)">
                {{
                  t("manage")
                }}
              </Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
      <div v-if="!versions.length && !loading" class="empty-state">
        <div>
          <PackageSearch class="mx-auto size-10" />
          <div class="font-bold mt-3">
            {{ t("empty") }}
          </div>
          <div class="text-xs">
            {{ t("emptyLead") }}
          </div>
        </div>
      </div>
      <div v-if="nextCursor" class="p-5 text-center border-t">
        <Button variant="outline" :disabled="loading" @click="load(false)">
          <Spinner v-if="loading" />
          {{
            t("loadMore")
          }}
        </Button>
      </div>
    </Card>
    <Dialog v-model:open="dialog">
      <DialogContent v-if="selected" size="2xl" scrollable>
        <DialogHeader>
          <DialogTitle>
            {{
              t("dialog.title", {
                name: selected.package?.name,
                version: selected.version,
              })
            }}
          </DialogTitle>
          <div class="flex flex-wrap items-center gap-2">
            <Badge variant="secondary">
              {{
                formatName(selected.package?.packageType)
              }}
            </Badge><StatusChip :status="selected.status" /><Badge variant="outline">
              {{
                t("dialog.riskScore", { score: selected.riskScore })
              }}
            </Badge><Badge v-if="selected.hasHardBlock" variant="destructive">
              {{
                t("hardGuard")
              }}
            </Badge>
          </div>
        </DialogHeader><div data-slot="dialog-body" class="dialog-scroll-body">
          <p>{{ selected.decisionExplanation }}</p>
          <PackageDecisionDetails :package-version-id="selected.id" /><Alert
            v-if="selected.status === 'BLOCKED'"
            class="mt-4"
          >
            <Info /><AlertDescription>{{ t("dialog.blockedHelp") }}</AlertDescription>
          </Alert><Alert
            v-if="selected.hasHardBlock"
            variant="destructive"
            class="mt-4"
          >
            <CircleAlert /><AlertDescription>{{ t("dialog.hardGuardHelp") }}</AlertDescription>
          </Alert><Field class="mt-5">
            <FieldLabel for="package-reason">
              {{ t('dialog.reason') }}
            </FieldLabel><Textarea id="package-reason" v-model="reason" rows="3" />
          </Field>
        </div><DialogFooter class="flex-wrap sm:justify-start">
          <Button
            variant="outline"
            :disabled="actionBusy"
            @click="rescan"
          >
            <RefreshCw />{{ t("actions.rescan") }}
          </Button><Button
            variant="ghost" class="text-destructive hover:text-destructive"
            :disabled="actionBusy"
            @click="confirmRemove"
          >
            <Trash2 />{{ t("actions.remove") }}
          </Button><Button
            v-if="canRequireReview(selected)"
            variant="outline" class="border-warning/50 text-warning"
            :disabled="actionBusy"
            @click="decide('review')"
          >
            {{ t("actions.review") }}
          </Button><Button
            v-if="canQuarantine(selected)"
            variant="outline" class="border-warning/50 text-warning"
            :disabled="actionBusy"
            @click="decide('quarantine')"
          >
            {{ t("actions.quarantine") }}
          </Button><Button
            v-if="canBlock(selected)"
            variant="destructive"
            :disabled="actionBusy"
            @click="decide('block')"
          >
            {{ t("actions.block") }}
          </Button><Button
            class="sm:ml-auto" variant="outline"
            :disabled="actionBusy"
            @click="dialog = false"
          >
            {{ t("actions.cancel") }}
          </Button><Button
            v-if="canApprove(selected)"
            :disabled="actionBusy"
            @click="decide('approve')"
          >
            {{ t("actions.approve") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    <ConfirmDialog
      v-if="selected"
      v-model="removeDialog"
      :title="t('dialog.removeTitle')"
      :message="
        t('dialog.removeConfirm', {
          name: selected.package?.name,
          version: selected.version,
        })
      "
      :confirm-text="t('actions.remove')"
      :cancel-text="t('actions.cancel')"
      :loading="actionBusy"
      @confirm="remove"
    />
    <AddPackageDialog
      v-model="addDialog"
      :repositories="repositories"
      :fixed-repository-id="repositoryId"
      @acquired="handleAcquired"
    />
  </div>
</template>

<script setup lang="ts">
import { Alert, AlertDescription, Badge, Button, Card, Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle, Field, FieldLabel, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Spinner, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, Textarea, Tooltip, TooltipContent, TooltipTrigger } from '@aditify/ui';
import { CircleAlert, CircleCheck, Download, Info, ListFilterPlus, PackageSearch, RefreshCw, Search, Trash2 } from '@lucide/vue';
import { computed, onMounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { graphql, mutationError } from '@/api/graphql';
import { removePackageVersionMutation } from '@/api/packageVersionMutations';
import AddPackageDialog from '@/components/AddPackageDialog.vue';
import ConfirmDialog from '@/components/ConfirmDialog.vue';
import PackageDecisionDetails from '@/components/PackageDecisionDetails.vue';
import StatusChip from '@/components/StatusChip.vue';
import { formatDateTime } from '@/utils/dateTime';

const props = withDefaults(
  defineProps<{ repositoryId?: string; embedded?: boolean }>(),
  { embedded: false },
);
const { t } = useI18n({ useScope: 'local' });

interface Repo {
  id: string;
  name: string;
  packageTypes: string[];
}
interface PackageInfo {
  name: string;
  packageType: string;
  repositoryId: string;
}
interface Version {
  id: string;
  version: string;
  status: string;
  sha256?: string;
  size?: number;
  riskScore: number;
  hasHardBlock: boolean;
  decisionExplanation: string;
  firstSeenAt: string;
  lastScannedAt?: string;
  package?: PackageInfo;
}
interface Connection {
  nodes: Version[];
  totalCount: number;
  pageInfo: { hasNextPage: boolean; endCursor?: string };
}

const repositories = ref<Repo[]>([]);
const versions = ref<Version[]>([]);
const totalCount = ref(0);
const nextCursor = ref<string>();
const loading = ref(false);
const error = ref('');
const success = ref('');
const actionBusy = ref(false);
const search = ref('');
const repositoryFilter = ref('__all');
const packageType = ref('__all');
const status = ref('__all');
const selected = ref<Version>();
const dialog = ref(false);
const removeDialog = ref(false);
const addDialog = ref(false);
const reason = ref('');
const effectiveRepositoryId = computed(
  () => props.repositoryId || (repositoryFilter.value === '__all' ? null : repositoryFilter.value),
);
const repositoryOptions = computed(() => [
  { id: '__all', name: t('filters.allRepositories') },
  ...repositories.value,
]);
const packageTypeOptions = computed(() => [
  { value: '__all', title: t('filters.allFormats') },
  { value: 'NU_GET', title: 'NuGet' },
  { value: 'NPM', title: 'npm' },
]);
const statusOptions = computed(() => [
  { value: '__all', title: t('filters.allStatuses') },
  ...[
    'APPROVED',
    'MANUAL_REVIEW',
    'QUARANTINED',
    'BLOCKED',
    'PENDING',
    'SCANNING',
  ].map(value => ({ value, title: t(`status.${value}`) })),
]);

async function load(reset = true) {
  loading.value = true;
  error.value = '';

  try {
    if (reset) {
      versions.value = [];
      nextCursor.value = undefined;
    }

    const variables = {
      repositoryId: effectiveRepositoryId.value || null,
      packageType: packageType.value === '__all' ? null : packageType.value,
      status: status.value === '__all' ? null : status.value,
      search: search.value.trim() || null,
      after: reset ? null : nextCursor.value || null,
    };
    const data = await graphql<{ packageVersions: Connection }>(
      `
        query Packages(
          $repositoryId: UUID
          $packageType: PackageType
          $status: PackageVersionStatus
          $search: String
          $after: String
        ) {
          packageVersions(
            repositoryId: $repositoryId
            packageType: $packageType
            status: $status
            packageName: $search
            first: 50
            after: $after
            sortBy: FIRST_SEEN_AT
            direction: DESCENDING
          ) {
            nodes {
              id
              version
              status
              sha256
              size
              riskScore
              hasHardBlock
              decisionExplanation
              firstSeenAt
              lastScannedAt
              package {
                name
                packageType
                repositoryId
              }
            }
            totalCount
            pageInfo {
              hasNextPage
              endCursor
            }
          }
        }
      `,
      variables,
    );

    versions.value = reset
      ? data.packageVersions.nodes
      : [...versions.value, ...data.packageVersions.nodes];
    totalCount.value = data.packageVersions.totalCount;
    nextCursor.value = data.packageVersions.pageInfo.hasNextPage
      ? data.packageVersions.pageInfo.endCursor
      : undefined;
  }
  catch (e) {
    error.value = (e as Error).message;
  }
  finally {
    loading.value = false;
  }
}
async function loadRepositories() {
  try {
    repositories.value = (
      await graphql<{ repositories: { nodes: Repo[] } }>(`
        query {
          repositories(first: 100, sortBy: NAME, direction: ASCENDING) {
            nodes {
              id
              name
              packageTypes
            }
          }
        }
      `)
    ).repositories.nodes;
  }
  catch (e) {
    error.value = (e as Error).message;
  }
}
function repositoryName(item: Version) {
  return (
    repositories.value.find(x => x.id === item.package?.repositoryId)?.name
    || t('unknown')
  );
}
async function handleAcquired(message: string) {
  error.value = '';
  success.value = message;
  await load();
}
function formatName(value?: string) {
  return value === 'NU_GET' ? 'NuGet' : value === 'NPM' ? 'npm' : t('unknown');
}
function open(item: Version) {
  selected.value = item;
  reason.value = '';
  dialog.value = true;
}
function canApprove(item?: Version) {
  return (
    !!item
    && !item.hasHardBlock
    && ['MANUAL_REVIEW', 'QUARANTINED'].includes(item.status)
  );
}
function canRequireReview(item?: Version) {
  return item?.status === 'APPROVED';
}
function canQuarantine(item?: Version) {
  return !!item && ['APPROVED', 'MANUAL_REVIEW'].includes(item.status);
}
function canBlock(item?: Version) {
  return (
    !!item
    && ['APPROVED', 'MANUAL_REVIEW', 'QUARANTINED', 'PENDING'].includes(
      item.status,
    )
  );
}
async function decide(action: 'approve' | 'review' | 'quarantine' | 'block') {
  if (!selected.value)
    return;

  actionBusy.value = true;
  error.value = '';

  try {
    const operations = {
      approve: 'approvePackageVersion',
      review: 'requirePackageVersionReview',
      quarantine: 'quarantinePackageVersion',
      block: 'blockPackageVersion',
    } as const;
    const operation = operations[action];
    const data = await graphql<
      Record<string, { errors: Array<{ code: string; message: string }> }>
    >(
      `mutation($id:UUID!,$reason:String!){${operation}(id:$id,reason:$reason){errors{code message}}}`,
      {
        id: selected.value.id,
        reason: reason.value.trim() || t(`defaultReason.${action}`),
      },
    );

    mutationError(data[operation]!.errors);
    dialog.value = false;
    await load();
  }
  catch (e) {
    error.value = (e as Error).message;
  }
  finally {
    actionBusy.value = false;
  }
}
async function rescan() {
  if (!selected.value)
    return;

  actionBusy.value = true;
  error.value = '';

  try {
    const data = await graphql<{
      rescanPackageVersion: {
        errors: Array<{ code: string; message: string }>;
      };
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
  catch (e) {
    error.value = (e as Error).message;
  }
  finally {
    actionBusy.value = false;
  }
}
function confirmRemove() {
  removeDialog.value = true;
}
async function remove() {
  if (!selected.value)
    return;

  actionBusy.value = true;
  error.value = '';

  try {
    const data = await graphql<{
      removePackageVersion: {
        errors: Array<{ code: string; message: string }>;
      };
    }>(
      removePackageVersionMutation,
      {
        id: selected.value.id,
        reason: reason.value.trim() || t('defaultReason.remove'),
      },
    );

    mutationError(data.removePackageVersion.errors);
    removeDialog.value = false;
    dialog.value = false;
    await load();
  }
  catch (e) {
    error.value = (e as Error).message;
  }
  finally {
    actionBusy.value = false;
  }
}
function clearFilters() {
  search.value = '';
  repositoryFilter.value = '__all';
  packageType.value = '__all';
  status.value = '__all';
  void load();
}
onMounted(async () => {
  await loadRepositories();

  await load();
});
watch(
  () => props.repositoryId,
  () => void load(),
);
</script>

<style scoped>
</style>

<i18n lang="json">
{
  "en": {
    "eyebrow": "Package inventory",
    "title": "Packages",
    "lead": "Search scanned NuGet and npm package versions, inspect their decisions, and apply manual security actions.",
    "filters": {
      "package": "Package name",
      "repository": "Repository",
      "format": "Format",
      "status": "Status",
      "allRepositories": "All repositories",
      "allFormats": "All formats",
      "allStatuses": "All statuses"
    },
    "status": {
      "APPROVED": "Approved",
      "MANUAL_REVIEW": "Manual review",
      "QUARANTINED": "Quarantined",
      "BLOCKED": "Blocked",
      "PENDING": "Pending",
      "SCANNING": "Scanning"
    },
    "search": "Search",
    "addPackage": "Add package",
    "clear": "Clear filters",
    "refresh": "Refresh",
    "count": "Showing {shown} of {total} versions",
    "package": "Package",
    "version": "Version",
    "format": "Format",
    "repository": "Repository",
    "decision": "Decision",
    "risk": "Risk",
    "riskHelp": "Aggregate score from archive, integrity, malware, script, and vulnerability findings. Policy rules can impose a stricter decision.",
    "scanned": "Scanned",
    "unknown": "Unknown",
    "notStored": "Not stored",
    "hardGuard": "Hard guard",
    "manage": "Manage",
    "empty": "No package versions found",
    "emptyLead": "Adjust the filters or acquire a package through a gateway endpoint.",
    "loadMore": "Load more",
    "dialog": {
      "title": "Manage {name} {version}",
      "riskScore": "Risk {score}",
      "policyReasons": "Policy reasons ({count})",
      "findings": "Security findings ({count})",
      "audit": "Decision history ({count})",
      "noPolicyReasons": "No policy rule results are recorded.",
      "noFindings": "No security findings are recorded.",
      "noAudit": "No decision history is recorded.",
      "reason": "Reason for this decision",
      "blockedHelp": "Blocked versions must be rescanned or approved through an explicit policy waiver.",
      "hardGuardHelp": "A non-waivable hard guard prevents manual approval.",
      "removeTitle": "Remove package version",
      "removeConfirm": "Remove {name} {version} from the gateway? Its artifact and evaluation state will be deleted. The next request will acquire and evaluate it again."
    },
    "actions": {
      "rescan": "Rescan",
      "remove": "Remove",
      "review": "Send to review",
      "quarantine": "Quarantine",
      "block": "Block",
      "cancel": "Cancel",
      "approve": "Approve"
    },
    "defaultReason": {
      "approve": "Approved through the package inventory.",
      "review": "Manual security review required through the package inventory.",
      "quarantine": "Quarantined through the package inventory.",
      "block": "Blocked through the package inventory.",
      "remove": "Removed through the package inventory for fresh acquisition and evaluation."
    }
  },
  "sv": {
    "eyebrow": "Paketöversikt",
    "title": "Paket",
    "lead": "Sök bland skannade NuGet- och npm-paketversioner, granska beslut och utför manuella säkerhetsåtgärder.",
    "filters": {
      "package": "Paketnamn",
      "repository": "Lagringsplats",
      "format": "Format",
      "status": "Status",
      "allRepositories": "Alla lagringsplatser",
      "allFormats": "Alla format",
      "allStatuses": "Alla statusar"
    },
    "status": {
      "APPROVED": "Godkänd",
      "MANUAL_REVIEW": "Manuell granskning",
      "QUARANTINED": "I karantän",
      "BLOCKED": "Blockerad",
      "PENDING": "Väntande",
      "SCANNING": "Skannas"
    },
    "search": "Sök",
    "addPackage": "Lägg till paket",
    "clear": "Rensa filter",
    "refresh": "Uppdatera",
    "count": "Visar {shown} av {total} versioner",
    "package": "Paket",
    "version": "Version",
    "format": "Format",
    "repository": "Lagringsplats",
    "decision": "Beslut",
    "risk": "Risk",
    "riskHelp": "Sammanlagd poäng från arkiv-, integritets-, skadeprograms-, skript- och sårbarhetsfynd. Policyregler kan ge ett striktare beslut.",
    "scanned": "Skannad",
    "unknown": "Okänd",
    "notStored": "Inte lagrad",
    "hardGuard": "Hård spärr",
    "manage": "Hantera",
    "empty": "Inga paketversioner hittades",
    "emptyLead": "Justera filtren eller hämta ett paket via en gateway-slutpunkt.",
    "loadMore": "Visa fler",
    "dialog": {
      "title": "Hantera {name} {version}",
      "riskScore": "Risk {score}",
      "policyReasons": "Policyorsaker ({count})",
      "findings": "Säkerhetsfynd ({count})",
      "audit": "Beslutshistorik ({count})",
      "noPolicyReasons": "Inga resultat från policyregler har registrerats.",
      "noFindings": "Inga säkerhetsfynd har registrerats.",
      "noAudit": "Ingen beslutshistorik har registrerats.",
      "reason": "Orsak till beslutet",
      "blockedHelp": "Blockerade versioner måste skannas om eller godkännas genom ett uttryckligt policyundantag.",
      "hardGuardHelp": "En hård spärr som inte kan undantas förhindrar manuellt godkännande.",
      "removeTitle": "Ta bort paketversion",
      "removeConfirm": "Ta bort {name} {version} från gatewayen? Artefakten och utvärderingstillståndet raderas. Nästa begäran hämtar och utvärderar paketet igen."
    },
    "actions": {
      "rescan": "Skanna igen",
      "remove": "Ta bort",
      "review": "Skicka till granskning",
      "quarantine": "Sätt i karantän",
      "block": "Blockera",
      "cancel": "Avbryt",
      "approve": "Godkänn"
    },
    "defaultReason": {
      "approve": "Godkänd via paketöversikten.",
      "review": "Manuell säkerhetsgranskning krävs via paketöversikten.",
      "quarantine": "Satt i karantän via paketöversikten.",
      "block": "Blockerad via paketöversikten.",
      "remove": "Borttagen via paketöversikten för ny hämtning och utvärdering."
    }
  }
}
</i18n>
