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
      <div class="flex gap-2">
        <Button variant="outline" :disabled="loading" @click="load">
          <Spinner v-if="loading" /><RefreshCw v-else />{{ t('refresh') }}
        </Button><Button @click="openCreate">
          <Plus />{{ t('new') }}
        </Button>
      </div>
    </div>
    <Alert v-if="error" variant="destructive" class="mt-5">
      <CircleAlert /><AlertDescription>{{ error }}</AlertDescription><AlertAction>
        <Button variant="ghost" size="icon" :aria-label="t('cancel')" @click="error = ''">
          <X />
        </Button>
      </AlertAction>
    </Alert>
    <Card class="mt-6 py-0">
      <div class="flex flex-wrap items-center gap-4 border-b p-5">
        <strong>{{ t('count', { count: filtered.length }) }}</strong><InputGroup class="ml-auto w-full max-w-[420px]">
          <InputGroupAddon><Search /></InputGroupAddon><InputGroupInput v-model="search" :placeholder="t('filter')" :aria-label="t('filter')" />
        </InputGroup>
      </div><Table>
        <TableHeader><TableRow><TableHead>{{ t('repository') }}</TableHead><TableHead>{{ t('formats') }}</TableHead><TableHead>{{ t('upstreams') }}</TableHead><TableHead>{{ t('slug') }}</TableHead><TableHead>{{ t('status') }}</TableHead><TableHead>{{ t('updated') }}</TableHead><TableHead /></TableRow></TableHeader><TableBody>
          <TableRow v-for="repo in filtered" :key="repo.id" class="click-row" tabindex="0" @click="router.push(`/repositories/${repo.id}/packages`)" @keydown.enter="router.push(`/repositories/${repo.id}/packages`)">
            <TableCell>
              <div class="font-bold">
                {{ repo.name }}
              </div><div class="text-xs text-muted-foreground">
                {{ repo.description || t('noDescription') }}
              </div>
            </TableCell><TableCell>
              <Badge v-for="format in formats(repo)" :key="format" variant="secondary" class="mr-1">
                {{ format }}
              </Badge>
            </TableCell><TableCell>{{ repo.upstreamCount ?? 0 }}</TableCell><TableCell class="mono">
              {{ repo.slug }}
            </TableCell><TableCell><StatusChip :status="repo.enabled ? 'healthy' : 'blocked'" /></TableCell><TableCell>{{ formatDateTime(repo.updatedAt) }}</TableCell><TableCell class="text-right">
              <ChevronRight class="ml-auto size-4" />
            </TableCell>
          </TableRow>
        </TableBody>
      </Table><div v-if="!filtered.length && !loading" class="empty-state">
        <div>
          <Package class="mx-auto mb-3 size-10" /><div class="font-bold">
            {{ t('empty') }}
          </div><div class="text-xs">
            {{ t('emptyLead') }}
          </div>
        </div>
      </div>
    </Card>
    <Dialog v-model:open="dialog">
      <DialogContent size="lg" scrollable>
        <DialogHeader><DialogTitle>{{ t('createTitle') }}</DialogTitle></DialogHeader><div data-slot="dialog-body" class="dialog-scroll-body">
          <FieldGroup>
            <Field>
              <FieldLabel for="repository-name">
                {{ t('displayName') }}
              </FieldLabel><Input id="repository-name" v-model="form.name" autofocus />
            </Field><Field>
              <FieldLabel for="repository-slug">
                {{ t('slug') }}
              </FieldLabel><Input id="repository-slug" v-model="form.slug" @update:model-value="slugEdited = true" /><FieldDescription>{{ t('slugHint') }}</FieldDescription>
            </Field><Field>
              <FieldLabel for="repository-description">
                {{ t('description') }}
              </FieldLabel><Textarea id="repository-description" v-model="form.description" rows="3" />
            </Field><Alert><Info /><AlertDescription>{{ t('baseline') }}</AlertDescription></Alert>
          </FieldGroup>
        </div><DialogFooter>
          <Button variant="outline" @click="dialog = false">
            {{ t('cancel') }}
          </Button><Button :disabled="!form.name || !form.slug" @click="create">
            {{ t('create') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>

<script setup lang="ts">
import { Alert, AlertAction, AlertDescription, Badge, Button, Card, Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle, Field, FieldDescription, FieldGroup, FieldLabel, Input, InputGroup, InputGroupAddon, InputGroupInput, Spinner, Table, TableBody, TableCell, TableHead, TableHeader, TableRow, Textarea } from '@aditify/ui';
import { ChevronRight, CircleAlert, Info, Package, Plus, RefreshCw, Search, X } from '@lucide/vue';
import { computed, onMounted, reactive, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRouter } from 'vue-router';
import { graphql, mutationError } from '@/api/graphql';
import StatusChip from '@/components/StatusChip.vue';
import { formatDateTime } from '@/utils/dateTime';
import { slugify } from '@/utils/slug';

const { t } = useI18n({ useScope: 'local' });

interface Repo { id: string; name: string; slug: string; packageType?: string; packageTypes: string[]; enabled: boolean; description?: string; updatedAt: string; upstreamCount?: number }

const router = useRouter(); const repositories = ref<Repo[]>([]); const dialog = ref(false); const error = ref(''); const loading = ref(false); const search = ref(''); const slugEdited = ref(false);
const form = reactive({ name: '', slug: '', description: '' });
const filtered = computed(() => {
  const value = search.value.trim().toLowerCase();

  return value ? repositories.value.filter(x => x.name.toLowerCase().includes(value) || x.slug.includes(value)) : repositories.value;
});

watch(() => form.name, (value) => {
  if (!slugEdited.value)
    form.slug = slugify(value);
});

async function load() {
  loading.value = true; error.value = '';

  try {
    const nodes = (await graphql<{ repositories: { nodes: Repo[] } }>(`query { repositories(first:100){nodes{id name slug packageType packageTypes enabled description updatedAt}}}`)).repositories.nodes;

    await Promise.all(nodes.map(async (repo) => { repo.upstreamCount = (await graphql<{ upstreams: Array<{ id: string }> }>(`query($id:UUID!){upstreams(repositoryId:$id){id}}`, { id: repo.id })).upstreams.length; })); repositories.value = nodes;
  }
  catch (e) { error.value = (e as Error).message; }
  finally { loading.value = false; }
}
function openCreate() { Object.assign(form, { name: '', slug: '', description: '' }); slugEdited.value = false; dialog.value = true; }
async function create() {
  try {
    const data = await graphql<{ createRepository: { repository?: Repo; errors: Array<{ code: string; message: string }> } }>(`mutation($input:CreateRepositoryCommandInput!){createRepository(input:$input){repository{id name slug} errors{code message}}}`, { input: form });

    mutationError(data.createRepository.errors); dialog.value = false;

    if (data.createRepository.repository)
      await router.push(`/repositories/${data.createRepository.repository.id}/upstreams`); else await load();
  }
  catch (e) { error.value = (e as Error).message; }
}
function formats(repo: Repo) { return repo.packageTypes.length ? repo.packageTypes.map(x => x === 'NU_GET' ? 'NuGet' : 'npm') : [t('noUpstreams')]; }
onMounted(load);
</script>

<i18n lang="json">
{"en":{"eyebrow":"Package sources","title":"Repositories","lead":"Repositories group NuGet and npm upstreams behind one stable endpoint slug.","refresh":"Refresh","new":"New repository","count":"{count} repositories","filter":"Filter repositories","repository":"Repository","formats":"Formats","upstreams":"Upstreams","slug":"Endpoint slug","status":"Status","updated":"Updated","noDescription":"No description","noUpstreams":"No upstreams","empty":"No repositories found","emptyLead":"Create a repository or change the filter.","createTitle":"Create repository","displayName":"Display name","slugHint":"Derived from the display name. Edit it to choose a different stable endpoint.","description":"Description","baseline":"Balanced policies are assigned automatically. Add NuGet and npm upstreams after creation.","cancel":"Cancel","create":"Create repository"},"sv":{"eyebrow":"Paketkällor","title":"Lagringsplatser","lead":"Lagringsplatser samlar NuGet- och npm-uppströmmar bakom ett stabilt slutpunktssegment.","refresh":"Uppdatera","new":"Ny lagringsplats","count":"{count} lagringsplatser","filter":"Filtrera lagringsplatser","repository":"Lagringsplats","formats":"Format","upstreams":"Uppströmmar","slug":"Slutpunktssegment","status":"Status","updated":"Uppdaterad","noDescription":"Ingen beskrivning","noUpstreams":"Inga uppströmmar","empty":"Inga lagringsplatser hittades","emptyLead":"Skapa en lagringsplats eller ändra filtret.","createTitle":"Skapa lagringsplats","displayName":"Visningsnamn","slugHint":"Skapas från visningsnamnet. Redigera för att välja ett annat stabilt slutpunktssegment.","description":"Beskrivning","baseline":"Balanserade policyer tilldelas automatiskt. Lägg till NuGet- och npm-uppströmmar efter att lagringsplatsen skapats.","cancel":"Avbryt","create":"Skapa lagringsplats"}}
</i18n>
