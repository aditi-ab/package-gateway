# Writing style

- Do not use em dashes (`—`) in literal user-facing text. Use commas, parentheses, colons, or separate sentences instead.

# Customer-facing product documentation

- Write product documentation for customers who use, administer, deploy, or integrate with Secure Package Gateway.
- Include technical details only when customers need them to install, configure, secure, operate, troubleshoot, or consume a supported capability.
- Do not include internal implementation details, source-code structure, development workflows, CI/CD mechanics, build-pipeline behavior, internal architecture notes, or contributor instructions in `documentation/`.
- Keep internal and contributor material outside the customer-facing VitePress documentation. Do not describe planned or unreleased behavior as available to customers.

# Repository instructions

## Product documentation is part of every change

Whenever product behavior, configuration, public contracts, GraphQL schema, protocol behavior, security policy, deployment, operations, or administration UI changes, update the matching VitePress pages under `documentation/` in the same change. Add a new page and navigation entry when no existing page explains the behavior.

Before completing a product change, verify both `yarn build:docs` and the relevant .NET/UI tests. Do not describe planned behavior as implemented behavior. Keep examples free of real credentials, internal hostnames, and secrets.

Architecture decision notes in `docs/` may supplement but do not replace user/operator documentation in `documentation/`.
