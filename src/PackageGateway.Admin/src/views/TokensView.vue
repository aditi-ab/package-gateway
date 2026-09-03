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
        <KeyRound />{{ t('create') }}
      </Button>
    </div><Alert
      v-if="error"
      variant="destructive"
      class="mt-5"
    >
      <CircleAlert /><AlertDescription>{{ error }}</AlertDescription><AlertAction>
        <Button variant="ghost" size="icon" :aria-label="t('close')" @click="error = ''">
          <X />
        </Button>
      </AlertAction>
    </Alert><Card class="mt-6 py-0">
      <Table>
        <TableHeader><TableRow><TableHead>{{ t('name') }}</TableHead><TableHead>{{ t('identifier') }}</TableHead><TableHead>{{ t('access') }}</TableHead><TableHead>{{ t('expiration') }}</TableHead><TableHead>{{ t('lastUsed') }}</TableHead><TableHead>{{ t('status') }}</TableHead><TableHead /></TableRow></TableHeader>
        <TableBody>
          <TableRow v-for="token in tokens" :key="token.id">
            <TableCell>
              <div class="font-bold">
                {{ token.name }}
              </div>
              <div class="text-xs text-muted-foreground">
                {{ token.owner }}
              </div>
            </TableCell>
            <TableCell class="mono">
              {{ token.tokenId }}
            </TableCell>
            <TableCell>
              <Badge
                v-for="scope in token.scopes"
                :key="scope"
                variant="secondary"
                class="mr-1"
              >
                {{ scopeLabel(scope) }}
              </Badge>
            </TableCell>
            <TableCell>{{ formatDateTime(token.expiresAt, t("never")) }}</TableCell>
            <TableCell>{{ formatDateTime(token.lastUsedAt, t("never")) }}</TableCell>
            <TableCell>
              <Badge :variant="token.enabled ? 'success' : 'secondary'">
                {{ t(token.enabled ? 'active' : 'revoked') }}
              </Badge>
            </TableCell>
            <TableCell class="text-right">
              <Tooltip v-if="token.enabled">
                <TooltipTrigger as-child>
                  <Button
                    variant="ghost"
                    size="icon"
                    class="text-destructive hover:text-destructive"
                    :aria-label="t('revoke')"
                    @click="confirmRevoke(token)"
                  >
                    <Ban />
                  </Button>
                </TooltipTrigger><TooltipContent>{{ t('revoke') }}</TooltipContent>
              </Tooltip>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Card>
    <Dialog v-model:open="dialog">
      <DialogContent size="lg" scrollable>
        <DialogHeader>
          <DialogTitle>
            {{
              t(secret ? "copyTitle" : "createTitle")
            }}
          </DialogTitle>
        </DialogHeader><div data-slot="dialog-body" class="dialog-scroll-body">
          <template v-if="secret">
            <Alert class="mb-4 border-warning/40 text-warning">
              <TriangleAlert /><AlertDescription>{{ t('secretWarning') }}</AlertDescription>
            </Alert><Textarea :model-value="secret" readonly class="mono" rows="3" :aria-label="t('copyTitle')" />
          </template><template v-else>
            <FieldGroup>
              <Field>
                <FieldLabel for="token-name">
                  {{ t('tokenName') }}
                </FieldLabel><Input id="token-name" v-model="form.name" />
              </Field><Field>
                <FieldLabel>{{ t('access') }}</FieldLabel><RadioGroup v-model="form.scopeMode" class="grid gap-3">
                  <div class="flex items-center gap-2">
                    <RadioGroupItem id="scope-all" value="all" /><Label for="scope-all">{{ t('readAll') }}</Label>
                  </div><div class="flex items-center gap-2">
                    <RadioGroupItem id="scope-selected" value="selected" /><Label for="scope-selected">{{ t('readSelected') }}</Label>
                  </div>
                </RadioGroup>
              </Field><Field v-if="form.scopeMode === 'selected'">
                <FieldLabel>{{ t('repositories') }}</FieldLabel><div class="grid gap-3 rounded-md border p-3">
                  <div v-for="repository in repositories" :key="repository.id" class="flex items-center gap-2">
                    <Checkbox :id="`repository-${repository.id}`" :model-value="form.repositoryIds.includes(repository.id)" @update:model-value="toggleRepository(repository.id, $event === true)" /><Label :for="`repository-${repository.id}`">{{ repository.name }}</Label>
                  </div>
                </div>
              </Field><Field>
                <FieldLabel for="token-expiration">
                  {{ t('expiration') }}
                </FieldLabel><Select v-model="form.expiration">
                  <SelectTrigger id="token-expiration">
                    <SelectValue />
                  </SelectTrigger><SelectContent>
                    <SelectItem v-for="option in expirationOptions" :key="option.value" :value="option.value">
                      {{ option.title }}
                    </SelectItem>
                  </SelectContent>
                </Select>
              </Field><Field v-if="form.expiration === 'custom'">
                <FieldLabel for="token-expiration-custom">
                  {{ t('expirationDate') }}
                </FieldLabel><Input id="token-expiration-custom" v-model="form.customExpiration" type="datetime-local" /><FieldDescription>{{ t('expirationHint') }}</FieldDescription>
              </Field>
            </FieldGroup>
          </template>
        </div><DialogFooter>
          <Button variant="outline" @click="dialog = false">
            {{
              t("close")
            }}
          </Button><Button
            v-if="secret"
            @click="copy"
          >
            <Copy />{{ t("copy") }}
          </Button><Button
            v-else
            :disabled="
              !form.name
                || (form.scopeMode === 'selected' && !form.repositoryIds.length)
                || (form.expiration === 'custom' && !form.customExpiration)
            "
            @click="create"
          >
            {{ t("create") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    <ConfirmDialog
      v-if="revokeTarget"
      :model-value="true"
      :title="t('revokeTitle')"
      :message="t('revokeConfirm', { name: revokeTarget.name })"
      :confirm-text="t('revoke')"
      :cancel-text="t('cancel')"
      :loading="revoking"
      @update:model-value="revokeTarget = undefined"
      @confirm="revoke"
    />
  </div>
</template>

<script setup lang="ts">
import { Alert, AlertAction, AlertDescription, Badge, Button, Card, Checkbox, Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle, Field, FieldDescription, FieldGroup, FieldLabel, Input, Label, RadioGroup, RadioGroupItem, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, Textarea, Tooltip, TooltipContent, TooltipTrigger } from '@aditify/ui';
import { Ban, CircleAlert, Copy, KeyRound, TriangleAlert, X } from '@lucide/vue';
import { computed, onMounted, reactive, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { graphql, mutationError } from '@/api/graphql';
import ConfirmDialog from '@/components/ConfirmDialog.vue';
import { formatDateTime } from '@/utils/dateTime';
import { tokenExpiration } from '@/utils/tokenExpiration';

const { t } = useI18n({ useScope: 'local' });

interface Token {
  id: string;
  name: string;
  tokenId: string;
  owner: string;
  scopes: string[];
  createdAt: string;
  expiresAt?: string;
  lastUsedAt?: string;
  enabled: boolean;
}
interface Repo {
  id: string;
  name: string;
}

const tokens = ref<Token[]>([]);
const repositories = ref<Repo[]>([]);
const dialog = ref(false);
const revokeTarget = ref<Token>();
const revoking = ref(false);
const secret = ref('');
const error = ref('');
const form = reactive({
  name: '',
  scopeMode: 'all',
  repositoryIds: [] as string[],
  expiration: '90',
  customExpiration: '',
});
const expirationOptions = computed(() => [
  { title: t('days', { count: 30 }), value: '30' },
  { title: t('days', { count: 90 }), value: '90' },
  { title: t('days', { count: 180 }), value: '180' },
  { title: t('days', { count: 365 }), value: '365' },
  { title: t('never'), value: 'never' },
  { title: t('custom'), value: 'custom' },
]);

async function load() {
  try {
    const data = await graphql<{
      accessTokens: { nodes: Token[] };
      repositories: { nodes: Repo[] };
    }>(`
      query {
        accessTokens(first: 100) {
          nodes {
            id
            name
            tokenId
            owner
            scopes
            createdAt
            expiresAt
            lastUsedAt
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

    tokens.value = data.accessTokens.nodes;
    repositories.value = data.repositories.nodes;
  }
  catch (e) {
    error.value = (e as Error).message;
  }
}
function openCreate() {
  Object.assign(form, {
    name: '',
    scopeMode: 'all',
    repositoryIds: [],
    expiration: '90',
    customExpiration: '',
  });
  secret.value = '';
  dialog.value = true;
}
function expiresAt() {
  return tokenExpiration(form.expiration, form.customExpiration);
}
function toggleRepository(id: string, selected: boolean) {
  form.repositoryIds = selected ? [...form.repositoryIds, id] : form.repositoryIds.filter(repositoryId => repositoryId !== id);
}
async function create() {
  try {
    const scopes
      = form.scopeMode === 'all'
        ? ['repository:read']
        : form.repositoryIds.map(id => `repository:${id}:read`);
    const data = await graphql<{
      createAccessToken: {
        accessToken?: { secret: string };
        errors: Array<{ code: string; message: string }>;
      };
    }>(
      `
        mutation ($name: String!, $scopes: [String!]!, $expiresAt: DateTime) {
          createAccessToken(
            name: $name
            scopes: $scopes
            expiresAt: $expiresAt
          ) {
            accessToken {
              secret
            }
            errors {
              code
              message
            }
          }
        }
      `,
      { name: form.name, scopes, expiresAt: expiresAt() },
    );

    mutationError(data.createAccessToken.errors);
    secret.value = data.createAccessToken.accessToken?.secret || '';
    await load();
  }
  catch (e) {
    error.value = (e as Error).message;
  }
}
function confirmRevoke(token: Token) {
  revokeTarget.value = token;
}
async function revoke() {
  if (!revokeTarget.value)
    return;

  revoking.value = true;

  try {
    const data = await graphql<{
      revokeAccessToken: { errors: Array<{ code: string; message: string }> };
    }>(
      `
        mutation ($id: UUID!) {
          revokeAccessToken(id: $id) {
            errors {
              code
              message
            }
          }
        }
      `,
      { id: revokeTarget.value.id },
    );

    mutationError(data.revokeAccessToken.errors);
    revokeTarget.value = undefined;
    await load();
  }
  catch (e) {
    error.value = (e as Error).message;
  }
  finally {
    revoking.value = false;
  }
}
async function copy() {
  await navigator.clipboard.writeText(secret.value);
}
function scopeLabel(scope: string) {
  if (scope === 'repository:read')
    return t('allRepositories');

  const id = scope.split(':')[1];

  return repositories.value.find(x => x.id === id)?.name || scope;
}
onMounted(load);
</script>

<i18n lang="json">
{
  "en": {
    "eyebrow": "Protocol authentication",
    "title": "Access tokens",
    "lead": "Create repository-scoped credentials for NuGet and npm clients. Secret material is displayed exactly once.",
    "create": "Create token",
    "name": "Name",
    "identifier": "Identifier",
    "access": "Repository access",
    "expiration": "Expiration",
    "lastUsed": "Last used",
    "status": "Status",
    "never": "Never",
    "active": "Active",
    "revoked": "Revoked",
    "revoke": "Revoke",
    "revokeTitle": "Revoke access token",
    "revokeConfirm": "Revoke token “{name}”? This cannot be undone.",
    "allRepositories": "All repositories",
    "copyTitle": "Copy token now",
    "createTitle": "Create access token",
    "secretWarning": "This secret will not be shown again after this dialog closes.",
    "tokenName": "Token name",
    "readAll": "Read all current and future repositories",
    "readSelected": "Read selected repositories",
    "repositories": "Repositories",
    "expirationDate": "Expiration date and time",
    "expirationHint": "Uses your local time and is stored as UTC.",
    "close": "Close",
    "cancel": "Cancel",
    "copy": "Copy",
    "days": "{count} days",
    "custom": "Custom date and time"
  },
  "sv": {
    "eyebrow": "Protokollautentisering",
    "title": "Åtkomsttoken",
    "lead": "Skapa lagringsplatsspecifika autentiseringsuppgifter för NuGet- och npm-klienter. Hemligheten visas endast en gång.",
    "create": "Skapa token",
    "name": "Namn",
    "identifier": "Identifierare",
    "access": "Åtkomst till lagringsplatser",
    "expiration": "Förfallodatum",
    "lastUsed": "Senast använd",
    "status": "Status",
    "never": "Aldrig",
    "active": "Aktiv",
    "revoked": "Återkallad",
    "revoke": "Återkalla",
    "revokeTitle": "Återkalla åtkomsttoken",
    "revokeConfirm": "Återkalla token “{name}”? Detta kan inte ångras.",
    "allRepositories": "Alla lagringsplatser",
    "copyTitle": "Kopiera token nu",
    "createTitle": "Skapa åtkomsttoken",
    "secretWarning": "Hemligheten visas inte igen efter att dialogrutan stängts.",
    "tokenName": "Tokennamn",
    "readAll": "Läs alla nuvarande och framtida lagringsplatser",
    "readSelected": "Läs valda lagringsplatser",
    "repositories": "Lagringsplatser",
    "expirationDate": "Förfallodatum och tid",
    "expirationHint": "Använder din lokala tid och lagras som UTC.",
    "close": "Stäng",
    "cancel": "Avbryt",
    "copy": "Kopiera",
    "days": "{count} dagar",
    "custom": "Anpassat datum och tid"
  }
}
</i18n>
