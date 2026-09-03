import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('@azure/msal-browser', () => ({ PublicClientApplication: vi.fn() }));

function jsonResponse(payload: unknown, ok = true, status = 200) {
  return { ok, status, json: async () => payload } as Response;
}

describe('local authentication', () => {
  beforeEach(() => {
    vi.resetModules();
    vi.restoreAllMocks();
  });

  it('shows first-use bootstrap when no administrator exists', async () => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(jsonResponse({ authenticationMode: 'local', scopes: [], graphqlEndpoint: '/graphql', documentationUrl: '/docs/' }))
      .mockResolvedValueOnce(jsonResponse({ bootstrapRequired: true, authenticated: false, antiforgeryToken: 'csrf-1' }));

    vi.stubGlobal('fetch', fetch);

    const authentication = await import('./auth');

    await authentication.initializeAuth();

    expect(authentication.authenticationMode.value).toBe('local');
    expect(authentication.bootstrapRequired.value).toBe(true);
    expect(authentication.signedIn.value).toBe(false);
  });

  it('bootstraps the administrator and refreshes the authenticated session', async () => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(jsonResponse({ authenticationMode: 'local', scopes: [], graphqlEndpoint: '/graphql', documentationUrl: '/docs/' }))
      .mockResolvedValueOnce(jsonResponse({ bootstrapRequired: true, authenticated: false, antiforgeryToken: 'csrf-1' }))
      .mockResolvedValueOnce(jsonResponse({ bootstrapRequired: true, authenticated: false, antiforgeryToken: 'csrf-2' }))
      .mockResolvedValueOnce({ ok: true, status: 204, json: async () => ({}) } as Response)
      .mockResolvedValueOnce(jsonResponse({ bootstrapRequired: false, authenticated: true, username: 'gateway.admin', antiforgeryToken: 'csrf-3' }));

    vi.stubGlobal('fetch', fetch);

    const authentication = await import('./auth');

    await authentication.initializeAuth();

    await authentication.bootstrapLocal('gateway.admin', 'A long local password 42!');

    expect(authentication.signedIn.value).toBe(true);
    expect(authentication.displayName.value).toBe('gateway.admin');
    expect(fetch).toHaveBeenNthCalledWith(4, '/admin/auth/bootstrap', expect.objectContaining({
      method: 'POST',
      headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'csrf-2' }),
      body: JSON.stringify({ username: 'gateway.admin', password: 'A long local password 42!' }),
    }));
  });

  it('returns the password validation message when a password change is rejected', async () => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(jsonResponse({ antiforgeryToken: 'csrf-1' }))
      .mockResolvedValueOnce(jsonResponse({ message: 'Password must be between 12 and 128 characters.' }, false, 400));

    vi.stubGlobal('fetch', fetch);

    const authentication = await import('./auth');

    await expect(authentication.changeLocalPassword('temporary-password', 'too-short'))
      .rejects
      .toThrow('Password must be between 12 and 128 characters.');

    expect(fetch).toHaveBeenNthCalledWith(2, '/admin/auth/change-password', expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ currentPassword: 'temporary-password', newPassword: 'too-short' }),
    }));
  });
});
