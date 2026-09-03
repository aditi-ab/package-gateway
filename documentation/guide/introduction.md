# Introduction

Secure Package Gateway sits between developer package clients and upstream NuGet/npm registries. It proxies discoverable metadata, rewrites all content URLs back to itself, and gates artifact delivery on a persisted decision about the exact stored bytes.

The central invariant is:

> An artifact is returned only when its exact locally stored bytes have an effective `Approved` decision.

Metadata visibility does not imply approval. A client may discover a version that later returns `503` while evaluation runs or `403` after a denying decision. The first request waits up to 90 seconds by default; work continues in the background and a timed-out caller receives `Retry-After: 15`.

## Included

- NuGet V3 service index, flat container, registrations, search, package download, `GET`, and `HEAD`.
- npm metadata, version documents, dist-tags, scoped names, tarballs, integrity, `GET`, and `HEAD`.
- SQLite or SQL Server metadata and artifact storage.
- Archive, integrity, signature, malware-indicator, vulnerability, and policy evaluation.
- Local administrator or Microsoft Entra authentication, a management API, and an administration console.
- Auditing, health checks, OpenTelemetry, automated recovery, migrations, and hardened Linux and Windows Server Core images.

This release is read-only and supports NuGet and npm. Package publishing and other package ecosystems are not supported.
