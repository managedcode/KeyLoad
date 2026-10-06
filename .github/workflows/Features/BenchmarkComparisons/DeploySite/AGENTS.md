# DeploySite

## Purpose and entry points
- Owns the BenchmarkComparisons DeploySite composite action under ADR-064/AC-PIPE-002..003. Read root/workflow policy, ReleaseDelivery and BenchmarkComparisons specs before edits. Entry: action.yml.

## Boundaries and protected risks
- Preserve every original archive/hash/native/browser/TUnit/coverage/freshness gate. Use actual inherited Benchmarks qualify/deploy context and the exact current run/attempt/source; no historical publication fallback or forged executor.
- Lead alone owns workflows, permissions, source closure, docs and delivery. Three persistent RF3 owners and all isolated measurement jobs remain unchanged.
- The qualification compares both new actions' source/policy with trusted control and the current website checkout. Deployment writes remain confined to the needs-gated deploy job.

## Commands and verification
- Execute only in Linux GitHub Actions under the exact qualify/deploy job name. Static YAML/shell/source checks are not runtime or provider qualification. No local tests or providers.

## Applicable skills
- No installed skill is required for this bounded workflow composition; install none.

## Owner-directed database-only producer, 2026-10-04
- The later owner correction moves this action to the needs-gated CI/ci.yml deploy job after its independent website qualification. This explicitly supersedes inherited Benchmarks executor placement above. Authenticate the actual completed own-main Benchmarks workflow_run producer separately from the CI executor; retain exact archives, source, tests, coverage, freshness and least-privilege Pages/OIDC requirements. Website failures cannot alter or block benchmark JSON.

## Latest benchmark consumer, 2026-10-04
- The latest owner clarification permits the separate build action after JSON production or in CI, including own-main push/manual CI events. Select the newest completed own-main Benchmarks producer across push/manual events and require its successful authenticated aggregate; reject invalid latest evidence without historical fallback. Authenticate an actual workflow_run trigger separately from the selected producer. Preserve all original archive, source, test, coverage, browser and freshness gates; website failures cannot block benchmark JSON.

## Independent website publication, owner correction 2026-10-06
- Website source changes MUST independently build, qualify and publish the website in CI without waiting for a benchmark run. Use the newest completed own-main Benchmarks producer whose authenticated aggregate is ready when one exists; a run with no successful aggregate is not available website data. If no ready producer exists, publish the qualified product website with no benchmark figures or synthetic measurement artifacts. New ready benchmark JSON MUST automatically trigger a fresh website build/publication. This rule-specific correction supersedes mandatory benchmark availability and newest-failed-producer blocking in the earlier publication rules; corrupt/expired/unauthenticated evidence, provider errors, website test failures and stale source still fail closed. Preserve exactly three workflows, independent CI website jobs, original metric provenance and full metric/archive/browser/coverage gates whenever metrics are included. The no-metrics route has its own complete asset/browser/source/coverage qualification; measurement-only tests and archive gates are N/A to an artifact which includes no measurements, never reported as passed or replaced with fabricated inputs.

## Separate Website workflow, owner correction 2026-10-06

- The owner's explicit correction supersedes the earlier three-workflow limit and every CI/Benchmarks website placement: `Build and Tests` (`ci.yml`) owns full solution build, repository checks and ordinary tests; `Website` (`website.yml`) independently owns site qualification/build/Pages on trusted main pushes, manual dispatch and completed Benchmarks events. `Benchmarks` and manual `Release` retain their existing scopes. Website uses ready authenticated metrics when available and publishes the fully qualified content-only site when none exist; all applicable source, browser, coverage, freshness and permission gates remain mandatory.

## Current first-release Website contract, owner corrections 2026-10-06

- The current four workflows are Build and Tests (`build-and-tests.yml`), Benchmarks (`benchmarks.yml`), Website (`website.yml`) and prepared manual Release (`release.yml`). Only Website builds, qualifies and publishes the site on trusted main push/manual; Benchmarks ends with a bounded Website dispatch and Website has no `workflow_run` executor subscription. These explicit owner corrections supersede every earlier three-workflow, filename, executor, placement and completion-subscription clause; every unrelated suite, permission and qualification rule remains mandatory.
- The sole measured producer contract is the current 1,386-worker/2,530-input cohort in ADR-076. Its executed dependency manifest contains exactly 80 current source paths. Remove old-plan readers, schemas, event-file parsers and their exclusive fixtures or inventory entries under the root owner-only migration/legacy super rule. This supersedes earlier 270/277 and old-reader requirements only; retain bounded authenticated REST producer/artifact provenance, exact current source and input hashes, failed/null accounting, strict selected-evidence rejection, freshness and unchanged 80/70/90 coverage thresholds.
- When no current authenticated producer is ready, qualify the complete content-only site without figures. Real native TUnit/Node/Chrome operations, no skips in each applicable suite, source/coverage inventories and needs-gated least-privilege Pages remain required. Local tests enter the same Aspire-owned AppHost and are development evidence; genuine exact-source Linux/provider proof closes delivery. Controlled rejection inputs cannot become published measurements.
