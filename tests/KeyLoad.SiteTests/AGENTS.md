# KeyLoad.SiteTests

## Purpose and entry points
- Owns the independently buildable static website qualification suite. Entry: KeyLoad.SiteTests.csproj; canonical slice: Features/BenchmarkComparisons/. Read root/site policy, BenchmarkComparisons design/acceptance/execution contracts and ADR-040 before implementation.

## Boundaries and ownership
- TUnit/Microsoft.Testing.Platform, net10.0, centrally pinned packages and all central analyzers are mandatory. No Core/AppHost dependency, alternate runner, mocks/fakes/stubs or browser automation package.
- Exercise actual production JavaScript through a bounded real Node child process and authentic successful GitHub report files. C# owns independent assertions/oracles. Controlled corrupt inputs are test data and must never be published as measurements.
- Tests preserve existing median/repetition/failure/unsupported/order intentions and cover validators, confined paths, raw hashes, clean builds and complete emitted assets. Visual/device/lifecycle exceptions require real browser evidence as specified in ADR-040; do not manufacture device-loss or coverage proof.

## Commands and verification
- Development: dotnet restore tests/KeyLoad.SiteTests/KeyLoad.SiteTests.csproj; dotnet build tests/KeyLoad.SiteTests/KeyLoad.SiteTests.csproj --no-restore --configuration Release.
- GitHub-only qualification: dotnet test --project tests/KeyLoad.SiteTests --no-build --no-restore --configuration Release. Never execute local tests, recovery qualifications or load benchmarks.
- Test inputs: KEYLOAD_SITE_ISOLATED_CAPTURE and KEYLOAD_SITE_ISOLATED_ARCHIVE_RECEIPT identify the authentic complete current cohort; KEYLOAD_SITE_REPOSITORY is the actual source root and KEYLOAD_SITE_SOURCE_REVISION must match HEAD. The original receipt carries exact successful producer provenance.

## Applicable skills
- No repository skill installation. Existing relevant guidance may be read; do not install tools/skills or change global configuration.

## Protected risks
- Never omit a required test, replace unavailable reports with fabricated fixture measurements, weaken analyzers, leak process environment/credentials or mistake historical measured SHA for website source SHA. Keep process timeouts/cancellation/stderr/exit-code checks and isolate disposable output.
- Worker ownership is only Features/BenchmarkComparisons test files. Root owns this policy, project, solution, workflow, shared contracts and governance inventory.

## BC028 publication qualification inputs
- Read BenchmarkComparisons and ADR-040/076 before publication work. Owner-directed immediate legacy removal retires the obsolete SiteGitHubEvidence/SiteGitHubArchive family with its removed producer; current SiteIsolatedGitHub tests retain all applicable stronger provenance/archive checks. Worker scopes are exact and disjoint; root alone owns workflow and source closure.
- The new publication qualification path MUST prepare authentic reports from the same digest-verified immutable GitHub ZIP through mandatory BCL before-session setup, with complete confinement/file-set/bounds preflight before owned output. No pre-extracted fallback, fake transport or custom ZIP parser.
- Preserve archive/raw-file hashes across extraction/tests/final build and every existing numerical/browser assertion. Controlled malformed metadata/ZIP copies are rejection data only, never provider/publication proof.
- KEYLOAD_SITE_SOURCE_REVISION MUST match actual HEAD. Every remaining production source stays in the closed native coverage inventory; critical validators retain critical90. Retired executable sources and only their obsolete entries are removed together under ADR-076, without threshold reduction or remaining-source exclusion.

## Isolated270 publication inputs
- ADR-076 supersedes the legacy12-file co-dependency: the authenticated270-cell current cohort is the sole live producer. Mandatory before-session BCL preparation consumes both exact original isolated ZIPs and verifies277 inputs; immutable receipts, archives and raw hashes are rechecked after the complete suite and around the final builder.
- Root supplies KEYLOAD_SITE_ISOLATED_CAPTURE and KEYLOAD_SITE_ISOLATED_ARCHIVE_RECEIPT. No pre-extracted fallback, skipped unavailable cohort, forged CI executor environment or fabricated metrics may satisfy these gates.
- All eleven site-isolated-github- modules retain native80/70 coverage and individual critical90. Preserve every remaining numerical, browser, archive, safety and provenance criterion; remove only retired executable sources and their obsolete entries under ADR-076. Root owns workflow and dependency closure joins.
## Feature slice responsibility folders
- Place every feature-owned source file under `Features/<SliceName>/<Role>/`, using populated feature-local roles such as `Cases/`, `Fixtures/`, `Assertions/`, `Models/`, `Processes/`, `Contracts/`, `Serialization/` or `Helpers/` according to the file's actual responsibility. Preserve an existing nested scenario/domain folder and add the role beneath it. Create only roles that own files.
- Keep genuinely shared test infrastructure and project composition entry points at their existing shared ownership paths; do not duplicate them into a feature.
- Structural moves preserve every source byte, namespace, type/serializer identity and test assertion. Do not change behavior, test logic or path references as part of a layout-only move; report source-path-sensitive joins to the solution integrator.

## Independent website publication, owner correction 2026-10-06
- Website source changes MUST independently build, qualify and publish the website in CI without waiting for a benchmark run. Use the newest completed own-main Benchmarks producer whose authenticated aggregate is ready when one exists; a run with no successful aggregate is not available website data. If no ready producer exists, publish the qualified product website with no benchmark figures or synthetic measurement artifacts. New ready benchmark JSON MUST automatically trigger a fresh website build/publication. This rule-specific correction supersedes mandatory benchmark availability and newest-failed-producer blocking in the earlier publication rules; corrupt/expired/unauthenticated evidence, provider errors, website test failures and stale source still fail closed. Preserve exactly three workflows, independent CI website jobs, original metric provenance and full metric/archive/browser/coverage gates whenever metrics are included. The no-metrics route has its own complete asset/browser/source/coverage qualification; measurement-only tests and archive gates are N/A to an artifact which includes no measurements, never reported as passed or replaced with fabricated inputs.

## Separate Website workflow, owner correction 2026-10-06

- The standalone `Website` workflow (`website.yml`) owns complete applicable website qualification and Pages publication. Native context operations authenticate Website rather than CI, preserving optional ready metrics, content-only qualification, original provenance and all source/browser/coverage/freshness gates. This explicit correction supersedes every earlier CI/Benchmarks website placement and three-workflow limit; Build and Tests remains the full solution/ordinary test workflow.

## Current first-release Website contract, owner corrections 2026-10-06

- The current four workflows are Build and Tests (`build-and-tests.yml`), Benchmarks (`benchmarks.yml`), Website (`website.yml`) and prepared manual Release (`release.yml`). Only Website builds, qualifies and publishes the site on trusted main push/manual; Benchmarks ends with a bounded Website dispatch and Website has no `workflow_run` executor subscription. These explicit owner corrections supersede every earlier three-workflow, filename, executor, placement and completion-subscription clause; every unrelated suite, permission and qualification rule remains mandatory.
- The sole measured producer contract is the current 1,386-worker/2,530-input cohort in ADR-076. Its executed dependency manifest contains exactly 84 current source paths. Remove old-plan readers, schemas, event-file parsers and their exclusive fixtures or inventory entries under the root owner-only migration/legacy super rule. This supersedes earlier 270/277 and old-reader requirements only; retain bounded authenticated REST producer/artifact provenance, exact current source and input hashes, failed/null accounting, strict selected-evidence rejection, freshness and unchanged 80/70/90 coverage thresholds.
- When no current authenticated producer is ready, qualify the complete content-only site without figures. Real native TUnit/Node/Chrome operations, no skips in each applicable suite, source/coverage inventories and needs-gated least-privilege Pages remain required. Local tests enter the same Aspire-owned AppHost and are development evidence; genuine exact-source Linux/provider proof closes delivery. Controlled rejection inputs cannot become published measurements.


## Current native comparison topology, owner direction 2026-10-09

- ADR-122 supersedes every active two-node comparison inventory: current benchmark planning, admission, original-file provenance and site projection accept only native one-node or three-node cells. Reject two-node current inputs before acquisition; RF3 majority remains two acknowledgements. Preserve immutable historical original bytes/hashes without a legacy reader or current qualification fallback.
- The existing control/scale/vector family now has 924 workers and 1,716 original inputs (1,656 suite files and 60 provider files), with the 84-path executed source closure. This current topology contract supersedes earlier active 1,386-worker/2,530-input clauses; new document-family evidence must receive its own exact inventory before publication. All source, authorization, native operation, complete cohort, provenance and coverage gates remain mandatory.
