<template>
  <div class="page">
    <div class="flex flex-wrap items-end justify-between gap-4">
      <div>
        <p class="eyebrow">
          {{ t('eyebrow') }}
        </p><h1>{{ t('title') }}</h1><p class="page-lead">
          {{ t('lead') }}
        </p>
      </div><Button @click="openCreate">
        <Plus />{{ t('new') }}
      </Button>
    </div><Alert
      v-if="error"
      variant="destructive"
      class="mt-5"
    >
      <CircleAlert /><AlertDescription>{{ error }}</AlertDescription>
    </Alert>
    <Card class="mt-6 py-0">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{{ t("policy") }}</TableHead>
            <TableHead>{{ t("handler") }}</TableHead>
            <TableHead>{{ t("formats") }}</TableHead>
            <TableHead>{{ t("repositories") }}</TableHead>
            <TableHead>{{ t("status") }}</TableHead>
            <TableHead />
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="policy in policies" :key="policy.id">
            <TableCell class="font-bold">
              {{ policy.name }}
            </TableCell>
            <TableCell class="mono text-xs">
              {{ t(`types.${policy.type}`) }}
            </TableCell>
            <TableCell>{{ displayFormats(policy) }}</TableCell>
            <TableCell>
              <Badge
                v-for="repoId in assignments[policy.id] || []"
                :key="repoId"
                variant="secondary"
                class="mr-1"
              >
                {{ repositories.find((x) => x.id === repoId)?.name }}
              </Badge><span
                v-if="!assignments[policy.id]?.length"
                class="text-muted-foreground"
              >{{ t("unassigned") }}</span>
            </TableCell>
            <TableCell>
              <Switch
                :model-value="policy.enabled"
                @update:model-value="toggle(policy)"
              />
            </TableCell>
            <TableCell class="text-right">
              <Tooltip>
                <TooltipTrigger as-child>
                  <Button
                    size="icon"
                    variant="ghost"
                    :aria-label="t('edit')"
                    @click="openEdit(policy)"
                  >
                    <Pencil />
                  </Button>
                </TooltipTrigger><TooltipContent>{{ t('edit') }}</TooltipContent>
              </Tooltip><Tooltip>
                <TooltipTrigger as-child>
                  <Button
                    size="icon"
                    variant="ghost"
                    class="text-destructive hover:text-destructive"
                    :aria-label="t('delete')"
                    @click="confirmDelete(policy)"
                  >
                    <Trash2 />
                  </Button>
                </TooltipTrigger><TooltipContent>{{ t('delete') }}</TooltipContent>
              </Tooltip>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Card>
    <Dialog v-model:open="dialog">
      <DialogContent size="xl" scrollable>
        <DialogHeader>
          <DialogTitle>
            {{
              t(editingId ? "edit" : "create")
            }}
          </DialogTitle>
        </DialogHeader><div data-slot="dialog-body" class="dialog-scroll-body grid gap-4">
          <div class="config-grid">
            <Field>
              <FieldLabel for="policy-name">
                {{ t('name') }}
              </FieldLabel><Input id="policy-name" v-model="form.name" />
            </Field><Field>
              <FieldLabel for="policy-type">
                {{ t('handler') }}
              </FieldLabel><Select v-model="form.type" :disabled="!!editingId">
                <SelectTrigger id="policy-type">
                  <SelectValue />
                </SelectTrigger><SelectContent>
                  <SelectItem v-for="option in typeOptions" :key="option.value" :value="option.value">
                    {{ option.title }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </Field>
          </div>
          <div class="config-grid">
            <Field>
              <FieldLabel>{{ t('applies') }}</FieldLabel><div class="grid gap-3 rounded-md border p-3">
                <label class="flex items-center gap-2"><Checkbox :model-value="form.packageTypes.includes('NU_GET')" @update:model-value="toggleValue(form.packageTypes, 'NU_GET', $event === true)" />NuGet</label><label class="flex items-center gap-2"><Checkbox :model-value="form.packageTypes.includes('NPM')" @update:model-value="toggleValue(form.packageTypes, 'NPM', $event === true)" />npm</label>
              </div>
            </Field><Field>
              <FieldLabel>{{ t('assign') }}</FieldLabel><div class="grid max-h-36 gap-3 overflow-y-auto rounded-md border p-3">
                <label v-for="repository in repositories" :key="repository.id" class="flex items-center gap-2"><Checkbox :model-value="form.repositoryIds.includes(repository.id)" @update:model-value="toggleValue(form.repositoryIds, repository.id, $event === true)" />{{ repository.name }}</label>
              </div>
            </Field>
          </div>
          <Separator />
          <div v-if="form.type === 'CooldownPolicy'" class="config-grid">
            <Field>
              <FieldLabel for="cooldown-hours">
                {{ t('cooldownHours') }}
              </FieldLabel><Input id="cooldown-hours" v-model.number="config.hours" type="number" min="0" max="8760" />
            </Field><Field>
              <FieldLabel for="cooldown-action">
                {{ t('cooldownAction') }}
              </FieldLabel><Select v-model="config.action">
                <SelectTrigger id="cooldown-action">
                  <SelectValue />
                </SelectTrigger><SelectContent>
                  <SelectItem v-for="option in actionOptions" :key="option.value" :value="option.value">
                    {{ option.title }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </Field>
          </div>
          <div v-else-if="form.type === 'LicensePolicy'">
            <Field>
              <FieldLabel>{{ t('allowedLicenses') }}</FieldLabel><TagsInput v-model="config.allowed">
                <TagsInputItem v-for="entry in config.allowed" :key="entry" :value="entry">
                  <TagsInputItemText /><TagsInputItemDelete />
                </TagsInputItem><TagsInputInput />
              </TagsInput>
            </Field><Field class="mt-4">
              <FieldLabel>{{ t('reviewLicenses') }}</FieldLabel><TagsInput v-model="config.manualReview">
                <TagsInputItem v-for="entry in config.manualReview" :key="entry" :value="entry">
                  <TagsInputItemText /><TagsInputItemDelete />
                </TagsInputItem><TagsInputInput />
              </TagsInput>
            </Field><Field class="mt-4">
              <FieldLabel for="unknown-license">
                {{ t('unknownLicense') }}
              </FieldLabel><Select v-model="config.unknown">
                <SelectTrigger id="unknown-license">
                  <SelectValue />
                </SelectTrigger><SelectContent>
                  <SelectItem v-for="option in actionOptions" :key="option.value" :value="option.value">
                    {{ option.title }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </Field>
          </div>
          <div
            v-else-if="
              form.type === 'PackageDenyPolicy'
                || form.type === 'PackageAllowPolicy'
            "
          >
            <Field>
              <FieldLabel>{{ t('patterns') }}</FieldLabel><TagsInput v-model="config.entries">
                <TagsInputItem v-for="entry in config.entries" :key="entry" :value="entry">
                  <TagsInputItemText /><TagsInputItemDelete />
                </TagsInputItem><TagsInputInput />
              </TagsInput><FieldDescription>{{ t('patternsHint') }}</FieldDescription>
            </Field>
          </div>
          <div v-else class="config-grid">
            <Field v-for="field in actionFields" :key="field">
              <FieldLabel :for="`action-${field}`">
                {{ actionLabels[field] }}
              </FieldLabel><Select
                v-model="config[field]"
              >
                <SelectTrigger :id="`action-${field}`">
                  <SelectValue />
                </SelectTrigger><SelectContent>
                  <SelectItem v-for="option in actionOptions" :key="option.value" :value="option.value">
                    {{ option.title }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </Field>
          </div>
          <Accordion type="single" collapsible class="mt-5 rounded-lg border px-4">
            <AccordionItem value="advanced">
              <AccordionTrigger>{{ t('advanced') }}</AccordionTrigger><AccordionContent><label class="mb-4 flex items-center gap-2"><Switch v-model="advanced" />{{ t('editJson') }}</label><Textarea v-model="jsonText" class="mono" rows="10" :readonly="!advanced" /></AccordionContent>
            </AccordionItem>
          </Accordion>
        </div><DialogFooter>
          <Button variant="outline" @click="dialog = false">
            {{
              t("cancel")
            }}
          </Button><Button
            :disabled="!form.name || !form.packageTypes.length"
            @click="save"
          >
            {{ t(editingId ? "save" : "create") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    <ConfirmDialog
      v-if="deleteTarget"
      :model-value="true"
      :title="t('delete')"
      :message="t('deleteConfirm', { name: deleteTarget.name })"
      :confirm-text="t('delete')"
      :cancel-text="t('cancel')"
      :loading="deleting"
      @update:model-value="deleteTarget = undefined"
      @confirm="remove"
    />
  </div>
</template>

<script setup lang="ts">
import type { PolicyType } from '@/utils/policyForms';
import { Accordion, AccordionContent, AccordionItem, AccordionTrigger, Alert, AlertDescription, Badge, Button, Card, Checkbox, Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle, Field, FieldDescription, FieldLabel, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Separator, Switch, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TagsInput, TagsInputInput, TagsInputItem, TagsInputItemDelete, TagsInputItemText, Textarea, Tooltip, TooltipContent, TooltipTrigger } from '@aditify/ui';
import { CircleAlert, Pencil, Plus, Trash2 } from '@lucide/vue';
import { computed, onMounted, reactive, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { graphql, mutationError } from '@/api/graphql';
import ConfirmDialog from '@/components/ConfirmDialog.vue';
import {
  defaultPolicyConfig,
  parsePolicyConfig,
  policyActions,

  policyTypes,
} from '@/utils/policyForms';

const { t } = useI18n({ useScope: 'local' });

interface Policy {
  id: string;
  name: string;
  type: PolicyType;
  schemaVersion: number;
  configJson: string;
  packageTypes: string[];
  enabled: boolean;
}
interface Repo {
  id: string;
  name: string;
}

const policies = ref<Policy[]>([]);
const repositories = ref<Repo[]>([]);
const assignments = ref<Record<string, string[]>>({});
const dialog = ref(false);
const deleteTarget = ref<Policy>();
const deleting = ref(false);
const editingId = ref<string>();
const error = ref('');
const advanced = ref(false);
const jsonText = ref('');
const config = ref<any>(defaultPolicyConfig('VulnerabilityPolicy'));
const form = reactive({
  name: '',
  type: 'VulnerabilityPolicy' as PolicyType,
  schemaVersion: 1,
  packageTypes: ['NU_GET', 'NPM'] as string[],
  enabled: true,
  repositoryIds: [] as string[],
});
const actionFields = computed(() =>
  form.type === 'VulnerabilityPolicy'
    ? ['critical', 'high', 'medium', 'low']
    : form.type === 'IntegrityPolicy'
      ? ['mismatch', 'invalidSignature', 'unsigned']
      : form.type === 'SignaturePolicy'
        ? ['invalidSignature', 'unsigned']
        : form.type === 'NpmInstallScriptPolicy'
          ? ['action']
          : [],
);
const actionLabels = computed<Record<string, string>>(() => ({
  critical: t('critical'),
  high: t('high'),
  medium: t('medium'),
  low: t('low'),
  mismatch: t('mismatch'),
  invalidSignature: t('invalidSignature'),
  unsigned: t('unsigned'),
  action: t('policyAction'),
}));
const actionOptions = computed(() =>
  policyActions.map(value => ({ title: t(`actions.${value}`), value })),
);
const typeOptions = computed(() =>
  policyTypes.map(value => ({ title: t(`types.${value}`), value })),
);

watch(
  () => form.type,
  () => {
    if (!editingId.value) {
      config.value = defaultPolicyConfig(form.type);
      syncJson();
    }
  },
);
watch(config, syncJson, { deep: true });
function syncJson() {
  if (!advanced.value)
    jsonText.value = JSON.stringify(config.value, null, 2);
}

async function load() {
  error.value = '';

  try {
    const data = await graphql<{
      policies: { nodes: Policy[] };
      repositories: { nodes: Repo[] };
    }>(`
      query {
        policies(first: 100) {
          nodes {
            id
            name
            type
            schemaVersion
            configJson
            packageTypes
            enabled
          }
        }
        repositories(first: 100) {
          nodes {
            id
            name
          }
        }
      }
    `);

    policies.value = data.policies.nodes;
    repositories.value = data.repositories.nodes;

    const map: Record<string, string[]> = {};

    await Promise.all(
      data.repositories.nodes.map(async (repo) => {
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
            { id: repo.id },
          )
        ).policies.nodes;

        for (const item of assigned) (map[item.id] ??= []).push(repo.id);
      }),
    );
    assignments.value = map;
  }
  catch (e) {
    error.value = (e as Error).message;
  }
}
function toggleValue(values: string[], value: string, selected: boolean) {
  const index = values.indexOf(value);

  if (selected && index < 0)
    values.push(value);
  else if (!selected && index >= 0)
    values.splice(index, 1);
}
function openCreate() {
  editingId.value = undefined;
  Object.assign(form, {
    name: '',
    type: 'VulnerabilityPolicy',
    schemaVersion: 1,
    packageTypes: ['NU_GET', 'NPM'],
    enabled: true,
    repositoryIds: [],
  });
  config.value = defaultPolicyConfig('VulnerabilityPolicy');
  advanced.value = false;
  syncJson();
  dialog.value = true;
}
function openEdit(policy: Policy) {
  editingId.value = policy.id;
  Object.assign(form, {
    name: policy.name,
    type: policy.type,
    schemaVersion: policy.schemaVersion,
    packageTypes: [...policy.packageTypes],
    enabled: policy.enabled,
    repositoryIds: [...(assignments.value[policy.id] || [])],
  });
  config.value = parsePolicyConfig(policy.type, policy.configJson);
  jsonText.value = JSON.stringify(JSON.parse(policy.configJson), null, 2);
  advanced.value = false;
  dialog.value = true;
}
async function syncAssignments(policyId: string) {
  const before = new Set(assignments.value[policyId] || []);
  const after = new Set(form.repositoryIds);

  for (const repo of repositories.value) {
    if (before.has(repo.id) === after.has(repo.id))
      continue;

    const operation = after.has(repo.id) ? 'assignPolicy' : 'unassignPolicy';
    const data = await graphql<
      Record<string, { errors: Array<{ code: string; message: string }> }>
    >(
      `mutation($repositoryId:UUID!,$policyId:UUID!){${operation}(repositoryId:$repositoryId,policyId:$policyId){errors{code message}}}`,
      { repositoryId: repo.id, policyId },
    );

    mutationError(data[operation]!.errors);
  }
}
async function save() {
  try {
    const configJson = advanced.value
      ? JSON.stringify(JSON.parse(jsonText.value))
      : JSON.stringify(config.value);
    let policyId = editingId.value;

    if (policyId) {
      const input = {
        id: policyId,
        name: form.name,
        schemaVersion: form.schemaVersion,
        configJson,
        enabled: form.enabled,
        packageTypes: form.packageTypes,
      };
      const data = await graphql<{
        updatePolicy: {
          policy?: { id: string };
          errors: Array<{ code: string; message: string }>;
        };
      }>(
        `
          mutation ($input: UpdatePolicyCommandInput!) {
            updatePolicy(input: $input) {
              policy {
                id
              }
              errors {
                code
                message
              }
            }
          }
        `,
        { input },
      );

      mutationError(data.updatePolicy.errors);
    }
    else {
      const input = {
        name: form.name,
        type: form.type,
        schemaVersion: form.schemaVersion,
        configJson,
        packageTypes: form.packageTypes,
      };
      const data = await graphql<{
        createPolicy: {
          policy?: { id: string };
          errors: Array<{ code: string; message: string }>;
        };
      }>(
        `
          mutation ($input: CreatePolicyCommandInput!) {
            createPolicy(input: $input) {
              policy {
                id
              }
              errors {
                code
                message
              }
            }
          }
        `,
        { input },
      );

      mutationError(data.createPolicy.errors);
      policyId = data.createPolicy.policy?.id;
    }

    if (policyId)
      await syncAssignments(policyId);

    dialog.value = false;
    await load();
  }
  catch (e) {
    error.value = (e as Error).message;
  }
}
async function toggle(policy: Policy) {
  try {
    const input = {
      id: policy.id,
      name: policy.name,
      schemaVersion: policy.schemaVersion,
      configJson: policy.configJson,
      enabled: !policy.enabled,
      packageTypes: policy.packageTypes,
    };
    const data = await graphql<{
      updatePolicy: { errors: Array<{ code: string; message: string }> };
    }>(
      `
        mutation ($input: UpdatePolicyCommandInput!) {
          updatePolicy(input: $input) {
            errors {
              code
              message
            }
          }
        }
      `,
      { input },
    );

    mutationError(data.updatePolicy.errors);
    await load();
  }
  catch (e) {
    error.value = (e as Error).message;
  }
}
function confirmDelete(policy: Policy) {
  deleteTarget.value = policy;
}
async function remove() {
  if (!deleteTarget.value)
    return;

  deleting.value = true;

  try {
    const data = await graphql<{
      deletePolicy: { errors: Array<{ code: string; message: string }> };
    }>(
      `
        mutation ($id: UUID!) {
          deletePolicy(id: $id) {
            errors {
              code
              message
            }
          }
        }
      `,
      { id: deleteTarget.value.id },
    );

    mutationError(data.deletePolicy.errors);
    deleteTarget.value = undefined;
    await load();
  }
  catch (e) {
    error.value = (e as Error).message;
  }
  finally {
    deleting.value = false;
  }
}
function displayFormats(policy: Policy) {
  return policy.packageTypes.length === 2
    ? t('allFormats')
    : policy.packageTypes
        .map(x => (x === 'NU_GET' ? 'NuGet' : 'npm'))
        .join(', ');
}
onMounted(load);
</script>

<i18n lang="json">
{
  "en": {
    "eyebrow": "Evaluation controls",
    "title": "Policies",
    "lead": "Create reusable security controls with guided forms, then assign them to one or more repositories.",
    "new": "New policy",
    "policy": "Policy",
    "handler": "Policy handler",
    "formats": "Formats",
    "repositories": "Repositories",
    "status": "Status",
    "unassigned": "Unassigned",
    "allFormats": "All formats",
    "edit": "Edit policy",
    "delete": "Delete policy",
    "create": "Create policy",
    "save": "Save changes",
    "name": "Policy name",
    "applies": "Applies to formats",
    "assign": "Assign to repositories",
    "cooldownHours": "Cooldown hours",
    "cooldownAction": "Action during cooldown",
    "allowedLicenses": "Allowed SPDX licenses",
    "reviewLicenses": "Licenses requiring manual review",
    "unknownLicense": "Unknown license action",
    "patterns": "Package patterns",
    "patternsHint": "Use package, package{'@'}version, or a trailing wildcard.",
    "advanced": "Advanced JSON",
    "editJson": "Edit JSON directly",
    "cancel": "Cancel",
    "deleteConfirm": "Soft-delete policy “{name}”?",
    "critical": "Critical vulnerabilities",
    "high": "High vulnerabilities",
    "medium": "Medium vulnerabilities",
    "low": "Low vulnerabilities",
    "mismatch": "Digest mismatch",
    "invalidSignature": "Invalid signature",
    "unsigned": "Unsigned package",
    "policyAction": "Policy action",
    "actions": {
      "Allow": "Allow",
      "Warn": "Warn",
      "ManualReview": "Manual review",
      "Quarantine": "Quarantine",
      "Block": "Block"
    },
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
  "sv": {
    "eyebrow": "Utvärderingskontroller",
    "title": "Policyer",
    "lead": "Skapa återanvändbara säkerhetskontroller med vägledda formulär och tilldela dem till en eller flera lagringsplatser.",
    "new": "Ny policy",
    "policy": "Policy",
    "handler": "Policyhanterare",
    "formats": "Format",
    "repositories": "Lagringsplatser",
    "status": "Status",
    "unassigned": "Ej tilldelad",
    "allFormats": "Alla format",
    "edit": "Redigera policy",
    "delete": "Ta bort policy",
    "create": "Skapa policy",
    "save": "Spara ändringar",
    "name": "Policynamn",
    "applies": "Gäller för format",
    "assign": "Tilldela till lagringsplatser",
    "cooldownHours": "Nedkylningsperiod i timmar",
    "cooldownAction": "Åtgärd under nedkylning",
    "allowedLicenses": "Tillåtna SPDX-licenser",
    "reviewLicenses": "Licenser som kräver manuell granskning",
    "unknownLicense": "Åtgärd för okänd licens",
    "patterns": "Paketmönster",
    "patternsHint": "Använd paket, paket{'@'}version eller jokertecken i slutet.",
    "advanced": "Avancerad JSON",
    "editJson": "Redigera JSON direkt",
    "cancel": "Avbryt",
    "deleteConfirm": "Mjukradera policyn “{name}”?",
    "critical": "Kritiska sårbarheter",
    "high": "Sårbarheter med hög allvarlighetsgrad",
    "medium": "Sårbarheter med medelhög allvarlighetsgrad",
    "low": "Sårbarheter med låg allvarlighetsgrad",
    "mismatch": "Felaktig kontrollsumma",
    "invalidSignature": "Ogiltig signatur",
    "unsigned": "Osignerat paket",
    "policyAction": "Policyåtgärd",
    "actions": {
      "Allow": "Tillåt",
      "Warn": "Varna",
      "ManualReview": "Manuell granskning",
      "Quarantine": "Karantän",
      "Block": "Blockera"
    },
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
  }
}
</i18n>
