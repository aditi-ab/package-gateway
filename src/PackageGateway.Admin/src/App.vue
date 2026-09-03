<template>
  <TooltipProvider>
    <ConfigProvider :scroll-body="false">
      <div class="app-shell">
        <template v-if="signedIn">
          <header class="app-header h-28 border-b">
            <div class="flex h-full w-full flex-col">
              <div class="flex items-center px-5 md:px-8" style="height: 64px">
                <div class="app-product-icon">
                  <img :src="productIconUrl" class="product-mark-image" alt="">
                </div>
                <div class="ml-3">
                  <div class="font-bold">
                    Package Gateway
                  </div>
                  <div class="text-xs text-muted-foreground">
                    {{ t('securityConsole') }}
                  </div>
                </div>
                <div class="header-actions ml-auto">
                  <Button variant="ghost" as-child class="hidden sm:flex">
                    <a :href="docsUrl" target="_blank"><BookOpen />{{ t('documentation') }}</a>
                  </Button>
                  <DropdownMenu :modal="false">
                    <DropdownMenuTrigger as-child>
                      <Button variant="secondary" class="language-button" :aria-label="t('language.label')">
                        <Languages />{{ language.toUpperCase() }}
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      <DropdownMenuRadioGroup :model-value="languagePreference" @update:model-value="selectLanguage">
                        <DropdownMenuRadioItem v-for="option in languageOptions" :key="option.value" :value="option.value">
                          {{ option.title }}
                        </DropdownMenuRadioItem>
                      </DropdownMenuRadioGroup>
                    </DropdownMenuContent>
                  </DropdownMenu>
                  <DropdownMenu :modal="false">
                    <DropdownMenuTrigger as-child>
                      <span class="inline-flex">
                        <Tooltip><TooltipTrigger as-child>
                          <Button variant="outline" size="icon" :aria-label="t('theme.label')">
                            <SunMoon />
                          </Button>
                        </TooltipTrigger><TooltipContent>{{ t('theme.label') }}</TooltipContent></Tooltip>
                      </span>
                    </DropdownMenuTrigger><DropdownMenuContent align="end">
                      <DropdownMenuRadioGroup :model-value="themePreference" @update:model-value="selectTheme">
                        <DropdownMenuRadioItem v-for="option in themeOptions" :key="option.value" :value="option.value">
                          <component :is="option.icon" />{{ option.title }}
                        </DropdownMenuRadioItem>
                      </DropdownMenuRadioGroup>
                    </DropdownMenuContent>
                  </DropdownMenu>
                  <DropdownMenu :modal="false">
                    <DropdownMenuTrigger as-child>
                      <Button variant="ghost">
                        <UserCircle />{{ displayName }}
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      <DropdownMenuItem v-if="localEnabled" @select="openPasswordDialog">
                        <KeyRound />{{ t('changePassword') }}
                      </DropdownMenuItem>
                      <DropdownMenuItem @select="signOut">
                        <LogOut />{{ t('signOut') }}
                      </DropdownMenuItem>
                    </DropdownMenuContent>
                  </DropdownMenu>
                </div>
              </div>
              <nav class="global-nav flex items-center px-4 md:px-7" :aria-label="t('nav.label')">
                <NavigationMenu :viewport="false" class="global-navigation-menu">
                  <NavigationMenuList>
                    <NavigationMenuItem v-for="item in primary" :key="item.path">
                      <NavigationMenuLink as-child :active="isNavigationActive(item.path)" :class="navigationMenuTriggerStyle()">
                        <RouterLink :to="item.path" :aria-current="isNavigationActive(item.path) ? 'page' : undefined">
                          {{ t(item.label) }}
                        </RouterLink>
                      </NavigationMenuLink>
                    </NavigationMenuItem>
                    <NavigationMenuItem v-for="group in groups" :key="group.label">
                      <NavigationMenuTrigger :class="{ 'bg-muted text-foreground': group.items.some(item => isNavigationActive(item.path)) }">
                        {{ t(group.label) }}
                      </NavigationMenuTrigger>
                      <NavigationMenuContent class="min-w-56">
                        <ul class="grid gap-1">
                          <li v-for="item in group.items" :key="item.path">
                            <NavigationMenuLink as-child :active="isNavigationActive(item.path)">
                              <RouterLink :to="item.path" :aria-current="isNavigationActive(item.path) ? 'page' : undefined">
                                <component :is="item.icon" />{{ t(item.label) }}
                              </RouterLink>
                            </NavigationMenuLink>
                          </li>
                        </ul>
                      </NavigationMenuContent>
                    </NavigationMenuItem>
                  </NavigationMenuList>
                </NavigationMenu>
              </nav>
            </div>
          </header>
          <main class="grow">
            <router-view v-slot="{ Component }">
              <transition name="route-view" mode="out-in">
                <div :key="route.fullPath" class="route-view">
                  <component :is="Component" />
                </div>
              </transition>
            </router-view>
          </main>
        </template>
        <template v-else>
          <header class="app-header flex h-16 items-center border-b px-5 md:px-8">
            <div class="app-product-icon">
              <img :src="productIconUrl" class="product-mark-image" alt="">
            </div>
            <div class="ml-3">
              <div class="font-bold">
                Package Gateway
              </div><div class="text-xs text-muted-foreground">
                {{ t('securityConsole') }}
              </div>
            </div>
            <div class="header-actions ml-auto">
              <DropdownMenu :modal="false">
                <DropdownMenuTrigger as-child>
                  <Button variant="secondary" class="language-button" :aria-label="t('language.label')">
                    <Languages />{{ language.toUpperCase() }}
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuRadioGroup :model-value="languagePreference" @update:model-value="selectLanguage">
                    <DropdownMenuRadioItem v-for="option in languageOptions" :key="option.value" :value="option.value">
                      {{ option.title }}
                    </DropdownMenuRadioItem>
                  </DropdownMenuRadioGroup>
                </DropdownMenuContent>
              </DropdownMenu>
              <DropdownMenu :modal="false">
                <DropdownMenuTrigger as-child>
                  <Button variant="outline" size="icon" :aria-label="t('theme.label')">
                    <SunMoon />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuRadioGroup :model-value="themePreference" @update:model-value="selectTheme">
                    <DropdownMenuRadioItem v-for="option in themeOptions" :key="option.value" :value="option.value">
                      <component :is="option.icon" />{{ option.title }}
                    </DropdownMenuRadioItem>
                  </DropdownMenuRadioGroup>
                </DropdownMenuContent>
              </DropdownMenu>
            </div>
          </header>
          <main class="grow">
            <div class="identity-sign-in-page">
              <Card class="identity-sign-in-card p-8 sm:p-10">
                <div class="eyebrow">
                  {{ t('auth.eyebrow') }}
                </div>
                <template v-if="isLocal">
                  <h1 class="page-title mt-2">
                    {{ t(bootstrapRequired ? 'auth.createTitle' : 'auth.signInTitle') }}
                  </h1>
                  <p class="page-lead mb-7">
                    {{ t(bootstrapRequired ? 'auth.createLead' : 'auth.localLead') }}
                  </p>
                  <Alert v-if="authenticationError" variant="destructive" class="mb-5">
                    <CircleAlert /><AlertDescription>{{ authenticationError }}</AlertDescription>
                  </Alert>
                  <form class="grid gap-4" @submit.prevent="submitLocal">
                    <Field v-if="!bootstrapRequired && passwordProviders.length > 1">
                      <FieldLabel for="login-provider">
                        {{ t('auth.provider') }}
                      </FieldLabel><Select v-model="selectedProvider">
                        <SelectTrigger id="login-provider">
                          <SelectValue />
                        </SelectTrigger><SelectContent>
                          <SelectItem v-for="provider in passwordProviders" :key="provider.id" :value="provider.id">
                            {{ provider.displayName }}
                          </SelectItem>
                        </SelectContent>
                      </Select>
                    </Field>
                    <Field>
                      <FieldLabel for="login-username">
                        {{ t('auth.username') }}
                      </FieldLabel><Input id="login-username" v-model="username" autocomplete="username" :disabled="authenticationBusy" />
                    </Field>
                    <Field>
                      <FieldLabel for="login-password">
                        {{ t('auth.password') }}
                      </FieldLabel><Input id="login-password" v-model="password" type="password" :autocomplete="bootstrapRequired ? 'new-password' : 'current-password'" :disabled="authenticationBusy" /><FieldDescription v-if="bootstrapRequired">
                        {{ t('auth.passwordHint') }}
                      </FieldDescription>
                    </Field>
                    <Field v-if="bootstrapRequired">
                      <FieldLabel for="login-confirm-password">
                        {{ t('auth.confirmPassword') }}
                      </FieldLabel><Input id="login-confirm-password" v-model="confirmPassword" type="password" autocomplete="new-password" :disabled="authenticationBusy" />
                    </Field>
                    <Button class="mt-1 w-full" size="lg" type="submit" :disabled="authenticationBusy">
                      <Spinner v-if="authenticationBusy" />{{ t(bootstrapRequired ? 'auth.createButton' : 'auth.signIn') }}
                    </Button>
                  </form>
                  <template v-if="entraEnabled && !bootstrapRequired">
                    <div class="my-5 flex items-center gap-3">
                      <Separator class="grow" /><span class="text-xs text-muted-foreground">{{ t('auth.or') }}</span><Separator class="grow" />
                    </div><Button class="w-full" variant="outline" @click="signIn">
                      {{ t('auth.microsoft') }}
                    </Button>
                  </template>
                  <template v-for="provider in oidcProviders" :key="provider.id">
                    <div class="my-5 flex items-center gap-3">
                      <Separator class="grow" /><span class="text-xs text-muted-foreground">{{ t('auth.or') }}</span><Separator class="grow" />
                    </div><Button class="w-full" variant="outline" @click="submitExternal(provider.id)">
                      {{ t('auth.continueWith', { provider: provider.displayName }) }}
                    </Button>
                  </template>
                </template>
                <template v-else>
                  <h1 class="page-title mt-2">
                    {{ t('auth.signInTitle') }}
                  </h1><p class="page-lead mb-7">
                    {{ t('auth.entraLead') }}
                  </p><Button size="lg" @click="signIn">
                    {{ t('auth.microsoft') }}
                  </Button>
                </template>
              </Card>
            </div>
          </main>
        </template>
        <footer class="app-footer border-t">
          <div class="content-shell text-center">
            <a href="https://github.com/aditi-ab/package-gateway" target="_blank" rel="noopener noreferrer" class="text-primary">{{ t('footer.sourceCode') }}</a>
          </div>
        </footer>
        <Dialog :open="passwordDialog" @update:open="updatePasswordDialog">
          <DialogContent size="md" scrollable :show-close-button="!mustChangePassword" @escape-key-down="mustChangePassword && $event.preventDefault()" @pointer-down-outside="mustChangePassword && $event.preventDefault()">
            <DialogHeader><DialogTitle>{{ t('changePassword') }}</DialogTitle></DialogHeader>
            <div data-slot="dialog-body" class="dialog-scroll-body grid gap-4">
              <Alert v-if="mustChangePassword" class="border-amber-500/40 text-amber-700 dark:text-amber-300">
                <TriangleAlert /><AlertDescription>{{ t('passwordRequired') }}</AlertDescription>
              </Alert>
              <Alert v-if="passwordChangeError" variant="destructive">
                <CircleAlert /><AlertDescription>{{ passwordChangeError }}</AlertDescription>
              </Alert>
              <FieldGroup>
                <Field>
                  <FieldLabel for="current-password">
                    {{ t('auth.password') }}
                  </FieldLabel><Input id="current-password" v-model="currentLocalPassword" type="password" autocomplete="current-password" :disabled="passwordChangeBusy" />
                </Field><Field>
                  <FieldLabel for="new-password">
                    {{ t('newPassword') }}
                  </FieldLabel><Input id="new-password" v-model="newLocalPassword" type="password" autocomplete="new-password" :disabled="passwordChangeBusy" /><FieldDescription>{{ t('auth.passwordHint') }}</FieldDescription>
                </Field>
              </FieldGroup>
            </div>
            <DialogFooter>
              <Button v-if="mustChangePassword" variant="outline" :disabled="passwordChangeBusy" @click="signOutFromPasswordDialog">
                {{ t('signOut') }}
              </Button><Button v-else variant="outline" :disabled="passwordChangeBusy" @click="updatePasswordDialog(false)">
                {{ t('back') }}
              </Button><Button :disabled="passwordChangeBusy" @click="submitPasswordChange">
                <Spinner v-if="passwordChangeBusy" />{{ t('changePassword') }}
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </div>
    </ConfigProvider>
  </TooltipProvider>
</template>

<script setup lang="ts">
import type { AcceptableValue } from 'reka-ui';
import type { Component } from 'vue';
import type { ThemePreference } from '@/composables/themePreference';
import type { AdminLanguagePreference } from '@/plugins/i18n';
import { Alert, AlertDescription, Button, Card, ConfigProvider, Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle, DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuRadioGroup, DropdownMenuRadioItem, DropdownMenuTrigger, Field, FieldDescription, FieldGroup, FieldLabel, Input, NavigationMenu, NavigationMenuContent, NavigationMenuItem, NavigationMenuLink, NavigationMenuList, NavigationMenuTrigger, navigationMenuTriggerStyle, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Separator, Spinner, Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@aditify/ui';
import { BookOpen, CircleAlert, ClipboardClock, KeyRound, Languages, LogOut, Monitor, Moon, Sun, SunMoon, TriangleAlert, UserCircle, Users } from '@lucide/vue';
import { computed, onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink, useRoute } from 'vue-router';
import { loadConfig } from '@/api/graphql';
import { bootstrapLocal, bootstrapRequired, changeLocalPassword, displayName, entraEnabled, identityProviders, localEnabled, loginLocal, mustChangePassword, signedIn, signIn, signInExternal, signOut } from '@/auth/auth';
import { usePasswordChangeDialog } from '@/auth/passwordChangeDialog';
import { initializeTheme, setThemePreference, themePreference } from '@/composables/themePreference';
import { language, languagePreference, setLanguagePreference } from '@/plugins/i18n';

const { t } = useI18n({ useScope: 'local' });
const route = useRoute();
const docsUrl = ref('/docs/');
const username = ref('');
const selectedProvider = ref('local');
const passwordProviders = computed(() => [{ id: 'local', displayName: t('auth.local') }, ...identityProviders.value.filter(provider => provider.type === 'ldap')]);
const oidcProviders = computed(() => identityProviders.value.filter(provider => provider.type === 'oidc'));
const { currentLocalPassword, newLocalPassword, openPasswordDialog, passwordChangeBusy, passwordChangeError, passwordDialog, resetPasswordDialog, updatePasswordDialog } = usePasswordChangeDialog(mustChangePassword);
const password = ref('');
const confirmPassword = ref('');
const authenticationError = ref('');
const authenticationBusy = ref(false);
const productIconUrl = `${import.meta.env.BASE_URL}secure-package-gateway.svg`;
const isLocal = localEnabled;
const primary = [{ label: 'nav.overview', path: '/' }, { label: 'nav.repositories', path: '/repositories' }, { label: 'nav.packages', path: '/packages' }, { label: 'nav.review', path: '/review' }, { label: 'nav.policies', path: '/policies' }];
const groups: Array<{ label: string; items: Array<{ label: string; path: string; icon: Component }> }> = [{ label: 'nav.access', items: [{ label: 'nav.users', path: '/users', icon: Users }, { label: 'nav.tokens', path: '/tokens', icon: KeyRound }] }, { label: 'nav.system', items: [{ label: 'nav.audit', path: '/audit', icon: ClipboardClock }] }];
const themeOptions = computed<Array<{ title: string; value: ThemePreference; icon: Component }>>(() => [{ title: t('theme.system'), value: 'system', icon: Monitor }, { title: t('theme.light'), value: 'light', icon: Sun }, { title: t('theme.dark'), value: 'dark', icon: Moon }]);
const languageOptions = computed<Array<{ title: string; value: AdminLanguagePreference }>>(() => [{ title: t('language.system', { locale: language.value.toUpperCase() }), value: 'system' }, { title: t('language.english'), value: 'en' }, { title: t('language.swedish'), value: 'sv' }]);

function isNavigationActive(path: string) { return path === '/' ? route.path === path : route.path === path || route.path.startsWith(`${path}/`); }
function selectLanguage(value: AcceptableValue) {
  if (value === 'system' || value === 'en' || value === 'sv')
    setLanguagePreference(value);
}
function selectTheme(value: AcceptableValue) {
  if (value === 'system' || value === 'light' || value === 'dark')
    setThemePreference(value);
}
onMounted(initializeTheme);

async function signOutFromPasswordDialog() {
  passwordChangeBusy.value = true;

  try { await signOut(); passwordDialog.value = false; resetPasswordDialog(); }
  finally { passwordChangeBusy.value = false; }
}
async function submitPasswordChange() {
  passwordChangeError.value = ''; passwordChangeBusy.value = true;

  try { await changeLocalPassword(currentLocalPassword.value, newLocalPassword.value); passwordDialog.value = false; resetPasswordDialog(); }
  catch (error) { passwordChangeError.value = error instanceof Error ? error.message : String(error); }
  finally { passwordChangeBusy.value = false; }
}
void loadConfig().then((x) => { docsUrl.value = x.documentationUrl; });
async function submitLocal() {
  authenticationError.value = '';

  if (bootstrapRequired.value && password.value !== confirmPassword.value) { authenticationError.value = t('auth.passwordMismatch'); return; }

  authenticationBusy.value = true;

  try {
    if (bootstrapRequired.value)
      await bootstrapLocal(username.value, password.value); else await loginLocal(username.value, password.value, selectedProvider.value === 'local' ? undefined : selectedProvider.value);

    password.value = ''; confirmPassword.value = '';
  }
  catch (error) { authenticationError.value = (error as Error).message; }
  finally { authenticationBusy.value = false; }
}
async function submitExternal(providerId: string) {
  authenticationBusy.value = true; authenticationError.value = '';

  try { await signInExternal(providerId); }
  catch (error) { authenticationError.value = error instanceof Error ? error.message : String(error); authenticationBusy.value = false; }
}
</script>

<style scoped>
.route-view {
  transition:
    opacity 200ms cubic-bezier(0, 0, 0.2, 1),
    transform 200ms cubic-bezier(0, 0, 0.2, 1);
}
.route-view-enter-from {
  opacity: 0;
  transform: translateY(0.375rem);
}
.route-view-leave-to {
  opacity: 0;
  transform: translateY(-0.25rem);
}
@media (prefers-reduced-motion: reduce) {
  .route-view {
    transition: none;
  }
  .route-view-enter-from,
  .route-view-leave-to {
    opacity: 1;
    transform: none;
  }
}
</style>

<i18n lang="json">
{
  "en": { "securityConsole": "Security console", "documentation": "Documentation", "signOut": "Sign out", "changePassword": "Change password", "newPassword": "New password", "passwordRequired": "Change the temporary password before continuing.", "language": { "label": "Language", "system": "System ({locale})", "english": "English", "swedish": "Swedish" }, "theme": { "label": "Theme preference", "system": "System", "light": "Light", "dark": "Dark" }, "nav": { "label": "Primary navigation", "overview": "Overview", "repositories": "Repositories", "packages": "Packages", "review": "Review queue", "policies": "Policies", "tokens": "Access tokens", "users": "Users", "access": "Access", "system": "System", "audit": "Audit history" }, "back": "Back", "footer": { "sourceCode": "View Package Gateway on GitHub" }, "auth": { "eyebrow": "Protected management plane", "createTitle": "Create the first administrator", "signInTitle": "Sign in to administer the gateway", "createLead": "Choose the local administrator credentials for this gateway. This one-time setup is disabled after the account is created.", "localLead": "Use a local or configured identity provider account.", "entraLead": "Use your organization’s Microsoft Entra account. Management roles are evaluated by the API for every operation.", "username": "Username", "password": "Password", "provider": "Provider", "local": "Local", "continueWith": "Continue with {provider}", "confirmPassword": "Confirm password", "passwordHint": "Use 12 to 128 characters.", "passwordMismatch": "Passwords do not match.", "createButton": "Create administrator", "signIn": "Sign in", "or": "or", "microsoft": "Continue with Microsoft" } },
  "sv": { "securityConsole": "Säkerhetskonsol", "documentation": "Dokumentation", "signOut": "Logga ut", "changePassword": "Byt lösenord", "newPassword": "Nytt lösenord", "passwordRequired": "Byt det tillfälliga lösenordet innan du fortsätter.", "language": { "label": "Språk", "system": "System ({locale})", "english": "Engelska", "swedish": "Svenska" }, "theme": { "label": "Temainställning", "system": "System", "light": "Ljust", "dark": "Mörkt" }, "nav": { "label": "Huvudnavigering", "overview": "Översikt", "repositories": "Lagringsplatser", "packages": "Paket", "review": "Granskningskö", "policies": "Policyer", "tokens": "Åtkomsttoken", "users": "Användare", "access": "Åtkomst", "system": "System", "audit": "Revisionshistorik" }, "back": "Tillbaka", "footer": { "sourceCode": "Visa Package Gateway på GitHub" }, "auth": { "eyebrow": "Skyddad administrationsyta", "createTitle": "Skapa den första administratören", "signInTitle": "Logga in för att administrera gatewayen", "createLead": "Välj autentiseringsuppgifter för den lokala administratören. Denna engångskonfiguration inaktiveras när kontot har skapats.", "localLead": "Använd ett lokalt konto eller en konfigurerad identitetsleverantör.", "entraLead": "Använd organisationens Microsoft Entra-konto. Administrationsroller kontrolleras av API:t för varje åtgärd.", "username": "Användarnamn", "password": "Lösenord", "provider": "Leverantör", "local": "Lokalt", "continueWith": "Fortsätt med {provider}", "confirmPassword": "Bekräfta lösenord", "passwordHint": "Använd 12 till 128 tecken.", "passwordMismatch": "Lösenorden matchar inte.", "createButton": "Skapa administratör", "signIn": "Logga in", "or": "eller", "microsoft": "Fortsätt med Microsoft" } }
}
</i18n>
