import { beforeEach, describe, expect, it, vi } from 'vitest';

function jsonResponse(payload: unknown, status = 200) {
  return { ok: status >= 200 && status < 300, status, json: async () => payload } as Response;
}

describe('graphQL authorization failures', () => {
  beforeEach(() => {
    vi.resetModules();
    vi.restoreAllMocks();
  });

  it.each([401, 403])('clears authentication after an HTTP %s response', async (status) => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(jsonResponse({ authenticationMode: 'local', scopes: [], graphqlEndpoint: '/graphql', documentationUrl: '/docs/' }))
      .mockResolvedValueOnce(jsonResponse({}, status));
    const authorizationFailure = vi.fn();

    vi.stubGlobal('fetch', fetch);

    const api = await import('./graphql');

    api.configureAuthorizationFailureHandler(authorizationFailure);

    await expect(api.graphql('query { repositories { nodes { id } } }'))
      .rejects
      .toThrow(`GraphQL request failed (${status}).`);
    expect(authorizationFailure).toHaveBeenCalledOnce();
  });

  it('does not clear authentication when a password change is required', async () => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(jsonResponse({ authenticationMode: 'local', scopes: [], graphqlEndpoint: '/graphql', documentationUrl: '/docs/' }))
      .mockResolvedValueOnce(jsonResponse({}, 428));
    const authorizationFailure = vi.fn();

    vi.stubGlobal('fetch', fetch);

    const api = await import('./graphql');

    api.configureAuthorizationFailureHandler(authorizationFailure);

    await expect(api.graphql('query { repositories { nodes { id } } }'))
      .rejects
      .toThrow('GraphQL request failed (428).');
    expect(authorizationFailure).not.toHaveBeenCalled();
  });
});
