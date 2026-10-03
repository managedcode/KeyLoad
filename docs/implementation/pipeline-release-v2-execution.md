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
