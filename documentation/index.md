---
layout: home
hero:
  name: Secure Package Gateway
  text: Approved bytes. Reproducible delivery.
  tagline: A self-hosted NuGet and npm gateway that discovers metadata freely but releases an artifact only after its exact bytes pass security evaluation.
  actions:
    - theme: brand
      text: Understand the gateway
      link: /guide/introduction
    - theme: alt
      text: Deploy it
      link: /operations/deployment
features:
  - title: Fail-closed delivery
    details: Pending, failed, quarantined, manual-review, and blocked artifacts are never streamed.
  - title: Immutable origins
    details: The first exact-version origin is pinned. Later byte changes create a hard integrity finding.
  - title: Auditable decisions
    details: Every state-changing package and administrative operation is recorded in the audit trail.
---
