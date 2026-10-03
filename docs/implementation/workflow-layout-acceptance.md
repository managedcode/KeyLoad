# Workflow layout acceptance

Goal: GitHub Actions lists CI, Tests, Benchmarks, Release and Website.
Owner clarification2026-10-03 requires separate PR checks, project tests,
performance tests and package builds; the performance pipeline is Benchmarks.
Scope is infrastructure, exact producer provenance and focused regressions.
No database, workload, topology, website design or unrelated dirty work changes.

Actors: trusted own-main push/manual workflows, PR contributors and Pages.
Pinned actions, least privileges, Linux qualification and real TUnit/MTP/SDK/MCP
boundaries remain. Release builds and retains packages; no feed credentials or
public NuGet publication is presumed from the request to build packages.

- AC-WF-001 / REQ-WF-001: exactly ci.yml, tests.yml, benchmarks.yml, release.yml
  and pages.yml exist at top level with names CI, Tests, Benchmarks, Release,
  Website. CI is PR/manual checks: repository rules, full Release/formatter and
  analyzer regression checks. Tests is main/manual project qualification:
  full Release/format/rules/analyzer, unit/scalar, process recovery and genuine
  Docker/Aspire RF3. Repository rules are inside those pipelines, never standalone.
- AC-WF-002 / REQ-WF-002: Benchmarks is main/manual performance qualification;
  it preserves every original native image/preflight/CRUD/specialized/aggregate
  job, 27/108/162 isolated Linux cells, real topology, workloads and artifacts.
  TimeSeries pinned-image tests join the same pipeline. A full build/format/rules
  prerequisite gates native jobs. No skips/fakes/merged native runners count.
- AC-WF-003 / REQ-WF-003: new isolated cohorts and TimeSeries receipts bind to
  Benchmarks and .github/workflows/benchmarks.yml with exact repo/ref/path/job
  checks. Wrong producer identity is rejected. Historical legacy main-push
  comparison-suite remains KeyLoad CI/ci.yml and is never rewritten; current CI
  metadata is explicitly renamed CI while historical run checks remain exact.
  New CI has no push trigger, so its PR/manual runs cannot enter legacy push history.
- AC-WF-004 / REQ-WF-004: Website watches Benchmarks completion and site pushes
  plus manual dispatch. The new executor identity is Website; isolated measured
  producer checkout/copy uses benchmarks.yml, legacy measured source retains ci.yml.
  All exact artifact/source checks, freshness, coverage and site gates remain;
  incomplete producers cannot refresh published metrics.
- AC-WF-005 / REQ-WF-005: Release supports v* tag/manual package builds from
  centrally versioned source, full Release build/format/rules and dotnet pack.
  Retain actual non-empty .nupkg outputs with exact source artifact metadata.
  Do not add credentials, publish a NuGet feed or fabricate release qualification.
- AC-WF-006 / REQ-WF-006: owner policy, feature/architecture/ADR records and
  focused TUnit provenance/layout regressions match all five workflows. Static
  checks and exact-SHA GitHub job/artifact proof are retained; existing baseline
  failures remain explicitly distinct. Scoped main commit/push preserves dirty work.

| Criteria | Verification |
|---|---|
| 001/002/005 | TUnit source-contract tests and actual workflow/job inventory; actual Release package artifact |
| 003 | Existing isolated provenance positive/negative tests; legacy current-metadata/historical-run regressions; real native image context |
| 004 | Existing SiteTests and authenticated source/producer/executor checks |
| 006 | Full scoped diff/static inventory and exact-SHA GitHub job/artifact records |

Qualification runs only in GitHub Actions. Static source/YAML/syntax/development
build checks are not runtime qualification. Pipeline UI names have explicit
real GitHub inventory review instead of a separate UI test. Rollback reverts the
scoped infrastructure commit and never rewrites immutable historical archives.
