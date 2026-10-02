# GitHub Actions workflows

## Purpose and entry points
- Owns repository CI qualification and GitHub Pages publication workflows.
- Canonical workflows: `ci.yml` (restore, Release build, TUnit unit/integration/recovery suites and comparison artifact production) and `pages.yml` (verified artifact download, site checks/build and Pages deployment).

## Ownership and boundaries
- Workflow changes belong to the feature or infrastructure contract they implement and must preserve the repository's single-repository, canonical-slice architecture. Workflow YAML is delivery infrastructure, not a place to hide missing product behavior.
- Qualification and tests must run in GitHub Actions. Do not add local qualification shortcuts, skipped suites, fake runtime proof, unaudited performance values, or claims beyond the run's actual SHA and artifacts.
- Keep permissions least-privileged, actions pinned, secrets out of logs, and Pages evidence bound to the successful source run/revision.

## Commands and evidence
- CI test commands are the workflow's actual invocations: `dotnet build KeyLoad.slnx --no-restore --configuration Release`; `dotnet test --project tests/KeyLoad.UnitTests --no-build --no-restore --configuration Release`; equivalent commands for `tests/KeyLoad.IntegrationTests`, `tests/KeyLoad.RecoveryTests` and `tests/KeyLoad.ComparisonTests` as defined in `ci.yml`.
- Pages invokes `node --test site/scripts/measurements.test.mjs` and `node site/scripts/build.mjs` against the downloaded comparison artifact. Dispatch/inspect using the root workflow, retain run/job URLs, exact SHA and artifacts. These checks are not to be run locally as substitutes for CI qualification.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Never bypass branch protections, expose tokens, downgrade a required suite, or conflate build success with RF3, recovery, endurance or production readiness.

## Read-first and canonical slice ownership
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned slices: `RepositoryGovernance` and `BenchmarkComparisons`; target paths: `Features/RepositoryGovernance/` and `Features/BenchmarkComparisons/`.
- Workflow edits are shared delivery infrastructure for these slices; retain the exact evidence provenance and permission boundaries.
- `pages.yml` currently invokes Node’s `node --test` runner; this conflicts with root policy requiring TUnit for all tests and is migration debt, not permission to add another framework.

## ADR-040 site qualification migration
- Existing Node command listings above are the preserved historical baseline. Root TUnit policy controls their replacement with the full independently buildable `tests/KeyLoad.SiteTests` suite in `pages.yml`. Its explicit validation-only mode qualifies candidate website source against downloaded successful-main-CI historical reports and uploads review artifacts without deploying.
- Validation and publication jobs retain least privileges and separate website-source and measured-source revisions. Publication must continue to use qualified measured-source checkout and cannot infer current database qualification from a historical site test. A validation result never authorizes DNS or public deployment.
