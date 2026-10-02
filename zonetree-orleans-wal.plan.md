# Atomic WAL implementation plan

Chosen [brainstorm](zonetree-orleans-wal.brainstorm.md) and [acceptance](zonetree-orleans-wal.acceptance.md) govern scope. Integration owner accepts the bounded implementation contract in ADR-057 under the owner's requested serializer change; required fault qualification remains pending. Do not expand into replication/checkpoint value re-encoding or concurrent comparison work.

## Ordered work and task graph

| Task | AC | Owner/model | Permission/start | Artifact/join |
|---|---|---|---|---|
|TASK-WAL-001|001-005|lead, high capability|read-only then scoped docs|Accepted ADR057, feature/upgrade contract before implementation|
|TASK-WAL-002|001-003|codec worker, capable coding|private new codec/DTO/buffer files after001|generated codec + bounded/full-consumption checks; lead reviews before integration|
|TASK-WAL-003|001-004|lead integration|existing storage files and central package/project refs only, after001|journaling/budget/identity/initializer joins; no other writer to these files|
|TASK-WAL-004|001-004|regression worker, capable coding|StorageRecovery unit files only after001|real-store/format regression tests mapped to acceptance; lead joins codec API|
|TASK-WAL-005|001-005|lead + read-only reviewers|after002/003/004 join|build/analyze/format/governance + final exact-SHA GitHub gates|
|TASK-WAL-GATE-REPAIR|005|bounded coding worker|five exact concurrent comparison compile diagnostics only|preserve concurrent topology code; fix Mongo property name/nesting/typed catch/static SQL/expected CLI configuration catch, no model/storage/measurement contracts|

All workers preserve unrelated work, use actual ZoneTree/Orleans, never run local tests, never change shared contracts or suppress analyzers. Escalate public format/ownership changes and unsafe corruption handling. Filesystem edits outside current workspace require tool sandbox approval; no source repository workaround.

- [x] Read policies, architecture and exact storage paths; identify native bytes vs atomic JSON.
- [x] Define requirements, acceptance, task ownership and ADR before implementation.
- [x] Full baseline: inspect running main37074392471 terminal unit/recovery/RF3 results; record each real failure with root cause/fix path. Previous37073331174 failed image preflight (unrelated comparison work); preserve its evidence.
- [x] Write/update acceptance-based real-store regressions together with codec implementation.
- [x] Integrate generated binary DTO/codecs and exact binary output bound; remove JSON-specific accounting.
- [x] Integrate frame/identity version guard, full validation before apply and offline checkpoint-only upgrade.
- [x] Review all worker diffs, cache/stage/poison/disposal/migration/error behavior.
- [ ] Development validation: restore, Release solution build/analyzers; required format check; git diff --check; static node governance. No local tests or benchmarks.
- [ ] Deliver only relevant stable KeyLoad changes under existing main authorization; preserve unrelated files/hunks and never stash/force-push/bypass gates.
- [ ] Qualify exact pushed SHA in GitHub: complete unit, real-process recovery and Docker/Aspire RF3 SDK+MCP; fix actual failures and repeat owning gate. No skipped/pending jobs qualify.
- [ ] Record actual final SHA/run/job summaries and gaps. Never mark completed with failed/pending gates or unmeasured speed/unsupported durability.

## Verification methodology and existing failures

The codec worker follows installed Orleans serialization/versioning guidance; lead reviews actual generated codecs, stable aliases and full-consumption boundaries. Real-store tests establish write/reopen and reject-before-durable behavior. Existing CrashHost tests exercise actual process cuts; RF3 SDK/MCP suites retain multi-node authority. Coverage collector status and numeric thresholds must be reported honestly. Format/analysis/governance are separate source gates; tests stay in GitHub.

- [ ] Baseline image preflight failure37073331174: existing comparison topology workflow repair is already in concurrent working tree; outside WAL ownership. Inspect updated37074392471 result before attributing it to this work.

Baseline test failures are recorded below. External blockers do not become permission to weaken acceptance or fabricate evidence.

Full development build observed four concurrent BenchmarkComparisons blockers: MongoReplicaProof.cs19 CS1061 ReplicaSet property, ComparisonValidation.cs53 KLD0033 nesting4, OpenSearchHttp.cs57 KLD0024 untypedcatch, PostgresTopology.cs60 CA2100 commandtext. TASK-WAL-GATE-REPAIR owns only those exact edits to unblock integrated source validation; no comparison feature delivery or unrelated commits are authorized. Baseline main37074392471 terminated failure: AC_IMAGE_002_MetadataRequiresAValidConfigIdAndExactSourceRevision (KeyNotFoundException atImageToolingContractTests.cs196; 1021/1022unit passes no skips) plus image preparation/cleanup command failure beforeRF3/comparison tests. The existing minimal image-contract/test corrections and owner-selected Linux-only matrix are delivery prerequisites for the complete canonical gate; preserve every other concurrent comparison change.

Final development source check: restore and static governance pass; all final WAL source/test files have no compiler/analyzer diagnostics. The full dirty-tree build fails on111 concurrent BenchmarkComparisons diagnostics, including two not-yet-added native resource types. Required full dirty-tree formatting likewise includes concurrent benchmark diagnostics. This is not a green full-solution qualification. Reviewers completed the codec contract and all44 new parameterized regression cases without running local tests; exact committed source will be built, formatted and tested by canonical GitHub CI.
