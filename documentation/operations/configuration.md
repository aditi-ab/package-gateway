# Configuration reference

Environment variables use `__` to represent nested settings.

| Setting | Meaning | Default |
| --- | --- | --- |
| `Database__Provider` | `SqlServer` or `Sqlite` | `Sqlite` |
| `Database__ConnectionString` | Provider connection | `/data/packagegateway.db` |
| `BlobStorage__Path` | durable content-addressed package blob directory | `/data/blobs` |
| `Gateway__TokenPepper` | HMAC server secret, ≥32 chars | required |
| `Authentication__Mode` | `Local`, `Entra`, or `LocalAndEntra` management authentication | `Local` |
| `Authentication__DataProtectionKeysPath` | durable local-session encryption key directory | `/data/dataprotection-keys` |
| `Authentication__Authority` | HTTPS Entra issuer authority | required in `Entra` mode |
| `Authentication__Audience` | management API audience | required in `Entra` mode |
| `Authentication__ClientId` | SPA application/client ID | required in `Entra` mode |
| `Authentication__ManagementScope` | delegated API scope | required in `Entra` mode |

These Entra values initialize the connection when no administration-managed value has been saved. After initial local setup, administrators can update and enable the live connection from **Access > Users** without restarting the service. Local authentication remains available for recovery.
| `Security__MaximumPackageBytes` | compressed/download limit | 250 MiB |
| `Security__MaximumExpandedBytes` | total expanded limit | 1 GiB |
| `Security__MaximumFileBytes` | individual entry limit | 100 MiB |
| `Security__MaximumFileCount` | archive entry limit | 10,000 |
| `Security__MaximumCompressionRatio` | entry/overall ratio guard | 200 |
| `Security__ScanTimeout` | scan deadline | 5 minutes |
| `Security__InitialRequestWait` | first client wait | 90 seconds |
| `Security__BlockedSha256Digests` | confirmed malware hashes | empty |
| `Gateway__VulnerabilityRescanInterval` | OSV rescan cadence | 12 hours |
| `Gateway__OriginIntegrityInterval` | pinned-origin recheck | 12 hours |
| `Gateway__BackgroundJobLeaseDuration` | recovery timeout for interrupted scheduled work | 30 minutes |

`BlobStorage__Path` must be writable by the application and must use durable storage. Keep it on storage that is backed up together with the database. Do not place retained blobs on the temporary scan filesystem.

Do not log or place connection strings, bearer tokens, Basic credentials, local passwords, token pepper, or sensitive private-package metadata in configuration diagnostics. Startup validation rejects invalid providers, authentication modes, weak secrets, invalid archive bounds, non-HTTPS Entra authorities, and missing Entra settings.
