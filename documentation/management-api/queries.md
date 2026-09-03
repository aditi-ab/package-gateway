# Queries

All queries require at least `Reader`, except access-token metadata which requires `Administrator`.

| Field | Purpose | Important arguments |
| --- | --- | --- |
| `repositories` | Bounded repository connection | `search`, `packageType`, `enabled`, `sortBy`, `direction`, `first`, `after` |
| `repository` | Repository by UUID | `id` |
| `packages` | Packages inside one repository | `repositoryId`, `search`, sorting, paging |
| `package` | Package identity by UUID | `id` |
| `packageVersion` | Full decision summary | `id` |
| `packageVersions` | Cross-repository version connection | `repositoryId`, `packageType`, `status`, `packageName`, sorting, paging |
| `quarantinedPackages` | Quarantine work queue | `repositoryId`, paging |
| `securityFindings` | Findings across or within a version | `packageVersionId`, `minimumSeverity`, paging |
| `scanHistory` | All scans for a version | `packageVersionId`, paging |
| `policyRuleResults` | Persisted individual rule outcomes | `packageVersionId`, paging |
| `approvalHistory` | Decisions, expiry, and affected rule IDs | `packageVersionId`, paging |
| `policies` | Global or assigned policies | `repositoryId`, paging |
| `upstreams` | Priority-ordered repository origins, format, status, and health | `repositoryId`, optional `packageType` |
| `upstreamPackages` | Search enabled upstreams without acquiring package bytes | `repositoryId`, `packageType`, `search`, `first` |
| `upstreamPackageVersions` | List versions of a package from its selected upstream | `repositoryId`, `upstreamId`, `packageType`, `packageName` |
| `auditEvents` | Immutable operation history | `entityType`, `entityId`, paging |
| `accessTokens` | Token metadata only, never secrets | paging |
| `scanners` | Scanner and scheduled-check status | none |
| `systemStatus` | Database, migration, scanner, and OSV status | none |

`packageVersion.package` resolves the owning package identity for UI-oriented lists, and `decisionExplanation` is suitable for operator display. Detailed investigations should also request scan, finding, policy-rule, approval, and audit histories.

Repository results expose `packageTypes`. Upstreams expose their authoritative `packageType`. Policy results expose `packageTypes` to indicate whether the policy evaluates NuGet, npm, or both.

`upstreamPackages` requires at least two search characters, returns at most 50 results, and deduplicates package identities according to upstream priority. Results identify the selected upstream and its latest listed version. Use `upstreamPackageVersions` to populate an exact-version selector for a result. Searching and listing versions do not download or scan an artifact.
