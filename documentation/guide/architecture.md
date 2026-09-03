# How package delivery works

Secure Package Gateway evaluates and retains the exact package bytes that it later serves to clients. This makes an approval reproducible and prevents a later upstream response from silently replacing approved content.

## Acquisition sequence

1. Normalize the ecosystem-specific package identity and exact version.
2. Check for a previously acquired package version. Approved requests use the retained package bytes directly.
3. Select upstreams matching the requested package format, then resolve them sequentially by ascending priority. NuGet and npm priorities are independent. The first matching upstream containing the exact version is pinned permanently.
4. Treat concurrent requests for the same version as one acquisition.
5. Download through bounded temporary storage, hashing as bytes arrive. Reject unsafe destinations and over-limit content.
6. Atomically retain the artifact in content-addressed blob storage, inspect it without executing content, query vulnerability data, and evaluate every assigned policy.
7. Record the scan, findings, rule results, final state, and audit event.
8. Return the retained bytes only if the recorded state is `Approved`.

The guarded lifecycle is `Unknown → Pending → Scanning → Approved | ManualReview | Quarantined | Blocked`.

## Origin integrity

An approved artifact is never fetched from its upstream during delivery. Scheduled integrity checks may compare the pinned origin with the retained artifact. Changed bytes or conflicting integrity metadata create an immutable hard finding, block the version, retain existing bytes, and append a security audit event. The gateway never falls through to another origin after pinning.

## Storage

Package bytes are retained below `BlobStorage__Path` by SHA-256 digest. The database retains package identity, origin, digest, size, decisions, scan results, and audit history. The default container stores blobs below `/data/blobs`, on the same durable volume as the default SQLite database. Existing database-backed blobs from an earlier release are moved to content-addressed files by a bounded background migration after upgrade.

A repository can retain NuGet and npm packages with the same normalized name because format is part of the stored identity. Identical package bytes share one content-addressed file. Repository deletion disables access but retains artifacts and audit history. An administrator can explicitly remove one exact package version so a later request acquires and evaluates it from scratch. The removal event remains in the immutable audit history.

The same acquisition sequence is used when a client requests a missing package and when a repository administrator proactively adds a version from upstream search in the administration UI.
