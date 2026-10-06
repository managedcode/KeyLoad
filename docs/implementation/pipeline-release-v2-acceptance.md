# Pipeline and release acceptance

Goal: Build and Tests, Benchmarks, Website and Release appear in Actions. Build/tests are together;
complete measured load/comparison JSON drives the downstream site, and a release
build delivers the real database and immutable dated release tag.

Scope: workflow orchestration, authenticated source/provenance adapters, release
version/distribution tooling and focused TUnit regressions. No changes to database
storage, workloads, native topology, DNS, unrelated dirty source or NuGet feed
publication. Existing package/image artifacts become real versioned release assets.
Actors: PR contributors use read-only CI; own-main push/manual execution measures
Benchmarks; a manual own-main Release can reserve/build, with write privileges only
at publication. UTC is the daily counter boundary; configured source major/minor
remain owner controlled. All qualification executes in Linux GitHub Actions.

Owner correction 2026-10-03: the product is not ready for packaging. Current scope
is the prepared pipeline implementation and build/test verification. Actual Release
dispatch, package/image builds, tag creation and provider publication are deferred
until a later explicit release/readiness instruction. Retain the future release
requirements and report their provider qualification as deferred, never passing.

- AC-PIPE-001 / REQ-PIPE-001: exactly ci.yml, benchmarks.yml, website.yml and release.yml remain;
  names are Build and Tests, Benchmarks, Website and Release. Build and Tests runs push(main), pull_request and manual;
  preserve full build/format/rules/analyzer/unit/scalar/recovery/real SDK/MCP RF3.
  Failure is failure; no omitted suite or separate governance/Tests workflow.
- AC-PIPE-002 / REQ-PIPE-002: Benchmarks retains every native image/preflight/CRUD/
  specialized/aggregate and TimeSeries suite with all isolated Linux cells, real
  topology, workload/acknowledgement contracts and source-bound artifacts. The
  qualify job needs successful aggregate and TimeSeries image; deploy needs full
  successful site qualification. Retain browser/TUnit/native coverage and freshness.
- AC-PIPE-003 / REQ-PIPE-003: publish authenticates this exact own-main Benchmarks
  run/attempt/SHA (push or manual), complete jobs/artifacts and rechecks it before
  deployment. A newer history entry, prior successful attempt, wrong SHA, failed
  current aggregate, incomplete/expired artifact or old Website executor cannot
  substitute. Historical validation pins remain distinct and are never publication.
  New CI inventory entries are authenticated nonproducers; chosen historical
  KeyLoad CI identity, report bytes, hashes and completeness stay exact.
- AC-REL-001 / REQ-REL-001: source configuration owns major/minor. Release is manual
  own-main, globally serialized and freezes its UTC date/version/source/run in an
  immutable reservation artifact. Tag is vM.m.yyMMdd.N, N >= 1; use existing dated
  tag counter and actual daily release-run ordinal to avoid reusing failed slots.
  Same-run retries recover the reservation; malformed/future/stolen reservations,
  source/ref mismatch, exhausted CLR counter or an existing tag at another SHA fail.
- AC-REL-002 / REQ-REL-002: build full solution with one release identity, verify
  format/rules, pack actual NuGet outputs, publish self-contained Linux database
  distribution and build/export real version-labelled server image. Keep RF3 Compose
  deployment and node-local persistent storage; no demo or bundled local user data.
  Check nonempty files, embedded package versions, hashes, image digest/labels and
  source/run provenance before publication. CLR version mapping remains bounded;
  package/informational/image/git identities retain the complete owner version.
  Preserve the source's `dev` package stage: NuGet versions append `-dev` to the
  same four numeric components; git tags and image versions retain the exact
  requested numeric format. Inspect this derived package version in reservation,
  nuspecs and manifest. Never suppress NU5104 or hide alpha dependencies.
- AC-REL-003 / REQ-REL-003: final publication authenticates successful exact-source
  CI and owned build artifacts, pushes immutable versioned GHCR image(s), creates
  source-bound annotated git tag and GitHub Release with real database/package/
  image assets and checksum manifest. Idempotent retries verify/reuse their own
  results; reject conflicting tags/images/assets without force or overwrite. Do
  not publish partial/failed packages or claim database readiness from packaging.
- AC-PIPE-004 / REQ-PIPE-004: update policy/feature/architecture/ADR and real TUnit
  role/version/provenance regressions; scoped main commit/push preserves unrelated
  work. Retain exact GitHub source/run/job/artifact outcomes and actual blockers.

| Acceptance | Automated qualification and review |
|---|---|
| PIPE-001/002 | TUnit source-role/graph assertions; actual GitHub job inventory and preserved native job diff |
| PIPE-003 | TUnit positive/negative real production JS adapters and authenticated current-run image/aggregate route; full SiteTests require authentic complete archives |
| REL-001 | TUnit runs real version tooling: UTC/date rollover, first/incremented/different-base counters, malformed/collision/exhaustion/retry/source cases |
| REL-002 | Actual GitHub Release build/pack/publish/Docker outputs, nuspec/hash/label inspection; source-contract TUnit assertions |
| REL-003 | Authenticated exact-source CI, GitHub tag/release/GHCR digest/asset verification; conflicting publication rejects; manual external-publication review where current source is unqualified |
| PIPE-004 | Static syntax/governance/scoped diff plus source-bound GitHub receipt |

Full existing CI failure is not a passing qualification. Missing complete native/
legacy website evidence blocks site tests/publication and must be reported. Tests
of release metadata use controlled data, never fabricated benchmark measurements.
Rollback reverts scoped workflow/adapters/tooling; published immutable tags/assets
are never removed or moved. No newly installed tools/skills or consumer workarounds.

Owner correction 2026-10-06: ADR-112 / AC-BC-WEB-006 moves complete website qualification/publication to standalone Website and removes its benchmark trigger/jobs from Build and Tests. The prepared Release source-success validator uses the current Build and Tests name while retaining the ci.yml path, mandatory job identities and exact-source success/permission gates. Historical CI measurement identities remain unchanged. Product Release stays manual and is not dispatched by this correction.
