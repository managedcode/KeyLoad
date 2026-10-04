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
