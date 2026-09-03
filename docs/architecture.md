# Architecture

The solution keeps the package protocols at the edge and sends all artifact downloads through one application contract:

```text
NuGet/npm client
  -> protocol metadata adapter (upstream URLs rewritten)
  -> package acquisition coordinator (single-flight key)
  -> bounded download and immutable hash
  -> archive, signature, heuristic, and OSV inspection
  -> assigned policy evaluation
  -> database blob + decision + findings + audit
  -> response only when the stored version is Approved
```

`PackageGateway.Domain` contains provider- and framework-independent state. `PackageGateway.Application` owns use-case contracts and DTOs. Storage, upstream HTTP, scanners, and protocols implement those contracts. `PackageGateway.Api` is the composition root and hosts GraphQL, package endpoints, authorization, health checks, rate limiting, background work, and OpenTelemetry.

An exact repository/package/version is pinned to the first enabled upstream, ordered by ascending priority, that contains it. Approved bytes are always served from the database. A new origin or changed hash blocks the version instead of replacing it.

The MVP deliberately uses an in-process operation table and background hosted services. Database uniqueness remains authoritative, and the locking/job interfaces are replacement points for a distributed deployment.
