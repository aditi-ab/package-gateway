# Database lifecycle

Apply migrations as a separate deployment step using the same immutable image:

```bash
docker run --rm --env-file gateway.env secure-package-gateway:1.0.0 database migrate
```

Only start or roll traffic after the command succeeds. The regular service checks connectivity and pending migrations but does not alter the schema. Back up the database and the directory configured by `BlobStorage__Path` as one recovery set. Restoring only one side can leave metadata without package bytes or unreferenced files.

The multi-format repository migration copies each existing repository format to its existing upstreams. Repository slugs, endpoint URLs, enabled states, package records, policies, approvals, and audit history are retained. Existing empty repositories retain their earlier format as a compatibility hint.

SQLite permits one application instance and requires a durable volume for its database, journal, and package blob directory. SQL Server stores metadata and operational records, while package bytes remain in the configured blob directory. Use SQL Server for production database concurrency, backups, and operational resilience. Multi-instance package acquisition and object-storage blob backends are not supported in this release.
