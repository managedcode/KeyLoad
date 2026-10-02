# MCAF governance acceptance

Goal: configure MCAF for this existing multi-project .NET repository while preserving every mandatory rule. Skills are not installed by the user's explicit direction. Runtime feature migrations are outside this governance installation.

Actors: repository owner, lead planner/integrator, read-only architect/reviewer, bounded governance coding workers. Entry points: root and project-local AGENTS.md, docs/Architecture.md, feature specs, ADRs and CI workflows. Workers have only their named write paths; no worker may commit, push, install skills or modify production code for this task.

| ID | Pass condition | Failure condition | Evidence |
|---|---|---|---|
| AC-MCAF-001 | Every byte of the existing root remains as its unchanged prefix; no existing local file is overwritten. | Deleted, rewritten, weakened or omitted policy. | Baseline byte count/hash, static validator, complete diff review. |
| AC-MCAF-002 | Root records MCAF-GOV-001, MCAF-ARCH-001, MCAF-AI-001 and MCAF-REQ-001 with customized commands, maintainability and workflow rules. | Placeholder commands, missing mandatory rules, an added bypass. | Validator and architectural review. |
| AC-MCAF-003 | Each csproj directory and the site, workflows, documentation and scripts modules has a local AGENTS.md with purpose, entry points, boundaries, commands, skills policy and protected risks. | Missing project, generic ownership with no entry point, or weaker root policy. | Inventory validator and review of every local diff. |
| AC-MCAF-004 | Architecture map covers the complete repository, interfaces, key types and canonical feature names; existing layout debt has a dated migration ADR. | Split repository target, missing surface, diagram-free map, or declaration of existing debt as compliant. | Mermaid source review, inventory and ADR links. |
| AC-MCAF-005 | Before coding workers start, feature and ADR define stable requirements, measurable acceptance, disjoint ownership, dependencies, exact checks, terminal states and join/escalation rules. | Worker starts without a contract, overlaps write ownership, or partial output is treated as complete. | Task graph, native task status and joined evidence packets. |
| AC-MCAF-006 | No skill directory, SKILL.md, skill tool or global agent configuration is created or changed. | Any skill is installed, removed or updated. | Before/after skill inventory and scoped Git diff. |
| AC-MCAF-007 | Installation reports policy conflicts and implementation gaps honestly; all qualification/performance claims link to GitHub Actions evidence. | Local load data appears as published evidence or migration gaps are marked complete. | Conflict record, CI baseline link, final review. |

Negative and edge flows: an upstream template change cannot overwrite an existing file; a missing project policy fails validation; a changed preserved prefix fails validation; absent CI evidence remains pending; a worker ambiguity ends as blocked and cannot unblock dependent work. Missing coverage/complexity tooling is documented as unconfigured, not reported as a passing gate.

No executable product behavior changes are planned in this installation. Static governance validation and independent review are the acceptance proof; runtime suites remain mandatory for subsequent product changes and execute in GitHub Actions. The baseline is [GitHub Actions 36926803549](https://github.com/managedcode/KeyLoad/actions/runs/36926803549).

Rollout adds governance files without moving or deleting product files. Rollback of policy requires rule-specific owner direction; no automatic cleanup may remove rules. The current DotNext, xUnit and host-process implementation is recorded as migration debt, not an accepted exception.
