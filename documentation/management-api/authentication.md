# Authentication and authorization

Set `Authentication__Mode` to `Local`, `Entra`, or `LocalAndEntra`. `LocalAndEntra` accepts either a local session or an Entra bearer token for each request.

## Local authentication

`Local` is the default and does not require an external identity provider. The first-use form creates the initial administrator. Administrators can then create additional local users and assign `Reader`, `SecurityReviewer`, `RepositoryAdmin`, and `Administrator`. Temporary passwords are displayed once and must be changed at first sign-in. Security changes invalidate existing sessions, and the final enabled administrator is protected.

The browser receives an encrypted, secure, HTTP-only session cookie. Unsafe management requests require a matching antiforgery token. Configure `Authentication__DataProtectionKeysPath` on durable storage to preserve the encryption keys across restarts. The local password is stored using the ASP.NET Core password hasher and is never returned by the API.

## Microsoft Entra authentication

Register an Entra API application whose audience matches `Authentication__Audience`, expose the configured management scope, and configure a SPA redirect URI of `https://gateway.example/admin/`. Put application roles with the exact values `Reader`, `SecurityReviewer`, `RepositoryAdmin`, and `Administrator` in the `roles` claim. Higher roles inherit the lower operational capabilities.

Configure the connection from **Access > Users** in the administration console. `Authentication__Mode`, `Authentication__Authority`, `Authentication__Audience`, `Authentication__ClientId`, and `Authentication__ManagementScope` provide initial deployment defaults until an administrator saves a connection. The UI uses the authorization-code flow with PKCE. The API validates issuer, audience, lifetime, and role claims. Local sign-in remains available as a recovery path.

## LDAP and OpenID Connect authentication

Administrators can add multiple LDAP and OIDC providers from **Access > Users**. LDAP uses search and bind for password sign-in. OIDC uses the authorization-code flow, validates the provider's ID token, and returns to `/admin/auth/external/{provider-id}/callback`. Register that callback URI at the provider.

Each provider can assign default roles and map LDAP group values or OIDC role claims to product roles. Unknown users are rejected unless automatic provisioning is enabled. LDAP bind passwords and OIDC client secrets are encrypted with the application's persistent data-protection keys and are never returned by the management API.

## Package client tokens

Protocol access tokens are independent of Entra roles. They use `repository:read` for all enabled repositories or `repository:{repository-guid}:read` for one repository. npm sends the secret as a bearer token. NuGet may send it as bearer or as the password in Basic authentication; the username is ignored.

`Gateway__TokenPepper` protects protocol access tokens. Configure it as a separate secret of at least 32 characters and load it from your deployment secret store. Do not include it in the image or deployment manifest.
