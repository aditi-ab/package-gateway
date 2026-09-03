# Secure Package Gateway

Secure Package Gateway is a read-only NuGet V3 and npm registry proxy built on .NET 10. Every new artifact is downloaded into quarantine, checked, evaluated against repository policies, and persisted before it can be returned. Only an exact blob whose effective state is `Approved` is deliverable.

## Development

Prerequisites are the .NET 10.0.301 SDK, Node.js 24, Yarn 1.22.22, and Docker for container/SQL Server validation.

Clone the repository together with its Aditify dependency:

```sh
git clone --recurse-submodules https://github.com/aditi-ab/package-gateway.git
```

For an existing clone, run `git submodule update --init --recursive` before installing dependencies. Set `Gateway__TokenPepper` through an environment variable, .NET user secrets, or another local secret store before starting the application.

```powershell
dotnet tool restore
dotnet restore SecurePackageGateway.slnx
dotnet build SecurePackageGateway.slnx --no-restore
dotnet test SecurePackageGateway.slnx --no-build
yarn install --frozen-lockfile
yarn build
```

Create or update the development SQLite schema explicitly:

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project src/PackageGateway.Api -- database migrate
dotnet run --project src/PackageGateway.Api
```

Normal application startup never applies migrations. `/health/ready` reports unhealthy until the configured database is reachable and current; `/health/live` only confirms that the process is running.

## Container deployment

Build locally from this directory, or pull `ghcr.io/aditi-ab/secure-package-gateway`. A manually dispatched GitHub Actions workflow publishes `master`, commit SHA, `latest`, and an automatically incremented `1.0.<run-number>` tag. Ordinary pushes and pull requests build and validate the image without publishing it. The same version is stamped into the .NET assembly and OCI image metadata.

Local authentication is the default. Supply a random token-pepper value of at least 32 characters, then run the migration command against the same volume before starting the service:

```bash
export PACKAGE_GATEWAY_TOKEN_PEPPER="$(openssl rand -base64 48)"
docker compose build
docker compose run --rm gateway database migrate
docker compose up -d
```

The image runs as the non-root `app` user with a read-only root filesystem. The `/data` volume contains content-addressed package blobs, local-session keys, and the default SQLite database; temporary scan space is a bounded `tmpfs`. SQL Server is recommended for production database storage: set `Database__Provider=SqlServer` and provide the connection string through the deployment secret store while retaining the durable blob volume.

## Management and clients

The administration UI is at `/admin/`, the VitePress documentation is at `/docs/`, and the authenticated management API is at `/graphql`. On the first visit in local mode, create the local administrator username and password. The bootstrap form is disabled permanently after that account is stored.

After creating the first local administrator, configure Entra authentication from **Access > Users** in the administration UI. Deployment settings can provide the initial authority, audience, client ID, and management scope. Entra application roles must use these exact values:

- `Reader`
- `SecurityReviewer`
- `RepositoryAdmin`
- `Administrator`

Create a repository and upstream through GraphQL, then create an access token with either `repository:read` or `repository:<repository-id>:read`. The secret is returned only by the create mutation and cannot be recovered later.

NuGet source URL:

```text
https://gateway.example/nuget/<repository-slug>/v3/index.json
```

Use any username and the gateway token as the Basic-auth password, or a bearer-capable NuGet credential provider.

npm configuration:

```ini
registry=https://gateway.example/npm/<repository-slug>/
//gateway.example/npm/<repository-slug>/:_authToken=pgw_<id>_<secret>
always-auth=true
```

On a first request, the gateway waits up to 90 seconds. If scanning continues, it returns `503` with `Retry-After: 15`; blocked/manual-review/quarantined artifacts return `403`.

## Security boundaries

- Approved blobs and audit events are immutable through normal APIs. Repository deletion is a soft delete.
- Invalid integrity or signatures, unsafe archives, and confirmed hard indicators cannot be manually waived.
- Archive contents are inspected but never executed. The built-in npm checks are heuristics and are not a replacement for a dedicated malware engine.
- Confirmed SHA-256 malware indicators can be supplied through `Security__BlockedSha256Digests`; matches are non-waivable hard blocks.
- Untrusted upstreams may only return public HTTPS artifact URLs. Marking an upstream `Trusted` permits internal/private endpoints and should be limited to operator-controlled registries.
- The MVP is single-instance. SQLite is only supported for a single process on durable local storage.

See the [VitePress documentation](documentation/index.md), [architecture note](docs/architecture.md), and [SECURITY.md](SECURITY.md).

## License

Secure Package Gateway is licensed under the [Apache License 2.0](LICENSE).
