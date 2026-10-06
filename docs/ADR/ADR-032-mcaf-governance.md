# ADR-032: Preserve existing policy while adopting MCAF

Status: Accepted. Date: 2026-10-01. Owner: KeyLoad lead/integrator. Requirements: REQ-MCAF-001 through REQ-MCAF-007. Acceptance: AC-MCAF-001 through AC-MCAF-007.

## Decision

Keep every existing root policy byte intact and append the customized current MCAF requirements. Add a project-local AGENTS.md to every csproj root and local policies for site, workflows and documentation. Use docs/Architecture.md as the global map and PascalCase canonical feature names under Features/<SliceName> across applicable technical roots. All solution-owned code and delivery assets stay in this repository.

No skills are installed. The owner explicitly overrode tutorial skill-install steps. No runtime restart is necessary to load nonexistent new skills. MCAF concepts are configured directly in repository governance.

```mermaid
flowchart TD
    Policy[Existing policy plus MCAF merge] --> Specification[Stable REQ and AC]
    Specification --> Contract[ADR implementation contract]
    Contract --> Local[Disjoint local governance worker]
    Contract --> Check[Disjoint validator worker]
    Local --> Review[Lead joins and reviews every diff]
    Check --> Review
    Review --> Validation[Combined static verification]
    Validation --> CI[GitHub Actions qualification]
```

## Implementation contract

1. Read all existing policy, the full current template and tutorial; save an exact pre-installation byte-count/hash baseline. Lead owns the root and shared docs.
2. Define design analysis, acceptance and execution contracts in the owning feature specification and this ADR, and review the architecture map before any delegated write task starts. The owner correction below removes separate working planning files.
3. Merge root policy. Record conflicts rather than silently selecting weaker wording. A read-only highest-capability architect reviews the decisions.
4. TASK-MCAF-LOCAL-002 owns only new local AGENTS.md in the 20 csproj roots, site, .github/workflows and docs. It must name entry points, boundaries, commands, protected risks and no-install skill policy.
5. TASK-MCAF-CHECK-003 owns only scripts/Features/RepositoryGovernance/verify.mjs and new scripts/AGENTS.md. It uses Node built-ins, consumes docs/implementation/mcaf-installation.json and scans the real repository for project/module coverage, prefix preservation, policy IDs, required documents and unexpected skills.
6. Lead waits for every required result, inspects every diff, fixes integration issues, runs the validator, reviews links and Mermaid sources, and records acceptance evidence. TASK-MCAF-REVIEW-005 supplies the highest-capability independent final review. Workers cannot commit, push or install anything.
7. Qualification and published performance data remain GitHub Actions evidence. A governance bootstrap cannot mark unrelated product work complete.

Start condition: the owning feature specification contains acceptance and ordered execution contracts, and required ADR contracts exist. Shared contracts and central docs have one integration owner. Terminal states: complete, blocked, failed or cancelled. Only a reviewed complete evidence packet can satisfy a join condition. Ambiguity, unsafe file overlap or missing credentials must be escalated immediately.

## Existing architecture refactoring debt

The current source layout predates MCAF. It is not declared compliant by this ADR. The target is mirrored Features/<SliceName>/ paths with shared composition roots and building blocks outside feature folders. Lead owner; removal date: 2026-10-15.

| Existing paths | Target / scope | Verification and refactoring owner |
|---|---|---|
| benchmarks/KeyLoad.Comparisons/*.cs and Targets/; AppHost/BenchmarkResources.cs; ComparisonTests/ComparisonTests.cs; site benchmark code | Features/BenchmarkComparisons/ in each applicable technical root; shared executable entry points remain composition roots. | BenchmarkComparisons feature owner; identical corpus, real Docker/Aspire topology, qualified GitHub JSON and site proof. |
| Core/Documents.cs, Events.cs, Messaging.cs, GraphAndSeries.cs; query and security feature files | DocumentStorage, EventStreams, Messaging, GraphTraversal, Search and Authorization slices, preserving node-local storage ownership. | Owning feature leads; mapped contracts and existing recovery/security regressions must pass before each move. |
| Existing unit/recovery/integration feature tests in flat project roots | Matching Features/<SliceName>/ test paths; fixtures and real-process hosts remain shared test infrastructure. | Owning feature leads; no skipped or weakened tests. |

New feature-owned artifacts must use the target layout. Existing runtime gaps (host-process RF3 and incomplete official MCP and blob surfaces) remain separately tracked implementation work, not exceptions to mandatory policy or results of MCAF installation. TUnit remains the required test framework; only a successful delivered-source GitHub run qualifies its current source.

### Static site refactoring join (2026-10-02)

[ADR-040](ADR-040-static-site-threejs-evidence.md) and [BenchmarkComparisons](../Features/BenchmarkComparisons.md) now own the website portion of this debt: feature HTML/modules/styles, pinned same-origin Three.js, independent TUnit `KeyLoad.SiteTests`, atomic historical-evidence loading and a validation-only Pages job. The new project's local policy was created before code. Replacement packets, static/raw-byte proof, manual browser evidence and the exact-source GitHub suite must all join before that portion is marked complete. This extension does not close the benchmark/runtime refactoring in the table or qualify new database topologies.

## Conflict register

- The incoming template permits local exceptions and obsolete-rule removal. Owner policy forbids weakening or omitting existing rules: existing policy stays mandatory; no exception can weaken it.
- The incoming template asks to install skills. Owner's latest explicit instruction says not to install skills: no skills are added or changed.
- Historical supplied AGENTS required lock files; an explicit owner correction requested removal of packages.lock.json before this bootstrap. The current on-disk mandatory root prohibits generating or committing them. This installation preserves that current file exactly and does not change package policy or central build settings.
- The design document discusses DotNext as an earlier candidate and optional per-request facades; current root forbids DotNext and requires a separate Orleans grain per request. Current stricter policy controls future implementation. The product specification is not silently rewritten.
- All tests use TUnit. Node-based static installation validation does not replace runtime tests.

## Rollout, rollback and verification

Installation adds documentation and a static validator only; it neither changes persisted data nor publishes fabricated metrics. Root policy rollback/removal requires explicit rule-specific owner direction. No preexisting source/config file is reset.

Verification: run node scripts/Features/RepositoryGovernance/verify.mjs; inspect all local files and the root diff; inspect native worker statuses and final evidence; confirm no skill changes. Baseline runtime proof is GitHub Actions 36926803549 at 9c570f8c33a7a9667507a8e1c0ca68860de3be45. Unconfigured complexity/coverage gates are reported as gaps, never green checks.

## Owner-directed working-file removal, 2026-10-03

The owner explicitly requests removal of all working plans, brainstorms and acceptance files and ignore rules preventing their return. This changes artifact placement only: canonical Feature/ADR requirements, pass/fail criteria, execution order, test mappings and qualification remain mandatory. The original preserved root prefix remains exact; its hash is not replaced.

Implementation contract: REQ-MCAF-008/009 and AC-MCAF-008/009 in [RepositoryGovernance](../Features/RepositoryGovernance.md), TASK-MCAF-CLEAN-001..005. Root owns AGENTS, ignore rules, file deletion, shared contracts and final staging. The validator/test worker owns only scripts/Features/RepositoryGovernance/verify.mjs and tests/KeyLoad.UnitTests/Features/RepositoryGovernance/. The documentation and catalog workers have disjoint frozen paths and preserve concurrent changes.

Ordered stages: update owner policy and these contracts; capture current paths and unrelated changes; remove all three suffix families without relocating plans; ignore root/nested matches without exceptions; replace live references with canonical Feature/ADR contracts; stop requiring planning artifacts and reject their reintroduction; run the original Node validator and focused real-process TUnit cases; review combined diffs and scoped stage/commit/push.

This update affects documentation and repository validation only, with no runtime/API/data/dependency/topology change. Historical receipts retain original source descriptions. Deleted tracked files remain recoverable in Git history; rollback of the new owner rule requires owner direction. Required regressions include positive absence, each suffix at root/nested paths, original prefix/inventory/policy/skill protections, actual ignore behavior and live link checks. Local tests are development evidence; GitHub qualification is separate. The working-file cleanup and local verification are complete; the owning Feature records the nine passing TUnit cases, formatter, inventory, ignore and link evidence and the unrelated shared-build limitation. This does not close the ADR's other refactoring or product-qualification stages.

```mermaid
flowchart LR
    Owner[Owner removes temporary planning files] --> Policy[Canonical Feature and ADR contracts]
    Policy --> Remove[Delete files and ignore suffixes]
    Remove --> Links[Repair live references]
    Links --> Verify[Real Node validator and TUnit checks]
    Verify --> Deliver[Reviewed scoped Git delivery]
```

## Owner-directed feature-local role refactoring, 2026-10-04

Decision: MCAF-ARCH-001 requires populated responsibility folders inside each owning
feature. A flat collection of unrelated grains, commands, queries, models and helpers
is not the target structure. Roles stay inside the canonical slice rather than becoming
global layers. Requirements: REQ-MCAF-010; acceptance: AC-MCAF-010; execution:
TASK-MCAF-LAYOUT-001/002 in RepositoryGovernance. This stage covers KeyLoad.Orleans.

Implementation contract: root first adds policy and captures exact current source bytes,
then moves every feature source to its actual role, preserving dirty/untracked work,
namespaces, API signatures, Orleans aliases/Ids and serialization. Routing owns Grains,
Commands, Queries, Models, Contracts, Streaming, Identity, Serialization, Diagnostics and
Topology. Replication owns GrainServices, Discovery, Transport, Authentication, Replay,
Contracts and Models. ResourceExecution owns Models, Contracts, Serialization,
Authentication and Validation. Small read-capability slices own Queries. The read-only
reviewer verifies responsibility assignment and path consumers; root owns all writes,
reference updates, combined checks and delivery. New API/data/dependencies/topology,
behavior fixes and other projects' layout refactoring are outside this stage.

Rollout is an exact-content physical move with current documentation path repair;
MSBuild's existing recursive source glob includes the files. Rollback reverses physical
paths without losing later code edits; it does not revoke the owner's mandatory rule.
Verification compares every pre/post file byte and complete inventory, checks no flat
feature C# file remains, reviews live links, and runs governance, formatter, Release
build and AppHost unit/recovery/RF3 suites. Existing process-recovery and real SDK/MCP
RF3 contracts remain mandatory. Missing infrastructure or unrelated concurrent compiler
failures are reported and never relabelled as passing. This refactoring does not mark the
ADR's other architecture debt or product qualification complete.

The owner's subsequent whole-solution clarification extends the feature-local role
refactoring to every production, contract, SDK, infrastructure, benchmark and test project.
TASK-MCAF-LAYOUT-003/004/005 own disjoint exact-content project moves and local policy;
TASK-MCAF-LAYOUT-006 owns central integration, current reference repair and verification.
The owning RepositoryGovernance specification contains exact project scopes and join
conditions. Test cases, fixtures, assertion helpers, models and process infrastructure
are grouped within their original slice; executable site/tool artifacts retain their
fully colocated artifact convention. No behavior or authority boundary changes are
introduced. Capture all worker hashes before source moves and verify the complete join;
never rewrite historical SHA-bound receipts or stage another task's code modifications.

The root also owns the narrow structural refactoring of `Core/GraphAndSeries.cs` into
GraphTraversal, TimeSeries and Search partial declarations. Preserve complete original
member text and signatures, document the role map and compare every declaration against
the original snapshot. Only the using/namespace/partial-class shell is repeated; runtime
behavior and existing private cross-model calls remain unchanged. This resolves the
file's mixed-feature ownership rather than assigning it to an arbitrary one-model slice.

The same exact-declaration refactoring applies to `Abstractions/Queries.cs`: place each public
request/result record in its owning DocumentStorage, EventStreams, Messaging,
GraphTraversal, TimeSeries, QueryExecution, Search, BackupRestore or ClusterRouting
slice under Contracts. Preserve XML documentation, signatures, attributes, aliases
and field IDs. Repeat only required using/namespace headers; verify original declaration
text and compile the complete solution. Concurrent API changes remain unstaged.

The existing repository governance validator also rejects C# files directly at
`Features/<SliceName>/` across all projects. Positive evidence is the complete live
inventory; negative development evidence temporarily introduces one owned flat C#
fixture, requires validator failure, removes it and requires success. This enforces
REQ-MCAF-010 without changing runtime contracts; role meaning still requires code review.
