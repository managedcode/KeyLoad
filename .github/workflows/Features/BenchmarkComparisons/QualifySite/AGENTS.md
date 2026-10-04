# QualifySite

## Purpose and entry points
- Owns the BenchmarkComparisons QualifySite composite action under ADR-064/AC-PIPE-002..003. Read root/workflow policy, ReleaseDelivery and BenchmarkComparisons specs before edits. Entry: action.yml.

## Boundaries and protected risks
- Preserve every original archive/hash/native/browser/TUnit/coverage/freshness gate. Use actual inherited Benchmarks qualify/deploy context and the exact current run/attempt/source; no historical publication fallback or forged executor.
- Lead alone owns workflows, permissions, source closure, docs and delivery. Three persistent RF3 owners and all isolated measurement jobs remain unchanged.
- The qualification compares both new actions' source/policy with trusted control and the current website checkout. Deployment writes remain confined to the needs-gated deploy job.

## Commands and verification
- Execute only in Linux GitHub Actions under the exact qualify/deploy job name. Static YAML/shell/source checks are not runtime or provider qualification. No local tests or providers.

## Applicable skills
- No installed skill is required for this bounded workflow composition; install none.

## Owner-directed failed-cell publication, 2026-10-04
- ADR-080 allows authenticated failed workload/null-report cells; keep every existing archive, source, TUnit, coverage, browser and freshness gate. The trusted producer/builder dependency closure now includes failed-cell finalization and its actual imports; source-bound counts change together with validators.
- The later owner correction moves this action to CI/ci.yml, triggered by an actual completed own-main Benchmarks workflow_run. This explicitly supersedes the inherited Benchmarks executor placement above. CI executor/control/website identity and benchmark producer source/run/attempt/event remain distinct and authenticated; browser, source, archive, TUnit, coverage and freshness gates remain mandatory in CI and cannot block benchmark JSON production.

## Latest benchmark consumer, 2026-10-04
- The latest owner clarification permits the separate build action after JSON production or in CI, including own-main push/manual CI events. Select the newest completed own-main Benchmarks producer across push/manual events and require its successful authenticated aggregate; reject invalid latest evidence without historical fallback. Authenticate an actual workflow_run trigger separately from the selected producer. Preserve all original archive, source, test, coverage, browser and freshness gates; website failures cannot block benchmark JSON.
