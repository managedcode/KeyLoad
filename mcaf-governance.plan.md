# MCAF governance plan

Status: implementation snapshot `3559225a5f918160e46e32c9a812c3f71790e382` is on `main`; exact-SHA CI run [36988949282](https://github.com/managedcode/KeyLoad/actions/runs/36988949282) failed on governance because this ignored plan was absent from the checkout and Windows normalized `AGENTS.md` line endings. The plan is now updated and is explicitly unignored as a required governance source artifact for the next delivery commit; `.gitattributes` pins root and local policy files to LF. These repairs have passed local governance, but require a new exact-SHA three-OS run. Inputs: [brainstorm](mcaf-governance.brainstorm.md), [acceptance](mcaf-governance.acceptance.md), [feature](docs/Features/RepositoryGovernance.md), [ADR-032](docs/ADR/ADR-032-mcaf-governance.md).

## Ordered work and completion criteria

- [x] Inventory and read current root policy, upstream template, tutorial, .NET configuration and all existing local governance (none existed).
- [x] Establish GitHub baseline: [36926803549](https://github.com/managedcode/KeyLoad/actions/runs/36926803549), commit 9c570f8c33a7a9667507a8e1c0ca68860de3be45, successful complete CI. No new local tests run. This baseline predates the newer mandatory implementation migrations.
- [x] Create stable REQ/AC, task ownership, acceptance and ADR contracts before coding workers start.
- [x] Save exact current root prefix preservation record and skill inventory; merge customized template requirements without deleting any existing byte.
- [x] Create architecture diagrams and full slice-to-surface map before delegated implementation.
- [x] TASK-MCAF-LOCAL-002: local policies were reviewed and preserved across the current 25-project/four-module inventory; no policy was weakened or omitted.
- [x] TASK-MCAF-CHECK-003: validator and scripts policy complete; positive validation and altered-prefix/missing-policy/unsafe-path rejection evidence reviewed.
- [x] Join TASK-MCAF-REVIEW-001, both coding tasks and independent TASK-MCAF-REVIEW-005; every diff, evidence packet and terminal state inspected.
- [x] Integrated local static validation passed for 25 projects/four modules; whitespace and local links were reviewed; architecture/governance diagrams rendered with the existing cached Mermaid CLI 11.12.0; the repository MCAF skill inventory remains empty. GitHub cross-platform validation is not yet green.
- [x] Record AC-MCAF-001 through AC-MCAF-007 local configuration outcomes and exact conflict register in docs/implementation/mcaf-installation.json.

Delivery constraint: do not publish a governance-only snapshot that records concurrent analyzer projects while omitting their source. New GitHub qualification must run on the complete committed snapshot. Local configuration, rendered documentation and source review do not claim a new CI run or production qualification. The owner-selected .gitignore excludes task scaffolding; its explicit exception now includes this validator-required governance plan in the qualified source snapshot.

## Baseline failures and implementation gaps

The recorded 9c570f8 baseline passed, but it predates mandatory product migrations. Exact candidate `3559225a5f918160e46e32c9a812c3f71790e382` built on Linux, macOS and Windows before governance; run 36988949282 failed on four analyzer tests, two comparison tests, 20 RF3 integration tests, and governance on all three matrix OSes. Governance symptoms are a missing tracked `mcaf-governance.plan.md` and Windows CRLF conversion of the preserved root-policy prefix; both source/packaging repairs are prepared, with local governance pass only. Comparison fixes for the Timescale resource-name collision and overbroad digest assertion are prepared. RF3 began with a byte-array reference assertion, a retained-snapshot `Validation`, and a leader-loss read with an unclassified safe internal-request rejection; the latter left a shared node stopped and caused most later connection/MCP failures. Test diagnostics now preserve the error code and both destructive scenarios attempt bounded container recovery. No exact-SHA rerun has qualified these repairs.

## Task graph and methodology

Read-only highest-capability review runs in parallel with lead-owned contracts. After contracts and root policy exist, least expensive capable workers write disjoint local governance and validator paths. Integration waits for all required complete results. Workers stop on ambiguity and return complete/blocked/failed/cancelled with changed paths, requirement IDs, commands, outcomes and limitations.

Validation order: governance validator (project coverage/preservation), complete diff review (no weakened policy), architecture/link/Mermaid source review (coherent navigation), GitHub baseline inspection (evidence source). No newly installed skill is required or permitted. Runtime build/analyze/test/format/complexity/coverage gates remain required for later production changes and are not invented or skipped to call this bootstrap production qualification.
