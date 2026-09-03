<template>
  <Dialog v-model:open="model">
    <DialogContent size="2xl" scrollable>
      <DialogHeader>
        <DialogTitle>
          {{ t("title") }}
        </DialogTitle>
      </DialogHeader><div data-slot="dialog-body" class="dialog-scroll-body dialog-body">
        <Alert class="mb-5">
          <Info /><AlertDescription>{{ t('about') }}</AlertDescription>
        </Alert>
        <Alert v-if="error" variant="destructive" class="mb-5">
          <CircleAlert /><AlertDescription>{{ error }}</AlertDescription>
        </Alert>
        <Alert v-if="!repositories.length">
          <Info /><AlertDescription>{{ t('noRepositories') }}</AlertDescription><AlertAction>
            <Button as-child variant="outline">
              <RouterLink to="/repositories">
                {{ t('createRepository') }}
              </RouterLink>
            </Button>
          </AlertAction>
        </Alert>
        <form v-else class="grid items-end gap-4 md:grid-cols-12" @submit.prevent="searchPackages">
          <Field class="md:col-span-4">
            <FieldLabel for="package-repository">
              {{ t('repository') }}
            </FieldLabel><Select v-model="repositoryId" :disabled="!!fixedRepositoryId">
              <SelectTrigger id="package-repository">
                <SelectValue />
              </SelectTrigger><SelectContent>
                <SelectItem v-for="repository in repositories" :key="repository.id" :value="repository.id">
                  {{ repository.name }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field><Field class="md:col-span-2">
            <FieldLabel for="package-format">
              {{ t('format') }}
            </FieldLabel><Select v-model="packageType">
              <SelectTrigger id="package-format">
                <SelectValue />
              </SelectTrigger><SelectContent>
                <SelectItem v-for="option in formatOptions" :key="option.value" :value="option.value">
                  {{ option.title }}
                </SelectItem>
              </SelectContent>
            </Select>
          </Field><Field class="md:col-span-5">
            <FieldLabel for="package-query">
              {{ t('query') }}
            </FieldLabel><Input id="package-query" v-model="query" />
          </Field><div class="md:col-span-1">
            <Tooltip>
              <TooltipTrigger as-child>
                <Button
                  type="submit"
                  size="icon"
                  :aria-label="t('search')"
                  :disabled="query.trim().length < 2 || !packageType"
                >
                  <Spinner v-if="searching" /><Search v-else />
                </Button>
              </TooltipTrigger><TooltipContent>{{ t('search') }}</TooltipContent>
            </Tooltip>
          </div>
        </form>
        <Alert v-if="selectedRepository && !formatOptions.length" class="mt-5">
          <Info /><AlertDescription>{{ t('noUpstreams') }}</AlertDescription>
        </Alert>
        <Card v-if="searched" class="results-card mt-5 py-0">
          <div class="results-scroll">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead class="expand-column" />
                  <TableHead>{{ t("package") }}</TableHead>
                  <TableHead>{{ t("version") }}</TableHead>
                  <TableHead>{{ t("upstream") }}</TableHead>
                  <TableHead />
                </TableRow>
              </TableHeader><TableBody>
                <template v-for="item in results" :key="resultKey(item)">
                  <TableRow>
                    <TableCell class="expand-column">
                      <Tooltip>
                        <TooltipTrigger as-child>
                          <Button
                            variant="ghost"
                            size="icon"
                            :aria-label="expandedKeys.has(resultKey(item)) ? t('collapseDetails', { name: item.name }) : t('expandDetails', { name: item.name })"
                            :aria-expanded="expandedKeys.has(resultKey(item))"
                            @click="toggleDetails(item)"
                          >
                            <ChevronDown class="transition-transform" :class="[{ 'rotate-180': expandedKeys.has(resultKey(item)) }]" />
                          </Button>
                        </TooltipTrigger><TooltipContent>{{ expandedKeys.has(resultKey(item)) ? t('collapseDetails', { name: item.name }) : t('expandDetails', { name: item.name }) }}</TooltipContent>
                      </Tooltip>
                    </TableCell>
                    <TableCell class="font-bold">
                      {{ item.name }}
                    </TableCell>
                    <TableCell class="mono">
                      {{ item.version }}
                    </TableCell>
                    <TableCell>{{ item.upstreamName }}</TableCell>
                    <TableCell class="version-action text-right">
                      <DropdownMenu :modal="false">
                        <Tooltip>
                          <TooltipTrigger as-child>
                            <DropdownMenuTrigger as-child>
                              <Button
                                size="icon"
                                :aria-label="t('chooseVersion', { name: item.name })"
                                :disabled="!!addingKey"
                                @click="loadVersions(item)"
                              >
                                <Spinner v-if="addingKey === resultKey(item)" /><Download v-else />
                              </Button>
                            </DropdownMenuTrigger>
                          </TooltipTrigger><TooltipContent>{{ t('chooseVersion', { name: item.name }) }}</TooltipContent>
                        </Tooltip><DropdownMenuContent class="version-menu" align="end">
                          <DropdownMenuItem
                            v-if="loadingVersionKey === resultKey(item)"
                            disabled
                          >
                            <Spinner />{{ t('loadingVersions') }}
                          </DropdownMenuItem>
                          <template v-else>
                            <DropdownMenuItem
                              v-for="version in versionsByKey[resultKey(item)] || [item.version]"
                              :key="version"
                              @select="add(item, version)"
                            >
                              <Download />{{ version }}
                            </DropdownMenuItem>
                          </template>
                        </DropdownMenuContent>
                      </DropdownMenu>
                    </TableCell>
                  </TableRow>
                  <TableRow v-if="expandedKeys.has(resultKey(item))">
                    <TableCell colspan="5">
                      <div class="result-details">
                        <div class="detail-label">
                          {{ t("description") }}
                        </div>
                        <p class="result-description">
                          {{ item.description || t("noDescription") }}
                        </p>
                      </div>
                    </TableCell>
                  </TableRow>
                </template>
              </TableBody>
            </Table>
          </div>
          <div v-if="!results.length && !searching" class="empty-state p-6">
            {{ t("empty") }}
          </div>
        </Card>
      </div><DialogFooter>
        <Button variant="outline" :disabled="!!addingKey" @click="model = false">
          {{ t("close") }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

<script setup lang="ts">
import { Alert, AlertAction, AlertDescription, Button, Card, Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle, DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger, Field, FieldLabel, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Spinner, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, Tooltip, TooltipContent, TooltipTrigger } from '@aditify/ui';
import { ChevronDown, CircleAlert, Download, Info, Search } from '@lucide/vue';
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink } from 'vue-router';
import { graphql, mutationError } from '@/api/graphql';
import { addPackageVersionMutation, upstreamPackageSearchQuery, upstreamPackageVersionsQuery } from '@/api/upstreamPackages';

interface RepositoryOption {
  id: string;
  name: string;
  packageTypes: string[];
}
interface SearchResult {
  upstreamId: string;
  upstreamName: string;
  packageType: string;
  name: string;
  version: string;
  description?: string;
}

const props = defineProps<{
  repositories: RepositoryOption[];
  fixedRepositoryId?: string;
}>();
const emit = defineEmits<{ acquired: [message: string] }>();
const model = defineModel<boolean>({ required: true });
const { t } = useI18n({ useScope: 'local' });
const repositoryId = ref<string>();
const packageType = ref<string>();
const query = ref('');
const results = ref<SearchResult[]>([]);
const searching = ref(false);
const searched = ref(false);
const addingKey = ref('');
const loadingVersionKey = ref('');
const expandedKeys = ref(new Set<string>());
const versionsByKey = ref<Record<string, string[]>>({});
const error = ref('');
const selectedRepository = computed(() =>
  props.repositories.find(item => item.id === repositoryId.value),
);
const formatOptions = computed(() =>
  (selectedRepository.value?.packageTypes || []).map(value => ({
    value,
    title: value === 'NU_GET' ? 'NuGet' : 'npm',
  })),
);

watch(model, (open) => {
  if (!open)
    return;

  repositoryId.value = props.fixedRepositoryId || repositoryId.value || props.repositories[0]?.id;
  syncFormat();
});
watch(repositoryId, () => {
  syncFormat();
  results.value = [];
  searched.value = false;
  resetDetails();
});
watch(() => props.repositories, () => {
  if (model.value && !repositoryId.value) {
    repositoryId.value = props.fixedRepositoryId || props.repositories[0]?.id;
    syncFormat();
  }
}, { deep: true });

function syncFormat() {
  const formats = selectedRepository.value?.packageTypes || [];

  if (!packageType.value || !formats.includes(packageType.value))
    packageType.value = formats[0];
}

async function searchPackages() {
  if (!repositoryId.value || !packageType.value || query.value.trim().length < 2)
    return;

  searching.value = true;
  searched.value = true;
  error.value = '';

  try {
    const data = await graphql<{ upstreamPackages: SearchResult[] }>(
      upstreamPackageSearchQuery,
      { repositoryId: repositoryId.value, packageType: packageType.value, search: query.value.trim() },
    );

    results.value = data.upstreamPackages;
    resetDetails();
  }
  catch (e) {
    error.value = (e as Error).message;
    results.value = [];
  }
  finally {
    searching.value = false;
  }
}

function resultKey(item: SearchResult) {
  return `${item.upstreamId}:${item.name}`;
}

function resetDetails() {
  expandedKeys.value = new Set();
  versionsByKey.value = {};
}

function toggleDetails(item: SearchResult) {
  const key = resultKey(item);
  const next = new Set(expandedKeys.value);

  if (next.has(key)) {
    next.delete(key);
    expandedKeys.value = next;
    return;
  }

  next.add(key);
  expandedKeys.value = next;
}

async function loadVersions(item: SearchResult) {
  const key = resultKey(item);

  if (versionsByKey.value[key] || loadingVersionKey.value === key || !repositoryId.value)
    return;

  loadingVersionKey.value = key;
  error.value = '';

  try {
    const data = await graphql<{ upstreamPackageVersions: string[] }>(
      upstreamPackageVersionsQuery,
      {
        repositoryId: repositoryId.value,
        upstreamId: item.upstreamId,
        packageType: item.packageType,
        packageName: item.name,
      },
    );
    const versions = data.upstreamPackageVersions.length ? data.upstreamPackageVersions : [item.version];

    versionsByKey.value = { ...versionsByKey.value, [key]: versions };
  }
  catch (e) {
    error.value = (e as Error).message;
    versionsByKey.value = { ...versionsByKey.value, [key]: [item.version] };
  }
  finally {
    loadingVersionKey.value = '';
  }
}

async function add(item: SearchResult, version: string) {
  if (!repositoryId.value)
    return;

  const key = resultKey(item);

  addingKey.value = key;
  error.value = '';

  try {
    const data = await graphql<{
      addPackageVersion: {
        packageVersion?: { version: string; status: string };
        errors: Array<{ code: string; message: string }>;
      };
    }>(
      addPackageVersionMutation,
      { repositoryId: repositoryId.value, packageType: item.packageType, packageName: item.name, version },
    );

    mutationError(data.addPackageVersion.errors);
    emit('acquired', t('started', { name: item.name, version }));
    model.value = false;
  }
  catch (e) {
    error.value = (e as Error).message;
  }
  finally {
    addingKey.value = '';
  }
}
</script>

<style scoped>
.dialog-body {
  display: flex !important;
  flex-direction: column;
  min-height: 0;
  overflow: hidden !important;
}
.results-card {
  display: flex;
  flex: 1 1 auto;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}
.results-scroll {
  display: grid;
  flex: 1 1 auto;
  grid-template-rows: minmax(0, 1fr);
  min-height: 0;
  overflow: hidden;
}
.expand-column {
  width: 3rem;
  padding-right: 0 !important;
}
.result-details {
  padding: 1rem;
}
.detail-label {
  margin-bottom: 0.25rem;
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--muted-foreground);
}
.result-description {
  margin: 0;
  color: var(--muted-foreground);
  overflow-wrap: anywhere;
}
.version-action {
  width: 10.5rem;
  white-space: nowrap;
}
.version-menu {
  min-width: 12rem;
  max-height: min(22rem, 60dvh);
  overflow-y: auto;
}
@media (max-width: 800px) {
  .search-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>

<i18n lang="json">
{
  "en": {
    "title": "Add package from upstream",
    "about": "Search enabled upstreams, then choose a version from the Add and scan menu. The gateway downloads, verifies, scans, and evaluates it with the repository policies before any client requests it.",
    "repository": "Repository",
    "format": "Format",
    "query": "Package name or keywords",
    "search": "Search upstreams",
    "package": "Package",
    "version": "Latest version",
    "upstream": "Upstream",
    "description": "Description",
    "noDescription": "No description was provided by this upstream.",
    "loadingVersions": "Loading available versions...",
    "expandDetails": "Show details for {name}",
    "collapseDetails": "Hide details for {name}",
    "chooseVersion": "Choose a version of {name} to add and scan",
    "add": "Add and scan",
    "close": "Close",
    "empty": "No upstream packages matched this search.",
    "noRepositories": "Create a repository and enabled upstream before adding packages.",
    "noUpstreams": "This repository has no enabled NuGet or npm upstream to search.",
    "createRepository": "Create repository",
    "started": "Added {name} {version} to the gateway for security evaluation."
  },
  "sv": {
    "title": "Lägg till paket från uppström",
    "about": "Sök i aktiverade uppströmmar och välj sedan en version i menyn Lägg till och skanna. Gatewayen hämtar, verifierar, skannar och utvärderar den med lagringsplatsens policyer innan någon klient begär den.",
    "repository": "Lagringsplats",
    "format": "Format",
    "query": "Paketnamn eller sökord",
    "search": "Sök i uppströmmar",
    "package": "Paket",
    "version": "Senaste version",
    "upstream": "Uppström",
    "description": "Beskrivning",
    "noDescription": "Ingen beskrivning tillhandahölls av uppströmmen.",
    "loadingVersions": "Läser in tillgängliga versioner...",
    "expandDetails": "Visa detaljer för {name}",
    "collapseDetails": "Dölj detaljer för {name}",
    "chooseVersion": "Välj en version av {name} att lägga till och skanna",
    "add": "Lägg till och skanna",
    "close": "Stäng",
    "empty": "Inga paket i uppströmmarna matchade sökningen.",
    "noRepositories": "Skapa en lagringsplats och en aktiverad uppström innan du lägger till paket.",
    "noUpstreams": "Lagringsplatsen har ingen aktiverad NuGet- eller npm-uppström att söka i.",
    "createRepository": "Skapa lagringsplats",
    "started": "{name} {version} har lagts till i gatewayen för säkerhetsutvärdering."
  }
}
</i18n>
