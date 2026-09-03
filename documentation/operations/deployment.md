# Deployment

The container serves the customer documentation at `/docs/`. Documentation pages use clean URLs without an `.html` suffix, for example `/docs/operations/deployment`. If a reverse proxy replaces the application's Content Security Policy, allow the generated VitePress inline bootstrap scripts under `/docs/` or configure equivalent script hashes.

SQL Server is recommended for production. SQLite is supported for one gateway instance only and requires a durable mounted volume.

## Container image

The supported image is `ghcr.io/aditi-ab/secure-package-gateway`. Use an immutable `1.0.<build>` version tag for controlled deployments. The `latest` tag is available for evaluation but should not be used where repeatable rollbacks are required.

Authenticate before pulling the image when the container package is private:

```bash
echo "$GHCR_TOKEN" | docker login ghcr.io -u USERNAME --password-stdin
docker pull ghcr.io/aditi-ab/secure-package-gateway:latest
```

The container defaults to local authentication and SQLite at `/data/packagegateway.db`, content-addressed package blobs at `/data/blobs`, and authentication keys below `/data`. These container storage paths remain fixed when `ASPNETCORE_ENVIRONMENT=Development` enables direct HTTP first-use setup. Supply only the token pepper and persistent volume for this single-instance layout:

```bash
docker volume create package-gateway-data
docker run --rm \
  -e Gateway__TokenPepper="$PACKAGE_GATEWAY_TOKEN_PEPPER" \
  -v package-gateway-data:/data \
  ghcr.io/aditi-ab/secure-package-gateway:latest database migrate
docker run -d --name package-gateway --restart unless-stopped \
  --read-only --cap-drop ALL --security-opt no-new-privileges \
  --tmpfs /tmp/packagegateway:rw,noexec,nosuid,size=1g,mode=1777 \
  -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e Gateway__TokenPepper="$PACKAGE_GATEWAY_TOKEN_PEPPER" \
  -v package-gateway-data:/data \
  ghcr.io/aditi-ab/secure-package-gateway:latest
```

Set `Database__Provider` and `Database__ConnectionString` only when overriding the SQLite default, for example when connecting to SQL Server.

Terminate TLS at a trusted reverse proxy, forward `X-Forwarded-For` and `X-Forwarded-Proto`, restrict direct container access, and expose `/health/live` for liveness and `/health/ready` for readiness. Local authentication cookies require HTTPS outside Development mode. Normal startup never alters schema. Readiness is unhealthy while migrations are pending or the database is unavailable.

After the first startup, open `https://gateway.example/admin/` and create the first local administrator. Then open **Access > Users** to configure Microsoft Entra sign-in if required. Deployment-level Entra values can provide initial defaults, but the local administrator remains available as a recovery path.

The final image runs non-root with no capabilities and a read-only application filesystem. It includes the .NET SDK code-signing and timestamp certificate bundles required to validate signed NuGet packages on Linux. Do not remove `/app/trustedroots` when extending the image. Mount `/tmp/packagegateway` as a bounded `tmpfs` with sticky world-writable mode `1777`. A runtime mount is initially owned by root and replaces the directory ownership from the image, so a restrictive owner-only mode prevents the application from creating temporary files. Mount `/data` persistently for package blobs and local-session keys, and also for SQLite when it is selected.

## Windows Server Core LTSC 2025

Windows container images use the same package name with the `-windowsservercore-ltsc2025` suffix. Use an immutable tag such as `1.0.42-windowsservercore-ltsc2025` for controlled deployments. The rolling `windowsservercore-ltsc2025` tag is available for evaluation. These images are `windows/amd64`, use `mcr.microsoft.com/windows/servercore:ltsc2025`, and require a compatible Windows container host.

```powershell
docker pull ghcr.io/aditi-ab/secure-package-gateway:1.0.42-windowsservercore-ltsc2025
docker volume create package-gateway-data
docker run --rm `
  --mount type=volume,src=package-gateway-data,dst=C:\data `
  -e Gateway__TokenPepper=$env:PACKAGE_GATEWAY_TOKEN_PEPPER `
  ghcr.io/aditi-ab/secure-package-gateway:1.0.42-windowsservercore-ltsc2025 database migrate
docker run -d --name package-gateway --restart unless-stopped `
  -p 8080:8080 `
  -e ASPNETCORE_ENVIRONMENT=Development `
  -e Gateway__TokenPepper=$env:PACKAGE_GATEWAY_TOKEN_PEPPER `
  --mount type=volume,src=package-gateway-data,dst=C:\data `
  ghcr.io/aditi-ab/secure-package-gateway:1.0.42-windowsservercore-ltsc2025
```

The Windows image stores its SQLite database, package blobs, and authentication keys below `C:\data`. It runs as the restricted built-in Network Service identity and includes a self-contained .NET runtime.
