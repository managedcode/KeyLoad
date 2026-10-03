# GitHub Actions workflows

## Purpose and entry points
- Owns repository CI qualification and GitHub Pages publication workflows.
- Canonical workflows: `ci.yml` (restore, Release build, TUnit unit/integration/recovery suites and comparison artifact production) and `pages.yml` (verified artifact download, site checks/build and Pages deployment).
- Latest owner correction 2026-10-03 supersedes the historical placements above and ADR-062's five-workflow scope: exactly `ci.yml` (`CI`) combines build/rules/unit/scalar/recovery/RF3 for PR/push/manual checks; `benchmarks.yml` (`Benchmarks`) owns all load/comparison/TimeSeries checks followed by aggregate-gated website qualification and publication; `release.yml` (`Release`) builds all packages and actual database images/distribution and creates the release/tag `v<major>.<minor>.<yyMMdd>.<daily-build>`. Remove standalone Tests/Website/governance/per-feature benchmark workflows. Preserve authentic historical KeyLoad CI report bytes and run identity independently of the current CI name; newer CI build/test runs are not historical measurement producers.

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
- The owner's subsequent separate post-test deployment request is REQ/AC-BC-028 under ADR-040 and the publication acceptance/execution contract in `docs/Features/BenchmarkComparisons.md`. Preserve every prior policy and measured-source checkout; this new task authorizes the publication workflow after its complete gates. DNS remains excluded.
- `pages.yml` MUST use trusted selection, one complete qualification/build path and a needs-gated deploy-only job. Only deployment receives Pages/OIDC writes. Qualification MUST retain all existing tests, native thresholds, real Chrome, format and governance without skips or fake transport.
- Publish MUST require the highest authenticated own-main-push CI run/current attempt itself successful, exact successful comparison job/steps, one immutable unexpired artifact and verified same-archive bytes; newer unsuccessful work blocks refresh. Recheck the complete tuple immediately before deployment. Historical pinning is validation-only.
- Record actual checkout source separately from triggering/control workflow SHA. Published measurements and report hashes MUST match the downloaded qualified JSON; successful workflow/provider/live evidence is required before claiming publication.

## Owner-directed site-only boundary, 2026-10-02
- The owner's explicit subsequent instruction to finish only the website supersedes the earlier BC028 conservative-B/equal-website-and-measured-SHA task restrictions above. Preserve those records; the current implementation contract is the BenchmarkComparisons publication acceptance/execution contract and ADR-040. Inspect measured source in a separate sibling checkout; qualify current trusted-main website source independently and record every revision accurately.
- Select the highest successful actual comparison job by main-push run number and descending attempt history, independent of unrelated job or overall workflow conclusion. After selecting success, missing/expired/ambiguous artifacts and invalid reports MUST fail without fallback. Recheck both current website source and the exact evidence tuple before deployment.
- The separate pages.yml MUST trigger on main site/** changes, producer workflow completion and manual dispatch. workflow_run arrives after the enclosing workflow; do not claim an individual-job webhook. Full website qualification and least privileges remain mandatory; database implementation and DNS remain outside this task.

## Isolated native comparison publication
- ADR056 adds authenticated successful comparison-aggregate selection and the complete270-cell/native-image proof, independently of the retained legacy comparison evidence. Qualify both original archives and all277 isolated inputs through BCL before-session preparation and preserve their byte hashes after tests and around the final builder.
- Capture actual Pages executor context; never impersonate the CI-only job environment. Historical pins are validation-only. Hash and compare the complete closed executed49-file dependency closure between trusted control and website source; separately inspect the isolated measured source.
- Retain full site/TUnit/browser/native coverage/no-skip gates. Qualification timeout180min and deployment freshness90min accommodate the bounded same-route provider rate protocol. Both native evidence freshness checks and current website SHA must pass immediately before needs-gated least-privilege Pages deployment.

## Owner-directed integrated publication and release, 2026-10-03
- The latest three-pipeline instruction explicitly supersedes the separate pages.yml placement above. Move its complete qualification/deploy stages behind the successful Benchmarks aggregate, bind current producer run/attempt/SHA directly from GitHub's executor context, and recheck that tuple before deployment. Historical validation pins cannot impersonate this current producer.
- Release uses UTC daily version reservation, immutable source-bound tags, actual database image/package/distribution hashes and an idempotent GitHub Release. Limit contents/packages write permissions to the publication job; reservation/build/test jobs remain read-only. Do not publish a failed build, overwrite an existing tag/image/release asset, bypass required checks or claim runtime qualification from packaging.

## Shared parallel Benchmarks and readable steps
- The latest owner correction places the actual pinned TimeSeries image test and retained facts in common image preparation, with no standalone feature branch. Keep all existing workload checks and isolated native measurements; staged intensive TimeSeries measurements remain explicitly pending.
- Build/checks, plan and images run independently; preflight, CRUD and specialized matrices share only real plan/image inputs, never each other's completion or an arbitrary max-parallel cap. Aggregate explicitly waits for all six preparation/check/matrix jobs before website checks/publication.
- Every authored job/step in the three workflows and used composites has a concrete readable name, including checkout, SDK setup, restore/build/test and uploads. Change displayed-name validators/oracles atomically; preserve stable internal job IDs, authenticated artifacts and all historical report bytes. Explicit owner direction supersedes earlier frozen displayed labels, without relaxing any identity, workload, topology, permission, coverage or publication gate.

## Owner-directed failed-cell publication, 2026-10-04
- ADR-080 and the latest explicit owner instruction supersede the all-success cohort requirement: finish every independent workload and publish authenticated successful measurements while failed workloads retain null reports and actual failed job/workload conclusions. Keep complete planned-cell artifact accounting, successful image authority, full site qualification, exact source/run/attempt identity and least privileges. Unrelated KeyLoad build/runtime failures do not by themselves skip aggregation or website jobs; missing/corrupt evidence and failed site gates still block publication.
