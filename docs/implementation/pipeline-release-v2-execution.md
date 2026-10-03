# Three-pipeline release plan

Inputs: [brainstorm](pipeline-release-v2-brainstorm.md) and [acceptance](pipeline-release-v2-acceptance.md);
ADR-064. Lead approves the bounded implementation contract under the owner's explicit
three-pipeline/public-release correction; existing required gates remain mandatory.

| Task | AC | Owner | Writes | Dependencies/start | Verification/join |
|---|---|---|---|---|---|
| TASK-PIPE-AUDIT | PIPE-003 | read-only audit | none | owner scope | exact guards/routes, completed audit |
| TASK-PIPE-SITE | PIPE-003 | capable audit worker | named site-isolated-github and github-evidence-runs modules only | approved contract/audit | node syntax, bounded provenance cases, lead full diff review |
| TASK-REL-TOOLS | REL-001/002/003 | capable script worker | scripts/Features/ReleaseDelivery helpers only | approved feature/ADR | syntax, actual source outputs through GitHub; no local tests |
| TASK-PIPE-TESTS | PIPE-001/002/004, REL-001/002 | capable C# worker | named workflow tests and new Features/ReleaseDelivery TUnit files | approved contracts | compiled unit/source/version cases in CI; no local tests |
| TASK-PIPE-LEAD | all | lead | workflows, central version, policies/docs, RF3 release distribution, source closure, delivery | serialized integration | inspect all diffs, static checks, scoped commit/push, GitHub evidence |
| TASK-REL-PACK-STAGE | REL-001/002 | script worker | version/CLI/context/asset helpers only | real run37116935776 NU5104; amended package-stage contract | freeze derived -dev package version; no suppression or local tests; lead owns workflow |
| TASK-REL-PACK-TEST | REL-001/002 | C# worker | ReleaseDelivery TUnit and role tests only | amended package-stage contract and agreed schema | real tool positive/tamper/retry assertions; GitHub CI |

- [x] Read policies/architecture; record exact owner correction first.
- [x] Read-only parallel provenance/release discovery and explicit safe write scopes.
- [x] Define brainstorm, detailed AC/test matrix and ADR contract before writes.
- [x] Join audit; move ordinary Tests into CI, remove standalone Tests/Website.
- [x] Join exact-current-run site and legacy nonproducer adapters; append full qualify/
  deploy chain behind aggregate/image success without reducing site gates.
- [x] Configure canonical source major/minor and release reservation/build/delivery;
  integrate bounded version/manifest helpers and real RF3 distribution/image assets.
- [x] Join worker TUnit/source/version regressions; inspect every diff and count all
  preserved native cells/suites. No worker commits or pushes.
- [ ] Static syntax/governance/whitespace on exact scoped source, then commit/push main.
- [ ] Inspect exact-SHA CI/Benchmarks and dispatch Release build when concrete;
  download actual package/image/distribution artifacts. Final publication remains
  gated by genuine successful exact-source CI; no duplicated full qualification.
- [ ] Verify actual tag/GitHub Release/GHCR only when all required gates pass; retain
  exact blockers otherwise and never claim packaging or configuration is release.
- [ ] Repair stable-package NU5104 from alpha-only Cartograph by preserving the
  source's `dev` package stage with the same numeric identity; verify reservation,
  actual nuspecs and checksums in GitHub without altering the exact git tag format.

Owner correction: actual packaging/publication is now deferred because the product
is unfinished. Do not dispatch another Release. Version/package-stage helpers,
workflow and TUnit checks remain prepared; future package/image/provider checks
above remain unchecked until a later explicit release/readiness request. All prior
Release runs are already completed; no active Release needs cancellation.

Baseline: earlier milestone CI/release build passes, all four layout and183 recovery
cases pass. Full units fail one existing SDK-status assertion; RF3 fails one SQL
admission assertion; Timescale resource-model tag assertions prevent complete
native cohort. Existing missing authenticated complete archives/native coverage
block site qualification/publication. These source fixes are unrelated shared work.
No local tests, stashes, force-push, policy weakening or dependency replacements.

Joined static review: all original eight benchmark job objects and all ordinary
test job commands are preserved; exact three workflow YAMLs and composite shell
syntax pass. Current shared governance validates26 projects; the scoped delivered
source will be checked independently because unrelated diagnostics remain uncommitted.

Delivered checkpoints: e050b5e3213ccc007622092587c35fd0a0bbf378 contains the three
pipelines; e089f3ccc77460c0fc6610581025b57651eac96a fixes the new test compiler
errors and authenticates the latest exact-source CI. GitHub reports exactly three
active workflows (CI372183043, Benchmarks373808964, Release373808965).

Exact e089 verification: [Release37116935776](https://github.com/managedcode/KeyLoad/actions/runs/37116935776)
reserved `v0.1.261003.3` in immutable artifact11271662391
(`sha256:5c1f33584b6784c4418b70e6ca5edcdac995a25f781e39d64ab503174ce376e7`).
Job111185484820 passed complete solution build (zero warnings/errors), formatter
and governance, then failed NU5104: third-party Cartograph/Catalog0.1.0-alpha cannot
be dependencies of stable NuGet packages. Publication was skipped. No stable
Cartograph version exists in its authenticated feed inventory; upstream publishing
access is unavailable. The bounded repair preserves the existing source `dev` stage
in NuGet only, freezes schema2/packageVersion and validates actual nuspecs/manifest;
the requested numeric tag/image format is unchanged. Static syntax, all65 workflow
bash blocks and26-project governance pass; real packaging remains to be rerun.

[Benchmarks37116893060](https://github.com/managedcode/KeyLoad/actions/runs/37116893060)
passed full build/format/rules, image round-trip1/1, current-job identity1/1 and real
pinned Timescale image1/1. Native model job111186389331 failed4 of98 existing cases
because the image tag is represented in the pinned image string, while tests read
the empty separate Tag field. All remaining cells, aggregate and site publication
were correctly skipped. The separately delivered c2415755d source contains that
assertion repair; it has not been called qualified here. e089 CI37116893088 is still
running at this checkpoint; no full CI/release/site success is claimed.
