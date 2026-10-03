# Scripts module

## Purpose

This module contains repository tooling that supports documented product and governance workflows. It does not own runtime database behavior.

## Entry points

- `Features/RepositoryGovernance/verify.mjs` validates the MCAF installation record against the real repository inventory and policy files.

## Boundaries

- Keep feature-owned tooling under `Features/<SliceName>/` and link its durable specification from `docs/Features/`.
- The governance validator uses Node.js built-ins, reads only the selected repository root, and does not follow symbolic links while scanning.
- Tooling must report policy or inventory failures; it must not rewrite project files or substitute for application qualification.

## Commands

- Static governance validation: `node scripts/Features/RepositoryGovernance/verify.mjs`.
- Isolated validation root: `node scripts/Features/RepositoryGovernance/verify.mjs --root <path>`.
- Required application build and test qualification run in GitHub Actions as specified by the root `AGENTS.md`; do not run tests or benchmarks locally.

## Skills

No repository skills are installed or applicable to this tooling module. Do not install skills or modify global agent configuration for this task.

## Protected risks

- Preserve the installation record's byte-prefix hash check; never update the recorded baseline to conceal a changed root policy.
- Keep inventory traversal inside the repository and skip generated output directories. Symbolic links must not extend the scan outside its root.
- Do not edit application code, shared policy, central configuration, local test policy, or installation evidence from this module's tooling changes.
- Static validation is governance evidence only and cannot qualify runtime behavior, durability, or production readiness.

## Read first and canonical slice ownership

- Read `../AGENTS.md`, `../docs/Architecture.md`, `../docs/Features/RepositoryGovernance.md` and `../docs/ADR/ADR-032-mcaf-governance.md` before changing tooling.
- Owned slice: `RepositoryGovernance`; implementation path: `Features/RepositoryGovernance/`; durable contract: `docs/Features/RepositoryGovernance.md`.
- Also owns `BenchmarkComparisons` tooling under `Features/BenchmarkComparisons/`. Read `../docs/Features/BenchmarkComparisons.md` and `../docs/ADR/ADR-034-cluster-comparisons.md` before editing that slice. Derive all published measurements and chart values from validated successful GitHub Actions JSON; preserve exact source/run/profile links.

## CodeQuality candidate coverage tooling
- Owns `Features/CodeQuality/site-analyzer-coverage*.ps1` and its frozen JSON contract under the accepted ADR-033 candidate substage. Read `../docs/Features/CodeQuality.md`, ADR-033 and the BenchmarkComparisons design contract in ADR-040 before implementation.
- Use the existing GitHub runner PowerShell and native MTP collector. Prepare exact source hashes before tests; verify unchanged inventory and raw Cobertura integer counts after the complete process exits. Fail on missing/ambiguous evidence or unmet80/70/90 thresholds; never rewrite collector XML, exclude executable sources or substitute coverage for diagnostic regressions.
- Parsing/threshold regressions belong to real TUnit AnalyzerTests and run only in GitHub. Controlled XML inputs remain test data and cannot become published measurements. Root alone owns workflow/configuration/contract/docs integration; scripts cannot change them.

## BC028 GitHub evidence tooling
- Owner-directed immediate legacy removal under ADR-076 retires `github-evidence*.mjs` and its obsolete comparison-smoke/comparison-suite transport. The live `site-isolated-github-*.mjs` contract validates the complete current Benchmarks cohort, original receipts, confined archive inputs and producer freshness. Supplied files never authenticate GitHub; trusted workflow REST transport remains mandatory.
- Every remaining production module MUST be inventoried with native TUnit/Node coverage; all eleven current site-isolated GitHub validators retain individual critical90 coverage. No HTTP doubles, custom ZIP parser, threshold weakening, partial cohort or historical fallback. Root alone owns workflow/environment/inventory/docs integration.
- The 2026-10-02 owner-directed site-only contract qualifies actual successful comparison-job JSON independently of unrelated overall CI failure. The earlier successful-run wording remains its historical record; exact attempt/job/step/artifact/digest proof and complete retained search history are mandatory. Use the revised AC-BC-028 stateful local-capture CLI contract, not the superseded conservative-B selection.

## StorageRecovery prior-executable tooling
- Owns `Features/StorageRecovery/build-native5-probe.sh` under ADR-077. Export only its immutable previous source revision into an owned temporary directory; overlay only the isolated probe/fixture and their composition dispatch. Never replace or imitate the prior provider, serializer, identity validator or snapshot reader.
- Bind the original commit/tree/archive and exact driver/assembly hashes in a bounded build receipt. Missing, altered or ambiguous probe artifacts fail qualification; do not skip an upgrade test or substitute a current writer relabeled as an old executable. Builds prepare fixtures; every runtime child runs inside a TUnit suite owned by the unified Aspire AppHost.
- Preserve source/user directories and unrelated work. Clean only tool-created temporary exports; never stash, install tools, mutate Git history or publish this fixture as a product package. Root owns workflow, format inventory and original evidence integration.

## Owner-directed failed-cell publication, 2026-10-04
- Under ADR-080 the complete authenticated planned-cell inventory may include terminal failed workloads with the fixed safe reason and null report, original failed job/workload conclusions and successful result upload. Successful cells retain their real measurements. This explicit owner correction supersedes all-success publication wording above; missing/corrupt/mixed/expired evidence remains rejected and supplied parser fixtures cannot authenticate GitHub.
