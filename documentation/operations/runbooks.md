# Operational runbooks

## Client receives 503

Honor `Retry-After: 15`. Inspect the version state, scan history, scanner status, and audit trail. Recovery is automatic when a scan is interrupted. Do not bypass delivery with direct database access.

## Client receives 403

Inspect `decisionExplanation`, findings, and rule results. A hard guard cannot be overridden. For a non-hard decision, record an explicit approval or a reasoned, expiring waiver tied to individual rule results.

## OSV is unavailable

The gateway uses a durable result no older than 24 hours. Without a valid cache, evaluation produces manual review and withholds bytes. Restore connectivity and queue a rescan; do not force approval simply because vulnerability data is missing.

## Origin changed

An integrity finding blocks the pinned version while preserving existing bytes. Treat it as a supply-chain incident: preserve audit/scan evidence, validate the upstream, rotate affected credentials if appropriate, and publish/use a new package version. Do not clear or replace the approved blob.

## Readiness is unhealthy

Check database reachability first, then run the one-shot migration command if pending migrations are reported. Never grant the web process schema-modification privileges solely to make startup pass.
