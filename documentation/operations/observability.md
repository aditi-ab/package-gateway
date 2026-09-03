# Observability

The service exports OpenTelemetry traces and metrics through OTLP. Configure the standard `OTEL_EXPORTER_OTLP_ENDPOINT`, headers, protocol, and resource attributes supported by the .NET OpenTelemetry SDK.

Instrumentation covers gateway requests, upstream HTTP, acquisition outcomes, metadata cache outcomes, scan duration, policy outcomes, database operations, and scheduled work. Correlate a client request with the package state transition and audit identifier; never add authorization headers, access-token material, connection strings, or private manifest bodies as span attributes.

Readiness reports database reachability and migration currency. GraphQL `systemStatus` adds scanner and vulnerability-provider details. `scanners` exposes each scanner's last start, completion, health, and error information.
