import type { AccountInfo } from '@azure/msal-browser';
import { PublicClientApplication } from '@azure/msal-browser';
import { computed, ref } from 'vue';
import { configureAccessToken, configureAntiforgeryToken, configureAuthorizationFailureHandler, loadConfig } from '@/api/graphql';

const account = ref<AccountInfo | null>(null);
const localUsername = ref('');
let client: PublicClientApplication | undefined;
let scopes: string[] = [];
let authorizationFailureRecovery: Promise<void> | undefined;

export const authenticationMode = ref<'local' | 'entra' | 'localandentra'>('local');
export const bootstrapRequired = ref(false);
export const mustChangePassword = ref(false);
export interface ExternalProvider { id: string; displayName: string; type: 'ldap' | 'oidc' | 'entra' }
export const identityProviders = ref<ExternalProvider[]>([]);
export const signedIn = computed(() => localUsername.value !== '' || account.value !== null);
export const displayName = computed(() => localUsername.value || account.value?.name || account.value?.username || '');
export const localEnabled = computed(() => authenticationMode.value !== 'entra');
export const entraEnabled = computed(() => authenticationMode.value !== 'local');

export async function initializeAuth(): Promise<void> {
  const config = await loadConfig();

  authenticationMode.value = config.authenticationMode;

  if (config.authenticationMode !== 'entra')
    await refreshLocalState();

  if (config.authenticationMode === 'local')
    return;

  scopes = config.scopes;

  if (!config.clientId || !config.authority)
    throw new Error('Entra authentication is not configured.');

  client = new PublicClientApplication({ auth: { clientId: config.clientId, authority: config.authority, redirectUri: `${location.origin}/admin/`, postLogoutRedirectUri: `${location.origin}/admin/` }, cache: { cacheLocation: 'sessionStorage' } });
  await client.initialize();

  const result = await client.handleRedirectPromise();

  account.value = result?.account || client.getAllAccounts()[0] || null;

  if (account.value)
    client.setActiveAccount(account.value);

  configureAccessToken(acquireToken);
}

export async function signIn(): Promise<void> {
  if (!client)
    return;

  const result = await client.loginPopup({ scopes });

  account.value = result.account;
  client.setActiveAccount(result.account);
}

export async function signOut(): Promise<void> {
  if (localUsername.value) { await postLocal('/admin/auth/logout'); await refreshLocalState(); return; }

  if (!client)
    return;

  await client.logoutPopup({ account: account.value || undefined });
  account.value = null;
}

export async function bootstrapLocal(username: string, password: string): Promise<void> {
  await postLocal('/admin/auth/bootstrap', { username, password });
  await refreshLocalState();
}

export async function loginLocal(username: string, password: string, providerId?: string): Promise<void> {
  await postLocal('/admin/auth/login', { username, password, ...(providerId ? { providerId } : {}) });
  await refreshLocalState();
}

async function refreshLocalState(): Promise<void> {
  const response = await fetch('/admin/auth/status', { credentials: 'same-origin' });

  if (!response.ok)
    throw new Error(`Unable to load authentication state (${response.status}).`);

  const state = await response.json() as { bootstrapRequired: boolean; authenticated: boolean; username?: string; mustChangePassword?: boolean; providers?: ExternalProvider[]; antiforgeryToken: string };

  bootstrapRequired.value = state.bootstrapRequired;
  identityProviders.value = state.providers ?? [];
  localUsername.value = state.authenticated ? state.username || '' : '';
  mustChangePassword.value = !!state.mustChangePassword;
  configureAntiforgeryToken(state.antiforgeryToken);
}
export async function changeLocalPassword(currentPassword: string, newPassword: string): Promise<void> { await postLocal('/admin/auth/change-password', { currentPassword, newPassword }); await refreshLocalState(); }
export async function signInExternal(providerId: string): Promise<void> {
  const state = await fetch('/admin/auth/status', { credentials: 'same-origin' }).then(response => response.json()) as { antiforgeryToken: string };
  const response = await fetch(`/admin/auth/external/${encodeURIComponent(providerId)}/start`, { method: 'POST', credentials: 'same-origin', headers: { 'content-type': 'application/json', 'X-CSRF-TOKEN': state.antiforgeryToken }, body: JSON.stringify({ returnUrl: '/admin/' }) });

  if (!response.ok)
    throw new Error('External authentication failed.');

  location.assign((await response.json() as { url: string }).url);
}

async function recoverFromAuthorizationFailure(): Promise<void> {
  if (authorizationFailureRecovery)
    return authorizationFailureRecovery;

  authorizationFailureRecovery = (async () => {
    if (localUsername.value) {
      try {
        await postLocal('/admin/auth/logout');
      }
      catch {
        // The local state is cleared below even if the invalid session cannot be logged out server-side.
      }
    }

    localUsername.value = '';
    mustChangePassword.value = false;
    account.value = null;
    configureAccessToken();
  })().finally(() => {
    authorizationFailureRecovery = undefined;
  });

  return authorizationFailureRecovery;
}

async function postLocal(path: string, body?: Record<string, string>): Promise<void> {
  const stateResponse = await fetch('/admin/auth/status', { credentials: 'same-origin' });

  if (!stateResponse.ok)
    throw new Error(`Unable to prepare authentication request (${stateResponse.status}).`);

  const state = await stateResponse.json() as { antiforgeryToken: string };
  const response = await fetch(path, { method: 'POST', credentials: 'same-origin', headers: { 'content-type': 'application/json', 'X-CSRF-TOKEN': state.antiforgeryToken }, body: body ? JSON.stringify(body) : undefined });

  if (response.ok)
    return;

  const payload = await response.json().catch(() => ({})) as { message?: string };

  throw new Error(payload.message || `Authentication request failed (${response.status}).`);
}

async function acquireToken(): Promise<string> {
  if (!client || !account.value)
    throw new Error('Sign in is required.');

  try { return (await client.acquireTokenSilent({ account: account.value, scopes })).accessToken; }
  catch { return (await client.acquireTokenPopup({ account: account.value, scopes })).accessToken; }
}

configureAuthorizationFailureHandler(recoverFromAuthorizationFailure);
