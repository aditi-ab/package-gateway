# Security decisions

## Hard guards

Hard guards are non-waivable and always block delivery:

- expected digest or npm integrity mismatch;
- changed bytes for a pinned version;
- malformed, traversing, link-based, or limit-violating archives;
- invalid NuGet signatures;
- a match against configured confirmed-malware SHA-256 indicators.

Manual approval is rejected while any hard guard is active. Allowlists do not skip download, hashing, scanning, or vulnerability lookup.

## Policy precedence

The strongest result wins: `Block > Quarantine > ManualReview > Warn > Allow`. The balanced repository defaults block critical/high vulnerabilities, warn on medium, permit low, apply 24-hour NuGet and 72-hour npm cooldowns, evaluate license posture, warn on unsigned NuGet content, and score npm lifecycle/heuristic signals. Each critical, high, medium, or low vulnerability contributes 100, 70, 30, or 5 points respectively to the aggregate risk score. Other findings contribute the score recorded with that finding. Scores 40–69 require review, 70–99 quarantine, and 100+ block. A policy can impose a stricter result than the score threshold.

OSV results are cached durably for 24 hours. During an outage the latest still-valid cache is usable; without one the package moves to manual review and its bytes are withheld.

## Overrides

A waiver must identify individual non-hard rule results, include a reason and future expiration, and record the actor and recalculated outcome. When a waiver expires, the affected version is evaluated again. Ordinary approvals may expire as well; rejection and quarantine remain explicit decisions.

A security reviewer can return an approved version to manual review. Delivery stops immediately, the exact stored bytes remain available for investigation, and the action and reason are written to the audit history. The reviewer can then approve, quarantine, block, or rescan the version.
