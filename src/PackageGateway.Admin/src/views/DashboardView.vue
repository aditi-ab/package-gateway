<template>
  <div class="page">
    <div class="flex flex-wrap items-end justify-between gap-4">
      <div>
        <p class="eyebrow">
          {{ t('eyebrow') }}
        </p><h1>{{ t('title') }}</h1><p class="page-lead">
          {{ t('lead') }}
        </p>
      </div><Button variant="outline" :disabled="loading" @click="refresh">
        <Spinner v-if="loading" /><RefreshCw v-else />{{ t('refresh') }}
      </Button>
    </div>

    <Alert
      v-if="error"
      variant="destructive"
      class="mt-5"
    >
      <CircleAlert /><AlertDescription>{{ error }}</AlertDescription><AlertAction>
        <Button variant="ghost" size="icon" :aria-label="t('refresh')" @click="error = ''">
          <X />
        </Button>
      </AlertAction>
    </Alert>

    <div class="section-heading mb-3 mt-6">
      {{ t("inventory") }}
    </div>
    <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
      <Card class="click-row h-full p-5" role="link" tabindex="0" @click="$router.push('/repositories')" @keydown.enter="$router.push('/repositories')">
        <div class="flex items-center gap-2 text-muted-foreground">
          <Database />{{ t("repositories") }}
        </div>
        <div class="metric mt-3">
          {{ repositoryCount }}
        </div>
        <div class="mt-2 text-xs">
          {{ t("repositoriesLead") }}
        </div>
      </Card>
      <Card class="click-row h-full p-5" role="link" tabindex="0" @click="$router.push('/packages')" @keydown.enter="$router.push('/packages')">
        <div class="flex items-center gap-2 text-muted-foreground">
          <Package />{{ t("versions") }}
        </div>
        <div class="metric mt-3">
          {{ versionCount }}
        </div>
        <div class="mt-2 text-xs">
          {{ t("versionsLead") }}
        </div>
      </Card>
      <Card class="click-row h-full p-5" role="link" tabindex="0" @click="$router.push('/packages')" @keydown.enter="$router.push('/packages')">
        <div class="flex items-center gap-2 text-muted-foreground">
          <BadgeCheck class="text-success" />{{ t("approved") }}
        </div>
        <div class="metric mt-3">
          {{ approvedCount }}
        </div>
        <div class="mt-2 text-xs">
          {{ t("approvedLead") }}
        </div>
      </Card>
      <Card class="click-row h-full p-5" role="link" tabindex="0" @click="$router.push('/review')" @keydown.enter="$router.push('/review')">
        <div class="flex items-center gap-2 text-muted-foreground">
          <CircleAlert class="text-warning" />{{ t("attention") }}
        </div>
        <div class="metric mt-3">
          {{ attentionCount }}
        </div>
        <div class="mt-2 text-xs">
          {{
            t("attentionLead", {
              review: reviewCount,
              quarantined: quarantinedCount,
              blocked: blockedCount,
            })
          }}
        </div>
      </Card>
    </div>

    <div class="section-heading mb-3 mt-6">
      {{ t("serviceHealth") }}
    </div>
    <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
      <Card class="h-full p-5">
        <div class="text-muted-foreground">
          {{ t("database") }}
        </div>
        <div class="flex items-center gap-3 mt-3">
          <div class="health-value">
            {{ t(status?.database.healthy ? "ready" : "degraded") }}
          </div>
          <StatusChip
            :status="status?.database.healthy ? 'healthy' : 'blocked'"
          />
        </div>
        <div class="text-xs mt-2">
          {{ status?.database.detail || t("databaseDetail") }}
        </div>
      </Card>
      <Card class="h-full p-5">
        <div class="text-muted-foreground">
          {{ t("background") }}
        </div>
        <div class="flex items-center gap-3 mt-3">
          <div class="health-value">
            {{
              t(status?.backgroundScanner.healthy ? "healthy" : "attention")
            }}
          </div>
          <StatusChip
            :status="
              status?.backgroundScanner.healthy ? 'healthy' : 'manual_review'
            "
          />
        </div>
        <div class="text-xs mt-2">
          {{ status?.backgroundScanner.detail || t("backgroundDetail") }}
        </div>
      </Card>
      <Card class="h-full p-5">
        <div class="text-muted-foreground">
          {{ t("vulnerabilities") }}
        </div>
        <div class="flex items-center gap-3 mt-3">
          <div class="health-value">
            {{ t(vulnerabilityHealthy ? "healthy" : "attention") }}
          </div>
          <StatusChip
            :status="vulnerabilityHealthy ? 'healthy' : 'manual_review'"
          />
        </div>
        <div class="text-xs mt-2">
          {{ vulnerabilityDetail }}
        </div>
      </Card>
      <Card class="h-full p-5">
        <div class="text-muted-foreground">
          {{ t("version") }}
        </div>
        <div class="health-value mt-3">
          {{ status?.version || t("unavailable") }}
        </div>
        <div class="text-xs mt-2">
          {{ t("started") }}
          {{ formatDateTime(status?.startedAt, t("unavailable")) }}
        </div>
      </Card>
    </div>

    <Card class="mt-6 py-0">
      <div
        class="flex flex-wrap items-center gap-3 p-5 border-b dashboard-card-header"
      >
        <div>
          <div class="section-heading">
            {{ t("queue") }}
          </div>
          <div class="text-xs text-muted-foreground">
            {{ t("queueLead", { shown: queue.length, total: reviewCount }) }}
          </div>
        </div>
        <Button as-child variant="outline" class="ml-auto">
          <RouterLink to="/review">
            {{ t('viewQueue') }}<ArrowRight />
          </RouterLink>
        </Button>
      </div>
      <Table v-if="queue.length">
        <TableHeader>
          <TableRow>
            <TableHead>{{ t("package") }}</TableHead>
            <TableHead>{{ t("repository") }}</TableHead>
            <TableHead>{{ t("format") }}</TableHead>
            <TableHead>{{ t("risk") }}</TableHead>
            <TableHead>{{ t("signals") }}</TableHead>
            <TableHead>{{ t("scanned") }}</TableHead>
            <TableHead />
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="item in queue" :key="item.id">
            <TableCell>
              <div class="font-bold">
                {{ item.package?.name || t("unknown") }}
              </div>
              <div
                class="flex items-center gap-2 text-xs text-muted-foreground"
              >
                <span class="mono">{{ item.version }}</span>
                <span>·</span>
                <span>{{ item.decisionExplanation }}</span>
              </div>
            </TableCell>
            <TableCell>
              <RouterLink
                v-if="item.package?.repositoryId"
                :to="`/repositories/${item.package.repositoryId}/packages`"
                class="text-primary no-underline"
              >
                {{ repositoryName(item) }}
              </RouterLink>
              <span v-else>{{ t("unknown") }}</span>
            </TableCell>
            <TableCell>
              <Badge variant="secondary">
                {{
                  formatName(item.package?.packageType)
                }}
              </Badge>
            </TableCell>
            <TableCell class="font-bold">
              {{ item.riskScore }}
            </TableCell>
            <TableCell>
              <Badge
                v-if="item.hasHardBlock"
                variant="destructive"
                class="mr-1"
              >
                {{ t("hardGuard") }}
              </Badge>
              <Badge
                v-if="item.hasInstallScripts"
                variant="warning"
              >
                {{ t("installScripts") }}
              </Badge>
              <span
                v-if="!item.hasHardBlock && !item.hasInstallScripts"
                class="text-muted-foreground"
              >
                {{ item.license || item.signatureStatus }}
              </span>
            </TableCell>
            <TableCell>
              {{ formatDateTime(item.lastScannedAt || item.firstSeenAt) }}
            </TableCell>
            <TableCell class="text-right">
              <Button as-child variant="outline" size="sm">
                <RouterLink :to="reviewTarget(item)">
                  {{ t('review') }}
                </RouterLink>
              </Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
      <div v-else-if="!loading" class="empty-state dashboard-empty-state">
        <div>
          <CircleCheck class="mx-auto size-10 text-success" />
          <div class="font-bold mt-3">
            {{ t("empty") }}
          </div>
          <div class="text-xs">
            {{ t("emptyLead") }}
          </div>
        </div>
      </div>
    </Card>
  </div>
</template>

<script setup lang="ts">
import { Alert, AlertAction, AlertDescription, Badge, Button, Card, Spinner, Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@aditify/ui';
import { ArrowRight, BadgeCheck, CircleAlert, CircleCheck, Database, Package, RefreshCw, X } from '@lucide/vue';
import { computed, onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink } from 'vue-router';
import { graphql } from '@/api/graphql';
import StatusChip from '@/components/StatusChip.vue';
import { formatDateTime } from '@/utils/dateTime';

const { t } = useI18n({ useScope: 'local' });

interface HealthStatus {
  healthy: boolean;
  detail?: string;
}
interface Status {
  version: string;
  startedAt: string;
  database: HealthStatus;
  backgroundScanner: HealthStatus;
  vulnerabilityProviders: Array<HealthStatus & { name: string }>;
}
interface Repository {
  id: string;
  name: string;
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
  riskScore: number;
  hasHardBlock: boolean;
  hasInstallScripts: boolean;
  license?: string;
  signatureStatus: string;
  decisionExplanation: string;
  lastScannedAt?: string;
  firstSeenAt: string;
  package?: PackageInfo;
}
interface CountConnection {
  totalCount: number;
}
interface QueueConnection extends CountConnection {
  nodes: Version[];
}

const status = ref<Status>();
const repositories = ref<Repository[]>([]);
const repositoryCount = ref(0);
const versionCount = ref(0);
const approvedCount = ref(0);
const reviewCount = ref(0);
const quarantinedCount = ref(0);
const blockedCount = ref(0);
const queue = ref<Version[]>([]);
const error = ref('');
const loading = ref(true);

const attentionCount = computed(
  () => reviewCount.value + quarantinedCount.value + blockedCount.value,
);
const vulnerabilityHealthy = computed(
  () =>
    !!status.value?.vulnerabilityProviders.length
    && status.value.vulnerabilityProviders.every(provider => provider.healthy),
);
const vulnerabilityDetail = computed(() => {
  const providers = status.value?.vulnerabilityProviders || [];

  if (!providers.length)
    return t('providersUnavailable');

  const unhealthy = providers.filter(provider => !provider.healthy);

  if (unhealthy.length) {
    return unhealthy
      .map(provider => provider.detail || provider.name)
      .join(' · ');
  }

  return t('providersHealthy', { count: providers.length });
});

async function refresh() {
  loading.value = true;
  error.value = '';

  try {
    const data = await graphql<{
      systemStatus: Status;
      repositories: { nodes: Repository[]; totalCount: number };
      allVersions: CountConnection;
      approvedVersions: CountConnection;
      reviewVersions: QueueConnection;
      quarantinedVersions: CountConnection;
      blockedVersions: CountConnection;
    }>(`
      query Dashboard {
        systemStatus {
          version
          startedAt
          database {
            healthy
            detail
          }
          backgroundScanner {
            healthy
            detail
          }
          vulnerabilityProviders {
            name
            healthy
            detail
          }
        }
        repositories(first: 100, sortBy: NAME, direction: ASCENDING) {
          nodes {
            id
            name
          }
          totalCount
        }
        allVersions: packageVersions(first: 1) {
          totalCount
        }
        approvedVersions: packageVersions(status: APPROVED, first: 1) {
          totalCount
        }
        reviewVersions: packageVersions(
          status: MANUAL_REVIEW
          first: 6
          sortBy: RISK_SCORE
          direction: DESCENDING
        ) {
          nodes {
            id
            version
            status
            riskScore
            hasHardBlock
            hasInstallScripts
            license
            signatureStatus
            decisionExplanation
            lastScannedAt
            firstSeenAt
            package {
              name
              packageType
              repositoryId
            }
          }
          totalCount
        }
        quarantinedVersions: packageVersions(status: QUARANTINED, first: 1) {
          totalCount
        }
        blockedVersions: packageVersions(status: BLOCKED, first: 1) {
          totalCount
        }
      }
    `);

    status.value = data.systemStatus;
    repositories.value = data.repositories.nodes;
    repositoryCount.value = data.repositories.totalCount;
    versionCount.value = data.allVersions.totalCount;
    approvedCount.value = data.approvedVersions.totalCount;
    reviewCount.value = data.reviewVersions.totalCount;
    quarantinedCount.value = data.quarantinedVersions.totalCount;
    blockedCount.value = data.blockedVersions.totalCount;
    queue.value = data.reviewVersions.nodes;
  }
  catch (e) {
    error.value = (e as Error).message;
  }
  finally {
    loading.value = false;
  }
}

function repositoryName(item: Version) {
  return (
    repositories.value.find(
      repository => repository.id === item.package?.repositoryId,
    )?.name || t('unknown')
  );
}
function formatName(value?: string) {
  return value === 'NU_GET' ? 'NuGet' : value === 'NPM' ? 'npm' : t('unknown');
}
function reviewTarget(item: Version) {
  return item.package?.repositoryId
    ? `/repositories/${item.package.repositoryId}/review`
    : '/review';
}

onMounted(refresh);
</script>

<style scoped>
.health-value {
  font-size: 1.5rem;
  font-weight: 700;
}
.dashboard-card-header {
  border-color: var(--border) !important;
}
.dashboard-empty-state {
  min-height: 190px;
}
</style>

<i18n lang="json">
{
  "en": {
    "eyebrow": "Security posture",
    "title": "Gateway overview",
    "lead": "Inventory, service health, and package decisions that require attention.",
    "refresh": "Refresh",
    "inventory": "Package posture",
    "repositories": "Repositories",
    "repositoriesLead": "Configured package endpoint containers",
    "versions": "Package versions",
    "versionsLead": "Acquired versions across all repositories",
    "approved": "Approved",
    "approvedLead": "Versions currently approved for delivery",
    "attention": "Needs attention",
    "attentionLead": "{review} review · {quarantined} quarantined · {blocked} blocked",
    "serviceHealth": "Service health",
    "database": "Database readiness",
    "ready": "Ready",
    "degraded": "Degraded",
    "databaseDetail": "Schema is current and reachable.",
    "background": "Background execution",
    "healthy": "Healthy",
    "backgroundDetail": "All scheduled jobs are reporting normally.",
    "vulnerabilities": "Vulnerability intelligence",
    "providersHealthy": "{count} providers reporting normally.",
    "providersUnavailable": "No vulnerability provider is reporting.",
    "version": "Gateway version",
    "unavailable": "Not available",
    "started": "Started",
    "queue": "Manual review queue",
    "queueLead": "Showing the highest-risk {shown} of {total} versions awaiting review.",
    "viewQueue": "View full queue",
    "package": "Package",
    "repository": "Repository",
    "format": "Format",
    "risk": "Risk",
    "signals": "Signals",
    "scanned": "Scanned",
    "hardGuard": "Hard guard",
    "installScripts": "Install scripts",
    "review": "Review",
    "unknown": "Unknown",
    "empty": "No packages need review",
    "emptyLead": "The manual review queue is clear."
  },
  "sv": {
    "eyebrow": "Säkerhetsstatus",
    "title": "Gatewayöversikt",
    "lead": "Paketöversikt, tjänstehälsa och paketbeslut som kräver åtgärd.",
    "refresh": "Uppdatera",
    "inventory": "Paketstatus",
    "repositories": "Lagringsplatser",
    "repositoriesLead": "Konfigurerade behållare för paketslutpunkter",
    "versions": "Paketversioner",
    "versionsLead": "Hämtade versioner i alla lagringsplatser",
    "approved": "Godkända",
    "approvedLead": "Versioner som för närvarande får levereras",
    "attention": "Kräver åtgärd",
    "attentionLead": "{review} granskning · {quarantined} i karantän · {blocked} blockerade",
    "serviceHealth": "Tjänstehälsa",
    "database": "Databasstatus",
    "ready": "Redo",
    "degraded": "Nedsatt",
    "databaseDetail": "Schemat är aktuellt och databasen är tillgänglig.",
    "background": "Bakgrundskörning",
    "healthy": "Fungerar",
    "backgroundDetail": "Alla schemalagda jobb rapporterar normalt.",
    "vulnerabilities": "Sårbarhetsinformation",
    "providersHealthy": "{count} leverantörer rapporterar normalt.",
    "providersUnavailable": "Ingen sårbarhetsleverantör rapporterar.",
    "version": "Gatewayversion",
    "unavailable": "Inte tillgänglig",
    "started": "Startad",
    "queue": "Kö för manuell granskning",
    "queueLead": "Visar de {shown} versionerna med högst risk av totalt {total} som väntar på granskning.",
    "viewQueue": "Visa hela kön",
    "package": "Paket",
    "repository": "Lagringsplats",
    "format": "Format",
    "risk": "Risk",
    "signals": "Signaler",
    "scanned": "Skannad",
    "hardGuard": "Hård spärr",
    "installScripts": "Installationsskript",
    "review": "Granska",
    "unknown": "Okänd",
    "empty": "Inga paket behöver granskas",
    "emptyLead": "Kön för manuell granskning är tom."
  }
}
</i18n>
