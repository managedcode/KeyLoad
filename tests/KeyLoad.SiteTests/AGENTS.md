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
