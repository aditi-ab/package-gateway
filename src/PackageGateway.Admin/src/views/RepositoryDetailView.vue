<template>
  <div>
    <div class="page repository-hero">
      <div class="text-xs mb-2">
        <RouterLink
          to="/repositories"
          class="text-primary no-underline"
        >
          {{ t("repositories") }}
        </RouterLink><span class="mx-2">/</span>{{ repository?.name || t("repository") }}
      </div>
      <div class="flex flex-wrap items-start gap-4">
        <div>
          <div class="flex items-center gap-3">
            <h1 class="page-title">
              {{ repository?.name }}
            </h1>
            <StatusChip
              v-if="repository"
              :status="repository.enabled ? 'healthy' : 'blocked'"
            />
          </div>
          <p class="page-lead">
            {{ repository?.description || t("defaultDescription") }}
          </p>
        </div>
        <Button
          class="ml-auto"
          @click="openUpstream"
        >
          <GitBranchPlus />{{ t("configureUpstream") }}
        </Button>
      </div>
      <div v-if="repository" class="flex flex-wrap gap-2 mt-3">
        <Badge
          v-for="format in repository.packageTypes"
          :key="format"
          variant="secondary"
        >
          {{ format === "NU_GET" ? "NuGet" : "npm" }}
        </Badge><Badge variant="outline" class="mono">
          <LinkIcon />
          {{
            repository.slug
          }}
        </Badge>
      </div>
    </div>
    <Tabs v-model="tab" class="w-full">
      <div class="content-shell overflow-x-auto overflow-y-hidden">
        <TabsList :aria-label="t('repositoryNavigation')">
          <TabsTrigger
            v-for="item in tabs"
            :key="item.value"
            :value="item.value"
          >
            {{ item.label }}
          </TabsTrigger>
        </TabsList>
      </div>
      <div class="page pt-7">
        <Alert
          v-if="error"
          variant="destructive"
          class="mb-5"
        >
          <CircleAlert /><AlertDescription>{{ error }}</AlertDescription>
        </Alert>
        <TabsContent value="packages" class="m-0">
          <PackagesView embedded :repository-id="id" />
        </TabsContent>
        <TabsContent value="review" class="m-0">
          <ReviewQueueView embedded :repository-id="id" />
        </TabsContent>
        <TabsContent value="upstreams" class="m-0">
          <div class="flex items-center mb-5">
            <div>
              <div class="section-heading">
                {{ t("upstreams.title") }}
              </div>
              <div class="text-sm text-muted-foreground">
                {{ t("upstreams.lead") }}
              </div>
            </div>
            <Button
              class="ml-auto"
              @click="openUpstream"
            >
              <Plus />{{ t("configureUpstream") }}
            </Button>
          </div>
          <Card class="py-0">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{{ t("upstreams.upstream") }}</TableHead>
                  <TableHead>{{ t("format") }}</TableHead>
                  <TableHead>{{ t("upstreams.priority") }}</TableHead>
                  <TableHead>{{ t("upstreams.security") }}</TableHead>
                  <TableHead>{{ t("upstreams.health") }}</TableHead>
                  <TableHead>{{ t("upstreams.status") }}</TableHead>
                  <TableHead />
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="item in upstreams" :key="item.id">
                  <TableCell>
                    <div class="font-bold">
                      {{ item.name }}
                    </div>
                    <div class="mono text-xs text-muted-foreground">
                      {{ item.url }}
                    </div>
                  </TableCell>
                  <TableCell>
                    <Badge variant="secondary">
                      {{
                        item.packageType === "NU_GET" ? "NuGet" : "npm"
                      }}
                    </Badge>
                  </TableCell>
                  <TableCell>{{ item.priority }}</TableCell>
                  <TableCell>
                    <Badge variant="success">
                      <Lock />HTTPS
                    </Badge><Badge
                      v-if="item.trusted"
                      variant="warning"
                      class="ml-1"
                    >
                      {{ t("upstreams.trusted") }}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    <StatusChip
                      :status="
                        item.isHealthy === false
                          ? 'blocked'
                          : item.isHealthy === true
                            ? 'healthy'
                            : 'unknown'
                      "
                    />
                    <div class="text-xs text-muted-foreground">
                      {{ item.healthDetail }}
                    </div>
                  </TableCell>
                  <TableCell>{{ t(item.enabled ? "enabled" : "disabled") }}</TableCell>
                  <TableCell class="text-right">
                    <Tooltip>
                      <TooltipTrigger as-child>
                        <Button
                          size="icon" variant="ghost"
                          :aria-label="t('upstreams.edit')"
                          @click="editUpstream(item)"
                        >
                          <Pencil />
                        </Button>
                      </TooltipTrigger><TooltipContent>{{ t('upstreams.edit') }}</TooltipContent>
                    </Tooltip><Tooltip>
                      <TooltipTrigger as-child>
                        <Button
                          size="icon" variant="ghost" class="text-destructive hover:text-destructive"
                          :aria-label="t('upstreams.delete')"
                          @click="confirmDeleteUpstream(item)"
                        >
                          <Trash2 />
                        </Button>
                      </TooltipTrigger><TooltipContent>{{ t('upstreams.delete') }}</TooltipContent>
                    </Tooltip>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
            <div v-if="!upstreams.length && !loading" class="empty-state">
              <div>
                <GitBranch class="mx-auto size-10" />
                <div class="font-bold mt-3">
                  {{ t("upstreams.empty") }}
                </div>
                <Button class="mt-4" @click="openUpstream">
                  {{
                    t("configureUpstream")
                  }}
                </Button>
              </div>
            </div>
          </Card>
        </TabsContent>
        <TabsContent value="policies" class="m-0">
          <div class="flex flex-wrap items-center gap-3 mb-5">
            <div>
              <div class="section-heading">
                {{ t("policies.title") }}
              </div>
              <div class="text-sm text-muted-foreground">
                {{ t("policies.lead") }}
              </div>
            </div>
            <Button as-child class="ml-auto" variant="outline">
              <RouterLink to="/policies">
                <Settings />{{ t('policies.manage') }}
              </RouterLink>
            </Button>
          </div>
          <div class="grid gap-5 lg:grid-cols-2">
            <div>
              <div class="flex items-center mb-3">
                <div class="text-sm font-bold">
                  {{ t("policies.assigned") }}
                </div>
                <Badge variant="secondary" class="ml-2">
                  {{
                    assignedPolicies.length
                  }}
                </Badge>
              </div>
              <Card>
                <ItemGroup v-if="assignedPolicies.length" class="px-3">
                  <Item
                    v-for="policy in assignedPolicies"
                    :key="policy.id"
                  >
                    <ItemMedia><ShieldCheck class="text-primary" /></ItemMedia><ItemContent>
                      <ItemTitle class="font-bold">
                        {{ policy.name }}
                      </ItemTitle><ItemDescription>
                        {{ t(`policies.types.${policy.type}`) }} ·
                        {{ policyFormats(policy) }}
                      </ItemDescription>
                    </ItemContent><ItemActions>
                      <div class="flex items-center gap-2">
                        <Badge
                          :variant="policy.enabled ? 'success' : 'secondary'"
                        >
                          {{ t(policy.enabled ? "enabled" : "disabled") }}
                        </Badge>
                        <Button
                          variant="ghost" class="text-destructive hover:text-destructive"
                          size="sm"
                          :disabled="policyActionId === policy.id"
                          @click="confirmUnassignPolicy(policy)"
                        >
                          {{ t("policies.remove") }}
                        </Button>
                      </div>
                    </ItemActions>
                  </Item>
                </ItemGroup>
                <div v-else class="empty-state policy-empty-state">
                  <div>
                    <ShieldOff class="mx-auto size-9" />
                    <div class="font-bold mt-2">
                      {{ t("policies.noneAssigned") }}
                    </div>
                  </div>
                </div>
              </Card>
            </div>
            <div>
              <div class="flex items-center mb-3">
                <div class="text-sm font-bold">
                  {{ t("policies.available") }}
                </div>
                <Badge variant="secondary" class="ml-2">
                  {{
                    availablePolicies.length
                  }}
                </Badge>
              </div>
              <Card>
                <ItemGroup v-if="availablePolicies.length" class="px-3">
                  <Item
                    v-for="policy in availablePolicies"
                    :key="policy.id"
                  >
                    <ItemMedia><Shield :class="policy.enabled ? 'text-primary' : undefined" /></ItemMedia><ItemContent>
                      <ItemTitle class="font-bold">
                        {{ policy.name }}
                      </ItemTitle><ItemDescription>
                        {{ t(`policies.types.${policy.type}`) }} ·
                        {{ policyFormats(policy) }}
                      </ItemDescription>
                    </ItemContent><ItemActions>
                      <div class="flex items-center gap-2">
                        <Badge v-if="!policy.enabled" variant="secondary">
                          {{ t("disabled") }}
                        </Badge>
                        <Button
                          variant="outline" size="sm"
                          :disabled="!policy.enabled"
                          @click="changePolicyAssignment(policy, true)"
                        >
                          {{ t("policies.assign") }}
                        </Button>
                      </div>
                    </ItemActions>
                  </Item>
                </ItemGroup>
                <div v-else class="empty-state policy-empty-state">
                  <div>
                    <ShieldCheck class="mx-auto size-9" />
                    <div class="font-bold mt-2">
                      {{ t("policies.noneAvailable") }}
                    </div>
                  </div>
                </div>
              </Card>
            </div>
          </div>
        </TabsContent>
        <TabsContent value="settings" class="m-0">
          <div class="grid gap-5 lg:grid-cols-3">
            <Card class="p-6 lg:col-span-2">
              <div class="section-heading mb-5">
                {{ t("settings.title") }}
              </div>
              <form class="grid gap-4" @submit.prevent="saveSettings">
                <Field>
                  <FieldLabel for="repository-name">
                    {{ t('settings.name') }}
                  </FieldLabel><Input id="repository-name" v-model="settings.name" />
                </Field><Field>
                  <FieldLabel for="repository-description">
                    {{ t('settings.description') }}
                  </FieldLabel><Textarea id="repository-description" v-model="settings.description" rows="3" />
                </Field><div class="flex flex-wrap items-center gap-4">
                  <label class="flex items-center gap-2"><Switch v-model="settings.enabled" />{{ t('settings.enabled') }}</label><Button type="submit">
                    {{
                      t("settings.save")
                    }}
                  </Button>
                </div>
              </form>
            </Card>
            <div>
              <Card class="p-6">
                <div class="section-heading">
                  {{ t("settings.endpoints") }}
                </div>
                <div class="text-xs text-muted-foreground mt-1 mb-4">
                  {{ t("settings.endpointsLead") }}
                </div>
                <div class="text-xs">
                  NuGet
                </div>
                <div
                  class="mono text-xs p-3 bg-muted rounded-md mt-1 mb-4"
                >
                  {{ endpoint("NU_GET") }}
                </div>
                <div class="text-xs">
                  npm
                </div>
                <div class="mono text-xs p-3 bg-muted rounded-md mt-1">
                  {{ endpoint("NPM") }}
                </div>
              </Card><Card class="mt-5 p-6">
                <div class="section-heading text-destructive">
                  {{ t("settings.danger") }}
                </div>
                <p class="text-sm text-muted-foreground mt-2">
                  {{ t("settings.dangerLead") }}
                </p>
                <Button
                  variant="destructive"
                  @click="disableRepositoryDialog = true"
                >
                  {{ t("settings.disable") }}
                </Button>
              </Card>
            </div>
          </div>
        </TabsContent>
      </div>
    </Tabs>
    <Dialog v-model:open="upstreamDialog">
      <DialogContent :size="upstreamStep === 'choose' ? 'xl' : 'lg'" scrollable>
        <DialogHeader>
          <DialogTitle>
            {{
              t(
                editingUpstream
                  ? "dialog.edit"
                  : upstreamStep === "choose"
                    ? "dialog.configure"
                    : "dialog.details",
              )
            }}
          </DialogTitle>
        </DialogHeader><div data-slot="dialog-body" class="dialog-scroll-body">
          <div v-if="upstreamStep === 'choose'">
            <div class="config-grid">
              <Card
                v-for="preset in presets"
                :key="preset.title"
                class="click-row p-5"
                role="button" tabindex="0"
                @click="choosePreset(preset)"
                @keydown.enter="choosePreset(preset)"
              >
                <div class="flex items-center gap-4">
                  <div class="format-icon bg-muted">
                    <Package />
                  </div>
                  <div>
                    <div class="font-bold">
                      {{ preset.title }}
                    </div>
                    <div class="mono text-xs text-muted-foreground">
                      {{ preset.subtitle }}
                    </div>
                  </div>
                </div>
              </Card><Card class="click-row p-5" role="button" tabindex="0" @click="manual" @keydown.enter="manual">
                <div class="flex items-center gap-4">
                  <div class="format-icon bg-muted">
                    <GitBranch />
                  </div>
                  <div>
                    <div class="font-bold">
                      {{ t("dialog.manual") }}
                    </div>
                    <div class="text-xs text-muted-foreground">
                      {{ t("dialog.manualLead") }}
                    </div>
                  </div>
                </div>
              </Card>
            </div>
          </div><div v-else class="grid gap-4">
            <div class="config-grid">
              <Field>
                <FieldLabel for="upstream-format">
                  {{ t('dialog.format') }}
                </FieldLabel><Select v-model="upstreamForm.packageType">
                  <SelectTrigger id="upstream-format">
                    <SelectValue />
                  </SelectTrigger><SelectContent>
                    <SelectItem value="NU_GET">
                      NuGet
                    </SelectItem><SelectItem value="NPM">
                      npm
                    </SelectItem>
                  </SelectContent>
                </Select>
              </Field><Field>
                <FieldLabel for="upstream-priority">
                  {{ t('upstreams.priority') }}
                </FieldLabel><Input id="upstream-priority" v-model.number="upstreamForm.priority" type="number" min="0" />
              </Field>
            </div>
            <Field>
              <FieldLabel for="upstream-name">
                {{ t('dialog.name') }}
              </FieldLabel><Input id="upstream-name" v-model="upstreamForm.name" />
            </Field><Field>
              <FieldLabel for="upstream-url">
                {{ t('dialog.url') }}
              </FieldLabel><Input id="upstream-url" v-model="upstreamForm.url" />
            </Field><label class="flex items-center gap-2"><Switch v-model="upstreamForm.trusted" />{{ t('dialog.trusted') }}</label><label v-if="editingUpstream" class="flex items-center gap-2"><Switch
              v-if="editingUpstream"
              v-model="upstreamForm.enabled"
            />{{ t('dialog.enabled') }}</label>
          </div>
        </div><DialogFooter v-if="upstreamStep === 'form'">
          <Button
            v-if="!editingUpstream"
            variant="outline"
            @click="upstreamStep = 'choose'"
          >
            {{ t("dialog.back") }}
          </Button><Button variant="outline" @click="upstreamDialog = false">
            {{
              t("dialog.cancel")
            }}
          </Button><Button
            :disabled="
              !upstreamForm.name || !upstreamForm.url.startsWith('https://')
            "
            @click="saveUpstream"
          >
            {{ t("dialog.save") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    <ConfirmDialog
      v-if="deleteUpstreamTarget"
      :model-value="true"
      :title="t('upstreams.delete')"
      :message="t('confirmUpstream', { name: deleteUpstreamTarget.name })"
      :confirm-text="t('upstreams.delete')"
      :cancel-text="t('dialog.cancel')"
      :loading="destructiveActionBusy"
      @update:model-value="deleteUpstreamTarget = undefined"
      @confirm="deleteUpstream"
    />
    <ConfirmDialog
      v-if="repository"
      v-model="disableRepositoryDialog"
      :title="t('settings.disable')"
      :message="t('confirmRepository', { name: repository.name })"
      :confirm-text="t('settings.disable')"
      :cancel-text="t('dialog.cancel')"
      :loading="destructiveActionBusy"
      @confirm="removeRepository"
    />
    <ConfirmDialog
      v-if="unassignPolicyTarget"
      :model-value="true"
      :title="t('policies.removeTitle')"
      :message="
        t('policies.removeConfirm', { name: unassignPolicyTarget.name })
      "
      :confirm-text="t('policies.remove')"
      :cancel-text="t('dialog.cancel')"
      :loading="policyActionId === unassignPolicyTarget.id"
      @update:model-value="unassignPolicyTarget = undefined"
      @confirm="unassignSelectedPolicy"
    />
  </div>
</template>

<script setup lang="ts">
import { Alert, AlertDescription, Badge, Button, Card, Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle, Field, FieldLabel, Input, Item, ItemActions, ItemContent, ItemDescription, ItemGroup, ItemMedia, ItemTitle, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Switch, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, Tabs, TabsContent, TabsList, TabsTrigger, Textarea, Tooltip, TooltipContent, TooltipTrigger } from '@aditify/ui';
import { CircleAlert, GitBranch, GitBranchPlus, Link as LinkIcon, Lock, Package, Pencil, Plus, Settings, Shield, ShieldCheck, ShieldOff, Trash2 } from '@lucide/vue';
import { computed, onMounted, reactive, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink, useRoute, useRouter } from 'vue-router';
import { graphql, mutationError } from '@/api/graphql';
import ConfirmDialog from '@/components/ConfirmDialog.vue';
import StatusChip from '@/components/StatusChip.vue';
import PackagesView from '@/views/PackagesView.vue';
import ReviewQueueView from '@/views/ReviewQueueView.vue';

const { t } = useI18n({ useScope: 'local' });

interface Repo {
  id: string;
  name: string;
  slug: string;
  packageTypes: string[];
  enabled: boolean;
  description?: string;
  updatedAt: string;
}
interface Upstream {
  id: string;
  name: string;
  url: string;
  packageType: string;
  priority: number;
  enabled: boolean;
  trusted: boolean;
  isHealthy?: boolean;
  lastHealthCheckAt?: string;
  healthDetail?: string;
}
interface Policy {
  id: string;
  name: string;
  type: string;
  packageTypes: string[];
  enabled: boolean;
}

const route = useRoute();
const router = useRouter();
const id = computed(() => String(route.params.id));
const tab = ref(String(route.params.tab || 'packages'));
const repository = ref<Repo>();
const upstreams = ref<Upstream[]>([]);
const policies = ref<Policy[]>([]);
const assignedIds = ref(new Set<string>());
const policyActionId = ref<string>();
const unassignPolicyTarget = ref<Policy>();
const error = ref('');
const loading = ref(false);
const upstreamDialog = ref(false);
const deleteUpstreamTarget = ref<Upstream>();
const disableRepositoryDialog = ref(false);
const destructiveActionBusy = ref(false);
const upstreamStep = ref<'choose' | 'form'>('choose');
const editingUpstream = ref<string>();
const upstreamForm = reactive({
  name: '',
  url: 'https://',
  packageType: 'NU_GET',
  priority: 1,
  enabled: true,
  trusted: false,
});
const settings = reactive({ name: '', description: '', enabled: true });
const tabs = computed(() => [
  { label: t('tabs.packages'), value: 'packages' },
  { label: t('tabs.review'), value: 'review' },
  { label: t('tabs.upstreams'), value: 'upstreams' },
  { label: t('tabs.policies'), value: 'policies' },
  { label: t('tabs.settings'), value: 'settings' },
]);
const assignedPolicies = computed(() =>
  policies.value.filter(policy => assignedIds.value.has(policy.id)),
);
const availablePolicies = computed(() =>
  policies.value.filter(policy => !assignedIds.value.has(policy.id)),
);
const presets = [
  {
    title: 'NuGet.org',
    subtitle: 'api.nuget.org/v3/index.json',
    icon: 'nuget',
    color: 'info',
    value: {
      name: 'NuGet.org',
      url: 'https://api.nuget.org/v3/index.json',
      packageType: 'NU_GET',
    },
  },
  {
    title: 'npmjs',
    subtitle: 'registry.npmjs.org',
    icon: 'npm',
    color: 'danger',
    value: {
      name: 'npmjs',
      url: 'https://registry.npmjs.org',
      packageType: 'NPM',
    },
  },
];

async function load() {
  loading.value = true;
  error.value = '';

  try {
    const data = await graphql<{
      repository: Repo | null;
      upstreams: Upstream[];
      policies: { nodes: Policy[] };
    }>(
      `
        query ($id: UUID!) {
          repository(id: $id) {
            id
            name
            slug
            packageTypes
            enabled
            description
            updatedAt
          }
          upstreams(repositoryId: $id) {
            id
            name
            url
            packageType
            priority
            enabled
            trusted
            isHealthy
            lastHealthCheckAt
            healthDetail
          }
          policies(first: 100) {
            nodes {
              id
              name
              type
              packageTypes
              enabled
            }
          }
        }
      `,
      { id: id.value },
    );

    if (!data.repository) {
      await router.replace('/repositories');
      return;
    }

    repository.value = data.repository;
    upstreams.value = data.upstreams;
    policies.value = data.policies.nodes;
    Object.assign(settings, {
      name: data.repository.name,
      description: data.repository.description || '',
      enabled: data.repository.enabled,
    });

    const assigned = (
      await graphql<{ policies: { nodes: Array<{ id: string }> } }>(
        `
          query ($id: UUID!) {
            policies(repositoryId: $id, first: 100) {
              nodes {
                id
              }
            }
          }
        `,
        { id: id.value },
      )
    ).policies.nodes;

    assignedIds.value = new Set(assigned.map(x => x.id));
  }
  catch (e) {
    error.value = (e as Error).message;
  }
  finally {
    loading.value = false;
  }
}
function endpoint(format: string) {
  if (!repository.value)
    return '';

  return format === 'NU_GET'
    ? `${location.origin}/nuget/${repository.value.slug}/v3/index.json`
    : `${location.origin}/npm/${repository.value.slug}/`;
}
function openUpstream() {
  editingUpstream.value = undefined;
  upstreamStep.value = 'choose';
  Object.assign(upstreamForm, {
    name: '',
    url: 'https://',
    packageType: 'NU_GET',
    priority: 1,
    enabled: true,
    trusted: false,
  });
  upstreamDialog.value = true;
}
function choosePreset(preset: (typeof presets)[number]) {
  Object.assign(upstreamForm, preset.value, {
    priority: nextPriority(preset.value.packageType),
    enabled: true,
    trusted: false,
  });
  upstreamStep.value = 'form';
}
function manual() {
  Object.assign(upstreamForm, {
    name: '',
    url: 'https://',
    packageType: 'NU_GET',
    priority: nextPriority('NU_GET'),
    enabled: true,
    trusted: false,
  });
  upstreamStep.value = 'form';
}
function nextPriority(type: string) {
  return (
    Math.max(
      0,
      ...upstreams.value
        .filter(x => x.packageType === type)
        .map(x => x.priority),
    ) + 1
  );
}
function editUpstream(x: Upstream) {
  editingUpstream.value = x.id;
  Object.assign(upstreamForm, x);
  upstreamStep.value = 'form';
  upstreamDialog.value = true;
}
async function saveUpstream() {
  try {
    if (editingUpstream.value) {
      const input = { id: editingUpstream.value, ...upstreamForm };
      const data = await graphql<{
        updateUpstream: { errors: Array<{ code: string; message: string }> };
      }>(
        `
          mutation ($input: UpdateUpstreamCommandInput!) {
            updateUpstream(input: $input) {
              errors {
                code
                message
              }
            }
          }
        `,
        { input },
      );

      mutationError(data.updateUpstream.errors);
    }
    else {
      const input = {
        repositoryId: id.value,
        name: upstreamForm.name,
        url: upstreamForm.url,
        packageType: upstreamForm.packageType,
        priority: upstreamForm.priority,
        trusted: upstreamForm.trusted,
      };
      const data = await graphql<{
        createUpstream: { errors: Array<{ code: string; message: string }> };
      }>(
        `
          mutation ($input: CreateUpstreamCommandInput!) {
            createUpstream(input: $input) {
              errors {
                code
                message
              }
            }
          }
        `,
        { input },
      );

      mutationError(data.createUpstream.errors);
    }

    upstreamDialog.value = false;
    await load();
  }
  catch (e) {
    error.value = (e as Error).message;
  }
}
function confirmDeleteUpstream(upstream: Upstream) {
  deleteUpstreamTarget.value = upstream;
}
async function deleteUpstream() {
  if (!deleteUpstreamTarget.value)
    return;

  destructiveActionBusy.value = true;

  try {
    const data = await graphql<{
      deleteUpstream: { errors: Array<{ code: string; message: string }> };
    }>(
      `
        mutation ($id: UUID!) {
          deleteUpstream(id: $id) {
            errors {
              code
              message
            }
          }
        }
      `,
      { id: deleteUpstreamTarget.value.id },
    );

    mutationError(data.deleteUpstream.errors);
    deleteUpstreamTarget.value = undefined;
    await load();
  }
  catch (e) {
    error.value = (e as Error).message;
  }
  finally {
    destructiveActionBusy.value = false;
  }
}
async function changePolicyAssignment(policy: Policy, assigned: boolean) {
  policyActionId.value = policy.id;

  const operation = assigned ? 'assignPolicy' : 'unassignPolicy';

  try {
    const data = await graphql<
      Record<string, { errors: Array<{ code: string; message: string }> }>
    >(
      `mutation($repositoryId:UUID!,$policyId:UUID!){${operation}(repositoryId:$repositoryId,policyId:$policyId){errors{code message}}}`,
      { repositoryId: id.value, policyId: policy.id },
    );

    mutationError(data[operation]!.errors);

    const next = new Set(assignedIds.value);

    assigned ? next.add(policy.id) : next.delete(policy.id);
    assignedIds.value = next;

    if (!assigned)
      unassignPolicyTarget.value = undefined;
  }
  catch (e) {
    error.value = (e as Error).message;
  }
  finally {
    policyActionId.value = undefined;
  }
}
function confirmUnassignPolicy(policy: Policy) {
  unassignPolicyTarget.value = policy;
}
async function unassignSelectedPolicy() {
  if (unassignPolicyTarget.value)
    await changePolicyAssignment(unassignPolicyTarget.value, false);
}
function policyFormats(policy: Policy) {
  return policy.packageTypes
    .map(value => (value === 'NU_GET' ? 'NuGet' : 'npm'))
    .join(', ');
}
async function saveSettings() {
  try {
    const input = { id: id.value, ...settings };
    const data = await graphql<{
      updateRepository: { errors: Array<{ code: string; message: string }> };
    }>(
      `
        mutation ($input: UpdateRepositoryCommandInput!) {
          updateRepository(input: $input) {
            errors {
              code
              message
            }
          }
        }
      `,
      { input },
    );

    mutationError(data.updateRepository.errors);
    await load();
  }
  catch (e) {
    error.value = (e as Error).message;
  }
}
async function removeRepository() {
  if (!repository.value)
    return;

  destructiveActionBusy.value = true;

  try {
    const data = await graphql<{
      deleteRepository: { errors: Array<{ code: string; message: string }> };
    }>(
      `
        mutation ($id: UUID!) {
          deleteRepository(id: $id) {
            errors {
              code
              message
            }
          }
        }
      `,
      { id: id.value },
    );

    mutationError(data.deleteRepository.errors);
    disableRepositoryDialog.value = false;
    await router.push('/repositories');
  }
  catch (e) {
    error.value = (e as Error).message;
  }
  finally {
    destructiveActionBusy.value = false;
  }
}
watch(id, load);
onMounted(load);
</script>

<i18n lang="json">
{
  "en": {
    "repositories": "Repositories",
    "repository": "Repository",
    "defaultDescription": "A secure container for NuGet and npm upstreams.",
    "configureUpstream": "Configure upstream",
    "repositoryNavigation": "Repository navigation",
    "format": "Format",
    "enabled": "Enabled",
    "disabled": "Disabled",
    "confirmUpstream": "Disable upstream “{name}”?",
    "confirmRepository": "Disable repository “{name}”? Approved bytes and audit history will be retained.",
    "tabs": {
      "packages": "Packages",
      "review": "Review queue",
      "upstreams": "Upstreams",
      "policies": "Policies",
      "settings": "Settings"
    },
    "packages": {
      "title": "Cached packages",
      "lead": "Packages observed through any configured upstream.",
      "filter": "Filter packages",
      "package": "Package",
      "identity": "Normalized identity",
      "noMatch": "No packages match this filter",
      "empty": "No packages observed",
      "noMatchLead": "Try a different package name.",
      "emptyLead": "Configure an upstream and request a package through the gateway."
    },
    "upstreams": {
      "title": "Upstream proxies",
      "lead": "Resolution priority is independent for each package format.",
      "upstream": "Upstream",
      "priority": "Priority",
      "security": "Security",
      "health": "Health",
      "status": "Status",
      "trusted": "Trusted network",
      "edit": "Edit upstream",
      "delete": "Delete upstream",
      "empty": "No upstreams configured"
    },
    "policies": {
      "title": "Repository policies",
      "lead": "Assign reusable controls. Format targeting is configured on each policy.",
      "manage": "Manage policies",
      "assigned": "Assigned policies",
      "available": "Available policies",
      "assign": "Assign",
      "remove": "Remove",
      "removeTitle": "Remove policy assignment",
      "removeConfirm": "Remove “{name}” from this repository? Future acquisitions and rescans will no longer be evaluated by this policy.",
      "noneAssigned": "No policies are assigned",
      "noneAvailable": "All policies are assigned",
      "types": {
        "VulnerabilityPolicy": "Vulnerability severity",
        "CooldownPolicy": "Cooldown",
        "LicensePolicy": "Licenses",
        "IntegrityPolicy": "Integrity",
        "SignaturePolicy": "Signature",
        "NpmInstallScriptPolicy": "npm install scripts",
        "PackageDenyPolicy": "Package deny list",
        "PackageAllowPolicy": "Package allow list"
      }
    },
    "settings": {
      "title": "General settings",
      "name": "Display name",
      "description": "Description",
      "enabled": "Repository enabled",
      "save": "Save changes",
      "endpoints": "Client endpoints",
      "endpointsLead": "Endpoints become active when the matching format has an upstream.",
      "danger": "Danger zone",
      "dangerLead": "Disabling retains approved artifacts and audit history.",
      "disable": "Disable repository"
    },
    "dialog": {
      "edit": "Edit upstream",
      "configure": "Configure upstream",
      "details": "Upstream details",
      "manual": "Manual setup",
      "manualLead": "Custom NuGet service index or npm registry",
      "format": "Package format",
      "name": "Name",
      "url": "HTTPS service index or registry URL",
      "trusted": "Allow trusted private-network destination",
      "enabled": "Upstream enabled",
      "back": "Back",
      "cancel": "Cancel",
      "save": "Save upstream"
    }
  },
  "sv": {
    "repositories": "Lagringsplatser",
    "repository": "Lagringsplats",
    "defaultDescription": "En säker behållare för NuGet- och npm-uppströmmar.",
    "configureUpstream": "Konfigurera uppström",
    "repositoryNavigation": "Navigering för lagringsplats",
    "format": "Format",
    "enabled": "Aktiverad",
    "disabled": "Inaktiverad",
    "confirmUpstream": "Inaktivera uppströmmen “{name}”?",
    "confirmRepository": "Inaktivera lagringsplatsen “{name}”? Godkända artefakter och revisionshistorik behålls.",
    "tabs": {
      "packages": "Paket",
      "review": "Granskningskö",
      "upstreams": "Uppströmmar",
      "policies": "Policyer",
      "settings": "Inställningar"
    },
    "packages": {
      "title": "Cachelagrade paket",
      "lead": "Paket som observerats via en konfigurerad uppström.",
      "filter": "Filtrera paket",
      "package": "Paket",
      "identity": "Normaliserad identitet",
      "noMatch": "Inga paket matchar filtret",
      "empty": "Inga paket har observerats",
      "noMatchLead": "Prova ett annat paketnamn.",
      "emptyLead": "Konfigurera en uppström och begär ett paket genom gatewayen."
    },
    "upstreams": {
      "title": "Uppströmsproxyer",
      "lead": "Prioriteringen för upplösning är oberoende för varje paketformat.",
      "upstream": "Uppström",
      "priority": "Prioritet",
      "security": "Säkerhet",
      "health": "Hälsa",
      "status": "Status",
      "trusted": "Betrott nätverk",
      "edit": "Redigera uppström",
      "delete": "Ta bort uppström",
      "empty": "Inga uppströmmar har konfigurerats"
    },
    "policies": {
      "title": "Policyer för lagringsplats",
      "lead": "Tilldela återanvändbara kontroller. Formatmål konfigureras för varje policy.",
      "manage": "Hantera policyer",
      "assigned": "Tilldelade policyer",
      "available": "Tillgängliga policyer",
      "assign": "Tilldela",
      "remove": "Ta bort",
      "removeTitle": "Ta bort policytilldelning",
      "removeConfirm": "Ta bort “{name}” från den här lagringsplatsen? Framtida hämtningar och omskanningar utvärderas inte längre av policyn.",
      "noneAssigned": "Inga policyer är tilldelade",
      "noneAvailable": "Alla policyer är tilldelade",
      "types": {
        "VulnerabilityPolicy": "Sårbarhetsgrad",
        "CooldownPolicy": "Nedkylning",
        "LicensePolicy": "Licenser",
        "IntegrityPolicy": "Integritet",
        "SignaturePolicy": "Signatur",
        "NpmInstallScriptPolicy": "npm-installationsskript",
        "PackageDenyPolicy": "Nekningslista för paket",
        "PackageAllowPolicy": "Tillåtelselista för paket"
      }
    },
    "settings": {
      "title": "Allmänna inställningar",
      "name": "Visningsnamn",
      "description": "Beskrivning",
      "enabled": "Lagringsplats aktiverad",
      "save": "Spara ändringar",
      "endpoints": "Klientslutpunkter",
      "endpointsLead": "Slutpunkter aktiveras när motsvarande format har en uppström.",
      "danger": "Riskzon",
      "dangerLead": "Inaktivering behåller godkända artefakter och revisionshistorik.",
      "disable": "Inaktivera lagringsplats"
    },
    "dialog": {
      "edit": "Redigera uppström",
      "configure": "Konfigurera uppström",
      "details": "Uppströmsinformation",
      "manual": "Manuell konfiguration",
      "manualLead": "Anpassat NuGet-tjänsteindex eller npm-register",
      "format": "Paketformat",
      "name": "Namn",
      "url": "HTTPS-tjänsteindex eller register-URL",
      "trusted": "Tillåt betrodd destination i privat nätverk",
      "enabled": "Uppström aktiverad",
      "back": "Tillbaka",
      "cancel": "Avbryt",
      "save": "Spara uppström"
    }
  }
}
</i18n>

<style scoped>
.policy-empty-state {
  min-height: 144px;
}
</style>
