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
2. Create brainstorm, acceptance, feature specification, architecture map and this ADR before any delegated write task starts.
3. Merge root policy. Record conflicts rather than silently selecting weaker wording. A read-only highest-capability architect reviews the decisions.
4. TASK-MCAF-LOCAL-002 owns only new local AGENTS.md in the 20 csproj roots, site, .github/workflows and docs. It must name entry points, boundaries, commands, protected risks and no-install skill policy.
5. TASK-MCAF-CHECK-003 owns only scripts/Features/RepositoryGovernance/verify.mjs and new scripts/AGENTS.md. It uses Node built-ins, consumes docs/implementation/mcaf-installation.json and scans the real repository for project/module coverage, prefix preservation, policy IDs, required documents and unexpected skills.
6. Lead waits for every required result, inspects every diff, fixes integration issues, runs the validator, reviews links and Mermaid sources, and records acceptance evidence. TASK-MCAF-REVIEW-005 supplies the highest-capability independent final review. Workers cannot commit, push or install anything.
7. Qualification and published performance data remain GitHub Actions evidence. A governance bootstrap cannot mark unrelated product migrations complete.

Start condition: feature, acceptance, plan and ADR contracts exist. Shared contracts and central docs have one integration owner. Terminal states: complete, blocked, failed or cancelled. Only a reviewed complete evidence packet can satisfy a join condition. Ambiguity, unsafe file overlap or missing credentials must be escalated immediately.

## Existing architecture migration debt

The current source layout predates MCAF. It is not declared compliant by this ADR. The target is mirrored Features/<SliceName>/ paths with shared composition roots and building blocks outside feature folders. Lead owner; removal date: 2026-10-15.

| Existing paths | Target / scope | Verification and migration owner |
|---|---|---|
| benchmarks/KeyLoad.Comparisons/*.cs and Targets/; AppHost/BenchmarkResources.cs; ComparisonTests/ComparisonTests.cs; site benchmark code | Features/BenchmarkComparisons/ in each applicable technical root; shared executable entry points remain composition roots. | BenchmarkComparisons feature owner; identical corpus, real Docker/Aspire topology, qualified GitHub JSON and site proof. |
| Core/Documents.cs, Events.cs, Messaging.cs, GraphAndSeries.cs; query and security feature files | DocumentStorage, EventStreams, Messaging, GraphTraversal, Search and Authorization slices, preserving node-local storage ownership. | Owning feature leads; mapped contracts and existing recovery/security regressions must pass before each move. |
| Existing unit/recovery/integration feature tests in flat project roots | Matching Features/<SliceName>/ test paths; fixtures and real-process hosts remain shared test infrastructure. | Owning feature leads; no skipped or weakened tests. |

New feature-owned artifacts must use the target layout. Existing runtime divergences (unqualified TUnit migration, DotNext consensus, host-process RF3, incomplete official MCP and blob surfaces) are separately tracked implementation gaps, not exceptions to mandatory policy and not work completed by MCAF installation. TUnit references and test source have been migrated locally after the initial inventory; only a successful delivered-source GitHub run can qualify them.

### Static site migration join (2026-10-02)

[ADR-040](ADR-040-static-site-threejs-evidence.md) and [the site plan](../../site-design.plan.md) now own the website portion of this debt: feature HTML/modules/styles, pinned same-origin Three.js, independent TUnit `KeyLoad.SiteTests`, atomic historical-evidence loading and a validation-only Pages job. The new project's local policy was created before code. Replacement packets, static/raw-byte proof, manual browser evidence and the exact-source GitHub suite must all join before that portion is marked complete. This extension does not close the benchmark/runtime migrations in the table or qualify new database topologies.

## Conflict register

- The incoming template permits local exceptions and obsolete-rule removal. Owner policy forbids weakening or omitting existing rules: existing policy stays mandatory; no exception can weaken it.
- The incoming template asks to install skills. Owner's latest explicit instruction says not to install skills: no skills are added or changed.
- Historical supplied AGENTS required lock files; an explicit owner correction requested removal of packages.lock.json before this bootstrap. The current on-disk mandatory root prohibits generating or committing them. This installation preserves that current file exactly and does not change package policy or central build settings.
- The design document discusses DotNext as an earlier candidate and optional per-request facades; current root forbids DotNext and requires a separate Orleans grain per request. Current stricter policy controls future implementation. The product specification is not silently rewritten.
- Existing site validation uses Node's test runner while current root policy says all tests use TUnit. Existing Node test invocations are recorded migration debt, not authorization to add an alternate framework. Static installation validation does not replace runtime tests.

## Rollout, rollback and verification

Installation adds documentation and a static validator only; it neither changes persisted data nor publishes fabricated metrics. Root policy rollback/removal requires explicit rule-specific owner direction. No preexisting source/config file is reset.

Verification: run node scripts/Features/RepositoryGovernance/verify.mjs; inspect all local files and the root diff; inspect native worker statuses and final evidence; confirm no skill changes. Baseline runtime proof is GitHub Actions 36926803549 at 9c570f8c33a7a9667507a8e1c0ca68860de3be45. Unconfigured complexity/coverage gates are reported as gaps, never green checks.
