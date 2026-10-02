# KeyLoad.SiteTests

## Purpose and entry points
- Owns the independently buildable static website qualification suite. Entry: KeyLoad.SiteTests.csproj; canonical slice: Features/BenchmarkComparisons/. Read root/site policy, BenchmarkComparisons, ADR-040 and site-design.acceptance.md before implementation.

## Boundaries and ownership
- TUnit/Microsoft.Testing.Platform, net10.0, centrally pinned packages and all central analyzers are mandatory. No Core/AppHost dependency, alternate runner, mocks/fakes/stubs or browser automation package.
- Exercise actual production JavaScript through a bounded real Node child process and authentic successful GitHub report files. C# owns independent assertions/oracles. Controlled corrupt inputs are test data and must never be published as measurements.
- Tests preserve existing median/repetition/failure/unsupported/order intentions and cover validators, confined paths, raw hashes, clean builds and complete emitted assets. Visual/device/lifecycle exceptions require real browser evidence as specified in ADR-040; do not manufacture device-loss or coverage proof.

## Commands and verification
- Development: dotnet restore tests/KeyLoad.SiteTests/KeyLoad.SiteTests.csproj; dotnet build tests/KeyLoad.SiteTests/KeyLoad.SiteTests.csproj --no-restore --configuration Release.
- GitHub-only qualification: dotnet test --project tests/KeyLoad.SiteTests --no-build --no-restore --configuration Release. Never execute local tests, recovery qualifications or load benchmarks.
- Test process inputs: KEYLOAD_SITE_REPORTS points at authentic downloaded comparison profiles; KEYLOAD_SITE_REPOSITORY identifies the real source root; KEYLOAD_SITE_EVIDENCE_RUN and KEYLOAD_SITE_MEASURED_REVISION record their successful GitHub provenance.

## Applicable skills
- No repository skill installation. Existing relevant guidance may be read; do not install tools/skills or change global configuration.

## Protected risks
- Never omit a required test, replace unavailable reports with fabricated fixture measurements, weaken analyzers, leak process environment/credentials or mistake historical measured SHA for website source SHA. Keep process timeouts/cancellation/stderr/exit-code checks and isolate disposable output.
- Worker ownership is only Features/BenchmarkComparisons test files. Root owns this policy, project, solution, workflow, shared contracts and governance inventory.
