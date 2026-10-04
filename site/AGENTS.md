# KeyLoad site

## Purpose and entry points
- Owns the static Pages website that presents benchmark comparisons using verified GitHub Actions reports.
- HTML/modules/styles entry: `Features/BenchmarkComparisons/`; thin builder: `scripts/build.mjs`; real qualification: `tests/KeyLoad.SiteTests` through the unified Aspire AppHost.

## Ownership and boundaries
- Feature-owned site behavior MUST live under `Features/<SliceName>/` with the same canonical feature name and `docs/Features/<SliceName>.md`; retired flat assets MUST be removed with their obsolete references.
- The site MUST display only the complete authenticated current Benchmarks cohort, preserve exact original source/run/attempt/artifact links and pass all publication gates. No fabricated, local-only, durability or production-readiness claims.
- Do not introduce a backend, credentials, or a second source of truth for benchmark measurements.
- Preserve the official Three.js 0.186.1 vendor files byte-for-byte against their local manifest and license. `.gitattributes` excludes only upstream `space-before-tab` findings for those exact two files so generic diff checks do not require mutating vendor bytes; authored site files retain normal whitespace checks.

## Commands and evidence
- Benchmarks runs complete Aspire-owned TUnit/browser/coverage qualification before the bounded standalone builder consumes the actual isolated aggregate and original receipt. The obsolete comparison-suite/three-profile publisher is retired under owner-directed ADR-076.
- Run publication qualification only through `.github/workflows/benchmarks.yml`; local development evidence cannot qualify published measurements.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Preserve accessible rendering, provenance links and strict input validation. Never include credentials or claim site publication until the Pages workflow succeeds.

## Read-first and canonical slice ownership
- Read the [root policy](../AGENTS.md), [architecture map](../docs/Architecture.md), [RepositoryGovernance feature](../docs/Features/RepositoryGovernance.md), and [ADR-032](../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned slice: `BenchmarkComparisons`; target path: `Features/BenchmarkComparisons/`, matching `docs/Features/BenchmarkComparisons.md`.
- TUnit through Aspire is the only qualification runner. Node probes and Chrome are real child tools of these tests; adding another test framework is forbidden.

## ADR-040 migration execution
- The canonical HTML/modules/styles MUST remain under `Features/BenchmarkComparisons/`, with `scripts/build.mjs` as the thin build entry and Aspire-owned `tests/KeyLoad.SiteTests` as the only qualification runner. Remove superseded assets and tests together with their reviewed replacement; do not keep a legacy implementation for later cleanup.
- Read the design/acceptance/execution contracts in `docs/Features/BenchmarkComparisons.md`, ADR-040/076 and the frozen feature `protocol.md`. Site qualification/publication MUST stay in `benchmarks.yml`; validation without all delivery gates MUST NOT deploy. Successful exact source/run verification precedes archive use; website and measured revisions remain separate. DNS remains outside this task.
- A local isolated static build and real-browser design/graphics inspection are the narrowly specified ADR-040 manual exceptions. They cannot qualify numeric tests, database behavior, coverage, publication or durability.

## Subsequent fresh-evidence publication task
- The owner's subsequent instruction requests a separate post-test Pages publication workflow, recorded in the BenchmarkComparisons publication contract and REQ/AC-BC-028/ADR-040. The earlier design task's no-publication rule remains its scope record; DNS remains excluded from this new task.
- Publication MUST preserve qualified measured-source checkout and independently accurate site/measured/control-workflow SHA fields. Validate-only candidate source MUST NOT deploy. Every figure MUST be computed from the actual authenticated comparison-job JSON; unsupported/missing/failed values remain unavailable.
- The new publication route MUST consume the exact verified archive/raw bytes, complete site/browser/native qualification, a current-run/attempt/artifact freshness check, and least-privilege Pages deployment. Never restore the old independent publisher bypass or treat configuration as a deployment receipt.

## Owner-directed site-only boundary, 2026-10-02
- The latest owner instruction supersedes this task's earlier equal-source restriction above: current trusted-main website source is qualified independently; measured source remains in a separate inspected checkout. Accurately retain website/measured/control SHA and actual successful comparison-job provenance. All site/browser/native/format/governance gates remain mandatory.
- Whole database-workflow success is not this website task's dependency. Use actual successful comparison evidence and accurate link labels even when unrelated jobs fail. Read the revised AC-BC-028 contract before implementation; do not present historical reports as qualification of current database code.

## Owner-directed shared comparison pipeline, 2026-10-03
- ADR-064/076 place all comparative tests in Benchmarks; Pages follows only its complete current producer with exact original identity. Retire active legacy collection, rendering and dependencies while preserving immutable historical evidence as history and every applicable site/source/provenance/qualification gate.

## Owner-directed failed-cell publication, 2026-10-04

- Owner correction 2026-10-04 moves website work to CI: Benchmarks only compares databases and emits authenticated JSON, including unavailable cells. CI automatically consumes that producer JSON for the landing and owns website tests/build/Pages. Preserve every website gate in CI; never run Chrome, browser/site qualification or static-site generation inside Benchmarks or make them dependencies of metrics production. This explicitly supersedes the earlier Benchmarks placement while retaining exactly three workflows.
- ADR-080 permits authenticated same-run failed cells alongside successful competitors. Render failed cells as no data with null numeric fields and actual job links; an entirely unavailable cohort has a null corpus digest. Full TUnit/browser/coverage/source/freshness gates remain mandatory. Original retained GitHub report fixtures are parser/coverage test inputs only, never current publication measurements.

## Latest benchmark and separate build action, 2026-10-04
- The latest owner clarification explicitly permits static website building after benchmark JSON or a separate CI trigger, superseding the interim static-build prohibition above. The selected independent CI action runs on own-main push/manual and completed Benchmarks events, always consumes the newest completed own-main push/manual benchmark and its successful authenticated aggregate, and retains original source/run/attempt, failed/null cells, all website gates and predeploy freshness. Never fall back to older evidence after rejecting the latest completed producer.

## Latest completed result eligibility, 2026-10-04
- Latest benchmark metrics means the newest completed own-main push/manual Benchmarks producer with success/failure conclusion. Pending, skipped and canceled workflows have no completed comparison cohort and MUST NOT displace ready JSON. Exclude them before choosing the latest eligible producer; then reject its missing/corrupt/failed aggregate without older fallback. A canceled workflow_run event still cannot authorize publication. This refines the latest-result rule without accepting incomplete or fabricated measurements.
