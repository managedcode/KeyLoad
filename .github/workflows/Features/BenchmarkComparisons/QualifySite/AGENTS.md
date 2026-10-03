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
