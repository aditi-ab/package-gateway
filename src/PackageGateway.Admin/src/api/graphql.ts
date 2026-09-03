export interface GatewayConfig {
  authenticationMode: 'local' | 'entra' | 'localandentra';
  authority?: string;
  clientId?: string;
  scopes: string[];
  graphqlEndpoint: string;
  documentationUrl: string;
}

export interface GraphQLError { message: string; extensions?: { code?: string } }

let configuration: GatewayConfig | undefined;
let tokenFactory: (() => Promise<string>) | undefined;
let antiforgeryToken: string | undefined;
let authorizationFailureHandler: (() => Promise<void> | void) | undefined;

export async function loadConfig(): Promise<GatewayConfig> {
  if (configuration)
    return configuration;

  const response = await fetch('/admin/config.json', { credentials: 'same-origin' });

  if (!response.ok)
    throw new Error(`Unable to load gateway configuration (${response.status}).`);

  configuration = await response.json() as GatewayConfig;
  return configuration;
}

export function configureAccessToken(factory?: () => Promise<string>) { tokenFactory = factory; }
export function configureAntiforgeryToken(token: string) { antiforgeryToken = token; }
export function configureAuthorizationFailureHandler(handler: () => Promise<void> | void) { authorizationFailureHandler = handler; }

export async function graphql<T>(query: string, variables: Record<string, unknown> = {}): Promise<T> {
  const config = await loadConfig();
  const token = await tokenFactory?.();

  if (config.authenticationMode === 'entra' && !token)
    throw new Error('Sign in is required.');

  const headers: Record<string, string> = { 'content-type': 'application/json' };

  if (token)
    headers.authorization = `Bearer ${token}`;

  if (antiforgeryToken)
    headers['X-CSRF-TOKEN'] = antiforgeryToken;

  const response = await fetch(config.graphqlEndpoint, {
    method: 'POST',
    headers,
    body: JSON.stringify({ query, variables }),
  });

  if (response.status === 401 || response.status === 403)
    await authorizationFailureHandler?.();

  const payload = await response.json().catch(() => ({})) as { data?: T; errors?: GraphQLError[] };

  if (!response.ok || payload.errors?.length)
    throw new Error(payload.errors?.map(x => x.message).join('\n') || `GraphQL request failed (${response.status}).`);

  if (!payload.data)
    throw new Error('GraphQL returned no data.');

  return payload.data;
}

export function mutationError(errors: Array<{ code: string; message: string }>): void {
  if (errors.length)
    throw new Error(errors.map(x => `${x.code}: ${x.message}`).join('\n'));
}
