# Mutations

Repository administration mutations require `RepositoryAdmin`: `createRepository`, `updateRepository`, `deleteRepository`, `createUpstream`, `updateUpstream`, `deleteUpstream`, `createPolicy`, `updatePolicy`, `deletePolicy`, `assignPolicy`, `unassignPolicy`, `clearPackageCache`, and `addPackageVersion`.

Repositories are format-neutral. `createRepository` accepts the earlier nullable `packageType` input for compatibility, but new clients should omit it. `createUpstream` and `updateUpstream` require the upstream `packageType`. Policy create and update inputs accept `packageTypes` for NuGet, npm, or both. `clearPackageCache` accepts an optional `packageType` when a package name could exist in both formats.

`addPackageVersion(repositoryId, packageType, packageName, version)` resolves the exact version using enabled upstream priority, then runs the same bounded download, integrity checks, scanning, vulnerability lookup, and policy evaluation as a first client request. The payload returns the stored package version and its current state. A long-running evaluation can initially return `Scanning`; inspect or refresh that version for its final decision.

Security decisions require `SecurityReviewer`:

- `approvePackageVersion(id, reason, expiresAt)` rejects active hard guards.
- `waivePackageVersion(id, affectedRuleResultIds, reason, expiresAt)` requires a future expiry and only non-hard deny results.
- `blockPackageVersion(id, reason)` withholds bytes.
- `quarantinePackageVersion(id, reason)` retains isolated bytes and withholds delivery.
- `requirePackageVersionReview(id, reason)` moves an approved version to manual review and immediately withholds delivery.
- `rescanPackageVersion(id)` queues or returns the current state; it never claims the scan already completed.

Token management requires `Administrator`: `createAccessToken` returns `{ token, secret }` once, while `revokeAccessToken` disables the stored verifier. Use `repository:read` for all current and future repositories, or `repository:{repositoryId}:read` for individual repositories. Expiration is an optional future UTC timestamp.

Package removal also requires `Administrator`. `removePackageVersion(id, reason)` removes one exact version, its cached artifact, scans, findings, rule results, approval records, and matching vulnerability cache entries. Its `BooleanPayload` exposes `success` and `errors`. The immutable audit event is retained. If it was the package's final version, the package identity is also removed. A later protocol request downloads and evaluates the version as a new observation.

```graphql
mutation Approve($id: UUID!, $reason: String!, $expires: DateTime) {
  approvePackageVersion(id: $id, reason: $reason, expiresAt: $expires) {
    packageVersion { id status decisionExplanation }
    errors { code message }
  }
}
```

Deletion of repositories, upstreams, and policies is soft. Cache clearing can remove only unapproved transient/failed/blocked/quarantined content. Exact package-version removal is a separate, explicit administrator operation and retains its audit event.
