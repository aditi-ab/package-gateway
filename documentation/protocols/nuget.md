# NuGet clients

Repository `engineering` exposes source URL:

```text
https://gateway.example/nuget/engineering/v3/index.json
```

Configure credentials without committing them:

```bash
dotnet nuget add source https://gateway.example/nuget/engineering/v3/index.json \
  --name secure-gateway --username gateway --password "$PACKAGE_GATEWAY_TOKEN" \
  --store-password-in-clear-text
dotnet restore
dotnet package search Example --source secure-gateway
```

The service index advertises only gateway-local flat-container, registration, and search endpoints. Registration content URLs are rewritten. Exact downloads and `HEAD` pass through the approval gate. `503` plus `Retry-After: 15` means first-time evaluation is still running; retry. `403` means the stored decision withholds the artifact.

Standard gzip, deflate, and Brotli compression on upstream JSON metadata responses is supported.

The first upstream containing an exact normalized version becomes its origin. Approved restores read only the immutable local blob, including transitive dependencies.

The same `engineering` repository may also contain npm upstreams. NuGet requests use only enabled NuGet upstreams, ordered by their NuGet priority. Adding npm upstreams does not change this source URL or NuGet resolution order.
