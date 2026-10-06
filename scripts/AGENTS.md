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
- Owner correction 2026-10-03 permits local development tests and bounded experiments through the actual Aspire-owned caller: after restore/build, run `dotnet run --project src/KeyLoad.AppHost --no-build --no-restore --configuration Release -- --KeyLoadTests:Suite=<suite>`. AppHost owns native runners, required topology and complete shutdown. Delivered-source Linux qualification and public comparison evidence still require genuine GitHub Actions artifacts; local results cannot substitute for them.

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

## Owner-directed failed-cell publication, 2026-10-04
- Under ADR-080 the complete authenticated planned-cell inventory may include terminal failed workloads with the fixed safe reason and null report, original failed job/workload conclusions and successful result upload. Successful cells retain their real measurements. This explicit owner correction supersedes all-success publication wording above; missing/corrupt/mixed/expired evidence remains rejected and supplied parser fixtures cannot authenticate GitHub.
- The later owner correction makes Benchmarks a database-only authenticated JSON producer. Website tooling executes in CI from the actual completed own-main Benchmarks workflow_run payload; validate the real CI executor separately from the producer run/attempt/source/event before capture. This supersedes inherited Benchmarks website-executor placement, while retaining original archive, source, freshness, test and coverage requirements. Failed database cells remain null and successful cells retain their measurements.

## Latest benchmark selection, 2026-10-04
- The latest owner clarification requires every separate CI website build, whether own-main push/manual or actual completed Benchmarks workflow_run, to consume the newest completed own-main Benchmarks push/manual producer. Authenticate the trigger separately from the latest selected run/attempt/source/event/conclusion; require its successful complete aggregate and original archives, reject invalid latest evidence without historical fallback, and repeat latest selection for predeploy freshness. This explicitly supersedes publication selection pinned blindly to the triggering producer.

## Latest completed result eligibility, 2026-10-04
- Latest benchmark metrics means the newest completed own-main push/manual Benchmarks producer with success/failure conclusion. Pending, skipped and canceled workflows have no completed comparison cohort and MUST NOT displace ready JSON. Exclude them before choosing the latest eligible producer; then reject its missing/corrupt/failed aggregate without older fallback. A canceled workflow_run event still cannot authorize publication. This refines the latest-result rule without accepting incomplete or fabricated measurements.

## Feature-local executable artifacts
- This module uses the root-approved fully colocated executable-artifact convention: keep each feature-owned script, website module or workflow with its canonical feature/artifact and its existing entry point. C# Grains/Models folders are N/A here because this module contains executable web, shell or YAML artifacts. Preserve source-bound historical receipts; update live consumers when solution source paths move.

## Independent website publication, owner correction 2026-10-06
- Website source changes MUST independently build, qualify and publish the website in CI without waiting for a benchmark run. Use the newest completed own-main Benchmarks producer whose authenticated aggregate is ready when one exists; a run with no successful aggregate is not available website data. If no ready producer exists, publish the qualified product website with no benchmark figures or synthetic measurement artifacts. New ready benchmark JSON MUST automatically trigger a fresh website build/publication. This rule-specific correction supersedes mandatory benchmark availability and newest-failed-producer blocking in the earlier publication rules; corrupt/expired/unauthenticated evidence, provider errors, website test failures and stale source still fail closed. Preserve exactly three workflows, independent CI website jobs, original metric provenance and full metric/archive/browser/coverage gates whenever metrics are included. The no-metrics route has its own complete asset/browser/source/coverage qualification; measurement-only tests and archive gates are N/A to an artifact which includes no measurements, never reported as passed or replaced with fabricated inputs.

## Separate Website workflow, owner correction 2026-10-06

- The owner's explicit correction supersedes the earlier three-workflow limit and every CI/Benchmarks website placement: `Build and Tests` (`ci.yml`) owns full solution build, repository checks and ordinary tests; `Website` (`website.yml`) independently owns site qualification/build/Pages on trusted main pushes, manual dispatch and completed Benchmarks events. `Benchmarks` and manual `Release` retain their existing scopes. Website uses ready authenticated metrics when available and publishes the fully qualified content-only site when none exist; all applicable source, browser, coverage, freshness and permission gates remain mandatory.

## Final Benchmarks Website trigger, owner clarification 2026-10-06

- Benchmarks ends with only the bounded dispatch of the separate Website workflow after aggregation dependencies settle. Website owns site qualification/build/Pages on main pushes and manual dispatch, and has no `workflow_run` completion subscription. Only the final dispatch job receives `actions: write`. An optional producer run is authenticated against GitHub and awaited boundedly before newest-ready selection; it does not supply trusted measurements. This supersedes the earlier completion-subscription and website-executor placement rules while retaining all provenance, qualification, freshness and permission gates.

## Current first-release Website contract, owner corrections 2026-10-06

- The current four workflows are Build and Tests (`build-and-tests.yml`), Benchmarks (`benchmarks.yml`), Website (`website.yml`) and prepared manual Release (`release.yml`). Only Website builds, qualifies and publishes the site on trusted main push/manual; Benchmarks ends with a bounded Website dispatch and Website has no `workflow_run` executor subscription. These explicit owner corrections supersede every earlier three-workflow, filename, executor, placement and completion-subscription clause; every unrelated suite, permission and qualification rule remains mandatory.
- The sole measured producer contract is the current 1,386-worker/2,530-input cohort in ADR-076. Its executed dependency manifest contains exactly 80 current source paths. Remove old-plan readers, schemas, event-file parsers and their exclusive fixtures or inventory entries under the root owner-only migration/legacy super rule. This supersedes earlier 270/277 and old-reader requirements only; retain bounded authenticated REST producer/artifact provenance, exact current source and input hashes, failed/null accounting, strict selected-evidence rejection, freshness and unchanged 80/70/90 coverage thresholds.
- When no current authenticated producer is ready, qualify the complete content-only site without figures. Real native TUnit/Node/Chrome operations, no skips in each applicable suite, source/coverage inventories and needs-gated least-privilege Pages remain required. Local tests enter the same Aspire-owned AppHost and are development evidence; genuine exact-source Linux/provider proof closes delivery. Controlled rejection inputs cannot become published measurements.

## Native TUnit entry, owner correction 2026-10-07
- ADR-117 supersedes the earlier outer AppHost caller requirements: CI starts TUnit directly after build with Detailed output. Test fixtures own Aspire infrastructure startup, readiness, client operations and cleanup. scripts/Features/TestInfrastructure/run-tests.mjs only selects native test arguments/environment; it cannot execute database workloads. RF3 coverage preparation belongs to the TUnit session lifecycle. Preserve every original qualification/artifact gate and separate Benchmarks ownership.
