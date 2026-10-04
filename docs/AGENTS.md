# Documentation

## Purpose and entry points
- Owns durable architecture, feature requirements, ADRs, implementation/qualification status and design references.
- Global map: `Architecture.md`; feature specifications: `Features/<SliceName>.md`; decisions: `ADR/`; implementation and qualification records: `implementation/`; product design references: `design/`.

## Ownership and boundaries
- Every non-trivial feature spec must use stable `REQ-*` and measurable `AC-*`, map each requirement/criterion to tests or an explicit evidence exception, identify applicable ADRs and use a Mermaid flow diagram. Architecture and ADR documents also require renderable Mermaid diagrams.
- Canonical slice names must match across technical roots and `docs/Features/`. Architecture migration debt must state owner, target paths, verification and removal date; never declare a deviation compliant.
- Keep one canonical source for each important fact and link to it. Status docs and README must distinguish source/configuration, CI qualification, published provider evidence and live behavior. Do not invent test, benchmark, coverage, complexity, durability or readiness results.
- Documentation is not authority to weaken root policy, split repository ownership, add unapproved dependencies or change runtime contracts.

## Commands and evidence
- Static governance inventory check: `node scripts/Features/RepositoryGovernance/verify.mjs` (run by the lead integration owner as static validation; it is not a runtime test).
- Delivered-source qualification uses Linux GitHub Actions in `.github/workflows/ci.yml`, with exact run/SHA/job/artifact evidence. Owner correction 2026-10-03 permits local development builds and tests; every KeyLoad test caller must use `dotnet run --project src/KeyLoad.AppHost --no-build --no-restore --configuration Release -- --KeyLoadTests:Suite=<suite>`, with AppHost-owned resources and shutdown. Unit/process runners use that entry without pretending to need RF3; RF3 tests use its discovered Docker topology and real SDK/official MCP clients. Label local results as development evidence; neither they nor a doc review replace product qualification.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Preserve existing mandatory policies and conflict records. Do not edit root/local governance outside the explicitly owned file scope; policy rollback requires explicit rule-specific owner direction.

## Read-first and canonical slice ownership
- Read the [root policy](../AGENTS.md), [architecture map](Architecture.md), [RepositoryGovernance feature](Features/RepositoryGovernance.md), and [ADR-032](ADR/ADR-032-mcaf-governance.md) first.
- This documentation module owns durable records for all canonical slices: `RepositoryGovernance`, `BenchmarkComparisons`, `DocumentStorage`, `EventStreams`, `Messaging`, `GraphTraversal`, `TimeSeries`, `Search`, `QueryExecution`, `Authorization`, `ChangeFeeds`, `StorageRecovery`, `ClusterReplication`, `ClusterRouting`, `ClientApi`, and `BackupRestore`.
- Feature docs use `Features/<SliceName>.md`; architecture, implementation and ADR records remain under their existing global documentation roots.
