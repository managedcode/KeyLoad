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

## Fresh-evidence publication extension
- The owner's subsequent separate post-test deployment request is REQ/AC-BC-028 under ADR-040 and `site-publication.acceptance.md`/`.plan.md`. Preserve every prior policy and measured-source checkout; this new task authorizes the publication workflow after its complete gates. DNS remains excluded.
- `pages.yml` MUST use trusted selection, one complete qualification/build path and a needs-gated deploy-only job. Only deployment receives Pages/OIDC writes. Qualification MUST retain all existing tests, native thresholds, real Chrome, format and governance without skips or fake transport.
- Publish MUST require the highest authenticated own-main-push CI run/current attempt itself successful, exact successful comparison job/steps, one immutable unexpired artifact and verified same-archive bytes; newer unsuccessful work blocks refresh. Recheck the complete tuple immediately before deployment. Historical pinning is validation-only.
- Record actual checkout source separately from triggering/control workflow SHA. Published measurements and report hashes MUST match the downloaded qualified JSON; successful workflow/provider/live evidence is required before claiming publication.

## Owner-directed site-only boundary, 2026-10-02
- The owner's explicit subsequent instruction to finish only the website supersedes the earlier BC028 conservative-B/equal-website-and-measured-SHA task restrictions above. Preserve those records; the current implementation contract is site-publication.acceptance.md and ADR-040. Inspect measured source in a separate sibling checkout; qualify current trusted-main website source independently and record every revision accurately.
- Select the highest successful actual comparison job by main-push run number and descending attempt history, independent of unrelated job or overall workflow conclusion. After selecting success, missing/expired/ambiguous artifacts and invalid reports MUST fail without fallback. Recheck both current website source and the exact evidence tuple before deployment.
- The separate pages.yml MUST trigger on main site/** changes, producer workflow completion and manual dispatch. workflow_run arrives after the enclosing workflow; do not claim an individual-job webhook. Full website qualification and least privileges remain mandatory; database implementation and DNS remain outside this task.
