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
- Owns `Features/CodeQuality/site-analyzer-coverage*.ps1` and its frozen JSON contract under the accepted ADR-033 candidate substage. Read `../docs/Features/CodeQuality.md`, ADR-033 and the site-design plan before implementation.
- Use the existing GitHub runner PowerShell and native MTP collector. Prepare exact source hashes before tests; verify unchanged inventory and raw Cobertura integer counts after the complete process exits. Fail on missing/ambiguous evidence or unmet80/70/90 thresholds; never rewrite collector XML, exclude executable sources or substitute coverage for diagnostic regressions.
- Parsing/threshold regressions belong to real TUnit AnalyzerTests and run only in GitHub. Controlled XML inputs remain test data and cannot become published measurements. Root alone owns workflow/configuration/contract/docs integration; scripts cannot change them.
