# Security policy

Do not report vulnerabilities through a public issue tracker. Use GitHub private vulnerability reporting for this repository.

Operators must rotate `Gateway:TokenPepper` only through a coordinated access-token rotation, because changing it invalidates every existing package-client token. Secrets, bearer tokens, Basic credentials, and connection strings must be supplied through the deployment secret store and must never be committed or logged.

Run the latest supported .NET 10 servicing release and rebuild the image regularly. Treat scanner failures, missing vulnerability data beyond the configured cache age, database failures, and integrity mismatches as fail-closed conditions.
