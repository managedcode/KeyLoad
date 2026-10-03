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

## R14 preserving successor join

Independent high-capability read-only review found a retained recovery defect:
the unchecked `position + 1` predicate accepts a wrapped negative frame after a
valid long.MaxValue checkpoint. Source proposal and hashes are retained in
/private/tmp/keyload-wal-successor-repair-r14.txt. REQ-STORAGE-017 / AC-WAL-003
already require fail-closed sequence validation; no format/API/serializer change
or new ADR decision is needed. Root approves that exact preserving contract:
last legal successor remains accepted, terminal checkpoint without a frame
remains readable, overflow rejects before decode/apply/truncation/identity
promotion with existing sanitized Corruption. Real-file regression source
precedes the one-condition guard; all runtime qualification is GitHub only.

| Task | AC / owner / model | Permission / dependency / start | Artifact / verification / join |
|---|---|---|---|
| TASK-WAL-SUCCESSOR-REVIEW-R14 | WAL003; atomic_wal_join_review_r14, high capability | Read-only exact recovery/checkpoint/fixture audit | Concrete diff/regression proposal complete; source-demonstrated, unexecuted defect |
| TASK-WAL-SUCCESSOR-TEST-R15 | WAL003; bounded Luna/high coding worker | Only NEW OrleansWalSuccessorTests.cs after accepted refinement; no existing file writes or runtime | Real checkpoint + official binary successor cases; root reviews every assertion and current fixture compatibility |
| TASK-WAL-SUCCESSOR-JOIN-R15 | WAL003/005; root high capability | Sole recovery guard/docs/evidence owner after test review; expected current bytes checked | Integrated build/formatter/static governance then complete new source GitHub unit/process/RF3 gates; pending |

The independent review also records unresolved decoded-memory/work bounds,
frozen legacy/binary format evidence and additional complete-envelope/torn-header
proof. This sequence repair satisfies none of those separate proof gaps and
does not establish speed, power-loss or production readiness. ADR057 stays
Accepted. The owner's full-current-KeyLoad-scope commit request controls the
final main checkpoint; preserve every eligible shared change and never stash.

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

## Exact-source full-gate repair37077856823

The WAL revision6ad4741a713ac3376868aef146ed60a199e97b93 builds and formats in GitHub; normal unit1064/1066 passed; scalar skipped after two unit failures; all136 real-process recovery passed. Comparison2/4 passed: Timescale metadata omitted its actual single-node topology, and a stream test expected generic Conflict while Events.cs explicitly returns RevisionConflict for stale expected revision. Integration62/63 passed; exact TUnit span and node diagnostics identify HTTP503 UnknownWriteOutcome after follower kill during native Orleans directory lookup, before TimeSeries arithmetic. Published TimeSeries10.0.2 is unrelated to this failure and remains outside this WAL checkpoint.

| Task | Requirement/owner/model | Scope/dependency/start | Verification/join |
|---|---|---|---|
| TASK-WAL-CI-METADATA | WAL005; lead high capability | Two exact native topology/error-contract corrections; actual GitHub logs and source contracts reviewed | Keep all other comparison/isolated edits; canonical full comparison gate |
| TASK-WAL-CI-RF3 | WAL005 and existing SERIES012; bounded capable coding worker | Only TimeSeriesRf3ReplicaRestartTests.cs after this accepted preserving contract | Real RF3 test uses existing RetryDuringElectionAsync, one pre-created immutable CommandRequest/ID, only existing UnknownWriteOutcome/OwnershipLost retries, existing deadline and all latest/window/count/survivor/restarted-node assertions; no product retry or timeout change |

The RF3 adjustment tests the already documented caller recovery lifecycle and existing shared helper; it does not alter the application, format, replication guarantees, native Orleans membership, or ManagedCode.TimeSeries. A new ADR is N/A. Every unexpected error still fails; cancellation/deadline remains. Root reviews diff, source size and exact command identity; all runtime verification remains complete GitHub CI. No source/test assertions may be weakened or skipped. Include the already authored exact successor guard/regressions in the final WAL join.

## Native raw-byte codec join after first full gate

First exact source unit failures are AcWal001RealBinaryPayloadUsesFewerEncodedBytesThanEquivalentRepresentativeJson and OversizedCompiledFramePersistsItsFailureAndAdvancesTheRaftApplyPosition. Preserve both assertions and fixture budgets. Exact Orleans10.3.1 source explains the regression: NullableCodec<ReadOnlyMemory<byte>> resolves the constructed generic through CodecProvider to ReadOnlyMemoryCodec<byte>, which writes a tagged field per byte. The generated nonnullable key already uses the static raw codec. Native ReadOnlyMemoryOfByteCodec writes length plus raw bytes and is the correct private WAL registration; no custom serializer, byte-by-byte application loop, copied DTO buffers, or count cap.

ADR057 now accepts explicit closed IFieldCodec<ReadOnlyMemory<byte>> registration to ReadOnlyMemoryOfByteCodec in the cached production provider and independent fixture. This changes nullable nonnull value wire encoding from TagDelimited to LengthPrefixed. Published but unqualified first revision frame2/identity3 is therefore fenced explicitly: final frame3/identity4, complete legacy frame1 OR2 headers refused before truncation; only empty/verified checkpoint2 legacy identity1/2/3 promotes offline. Type alias and field IDs/types/kinds remain. No decoder fallback or mixed-version writes. Previous matching binary must compact each stopped node before upgrade.

| Task | Acceptance/owner/model | Exact permission/start | Verification/join |
|---|---|---|---|
| TASK-WAL-NATIVE-CODEC | WAL001/002/003; codec capable coding worker | Only ZoneTreeJournalCodec.cs; after accepted native-API diagnosis | Closed native raw-byte singleton registration, existing session pooling/full-consumption unchanged; no local tests |
| TASK-WAL-NATIVE-REGRESSIONS | WAL001..004; regression capable coding worker | Only WAL StorageRecovery unit fixtures/tests plus CheckpointTests expectation | Match native closed registration independently; hardcode frame3/identity4, explicit frame2 refusal for identity1..4, identity3 checkpoint promotion, existing1024 size and4096 protocol assertions intact |
| TASK-WAL-NATIVE-FORMAT | WAL004/005; root high capability | Magic/identity/recovery guard and durable docs only; workers do not touch shared source | Fail closed for frames1/2, promote supported1/2/3 checkpoint-only; scoped source review/build/format then complete exact-final-SHA GitHub gate |

The independently reviewed native EnsureAvailable checks bound array count by remaining input bytes and byte-memory lengths before allocation. Serialized MaxFrameBytes is enforced; decoded-memory amplification for deliberately malformed recomputed-checksum frames remains an explicit unqualified capability/evidence gap. Native session reset is correct. This join does not invent a cap, duplicate wire decoding, claim maximum speed, or establish power-loss/endurance.

## Exact SDK cancellation contract join

| Task | Acceptance / owner / model | Permissions / start | Artifacts / verification / join |
|---|---|---|---|
| TASK-WAL-NATIVE-JOIN-R18 | WAL005; atomic_wal_join_review_r14, high capability | Read-only current c10c48e40 run/source/artifacts; private-tmp output only; no source, Git, tests or runtime mutation | Authenticated terminal native jobs, exact report IDs/failures, seeded recovery, scalar/coverage/WAL gaps; root independently reviews before durable evidence or consumer qualification |

Exact c10c48e40 run37079707413 builds/formats successfully; comparison3/4 reaches the next retained regression and fails on KeyLoad:Cancelled in KeyLoadStreamPublicRegression.CaptureCancellationAsync. KeyLoadClient.Send intentionally converts cancelled reads into Result/ErrorCode.Cancelled; KeyLoadClientResults.Success wraps that as ComparisonFailureException. The existing SDK regression already asserts this result contract. TASK-WAL-CI-METADATA preserving scope now includes the exact pre-cancelled read assertion: require the actual KeyLoad:Cancelled adapter code and an actually cancelled caller token; successful completion and all other exceptions/codes still fail. Keep following append/read, original seeded read and exact event identity/revision/payload assertions intact. This is a test-contract correction, no SDK/adapter/product API, topology, retry or deadline change; ADR N/A. Lead owns only the constant/assertion/helper hunk and the final complete GitHub source join; concurrent nodeCount/peer edits remain unstaged.

The lead independently joined the original c10c attempt1 archives against fresh authenticated GitHub run/artifact/job metadata in [the native receipt](docs/implementation/runtime-qualification-37079707413.json): 7 original TUnit publications, 2615 executions, 1409 distinct IDs, one comparison failure; normal/scalar units1088/1088 each, recovery136/136 and RF3 63/63. All19 source WAL methods and every argument expansion match66/66 native cases in each mode, including closed raw-byte codec, unchanged size/protocol assertions and successor refusal before apply. All1000 recovery rows match exact source seeds/stages and atomic cuts. Coverage, decoded-memory/work bounds, independent deployed-binary migration, remaining malformed envelopes, speed, power-loss and endurance remain open. The historical c10c consumer pin and native TimeSeries report are10.0.0; current10.0.3 and isolated270-cell changes require new source qualification.

Completed c10c48e40 source gate: full solution build/formatter/governance and118 analyzer cases pass;1088/1088 normal and1088/1088 scalar units,136/136 real process recovery and63/63 RF3 SDK/MCP pass with0 skips. Comparison3/4 fails only the cancellation contract above; both measurement profiles skip after that failure. This is a failed whole workflow, not complete qualification. The exact preserving cancellation hunks pass independent source review and scoped formatting; dirty comparison build has9 unrelated isolated-feature diagnostics and0 owned cancellation diagnostics. Final revised-source full GitHub gate remains mandatory.


## Closed comparison report matrix join

Exact407619362 run37080578096 reaches and passes the repaired cancellation/following-session flow, then fails retained report cardinality:72 expected versus96 actual. Native comparison-suite artifact11258229884 matches that SHA/run and contains96 distinct target/scenario/repetition tuples,48 measured and48 unsupported,0 failures,12 samples per measured case and577 CSV rows. Six delivered targets and eight defined scenarios produce48 cases per repetition; Supports declarations independently sum to24 measured cases (KeyLoad8,PostgreSQL8,Qdrant1,RabbitMQ1,Redis2,Neo4j4). TASK-WAL-CI-MATRIX, owned by lead under REQ/AC-BC-005 and WAL005, may change only ComparisonTestReportAssertions.cs: named six-target/eight-scenario/24-measured constants, matching total/measured/CSV exact counts, and a closed Cartesian tuple check rejecting duplicates, missing/unknown/out-of-range rows. GraphTraverse3 per repetition, provenance/schema/images/RF3, per-case successes/resources and queue uniqueness/phase assertions remain exact. No runner/adapter/dataset/topology mutation or test skip; this is an existing report-test contract correction, ADR N/A. Complete source CI remains mandatory after all currently known repairs.


## Native Neo4j fixture credential join

Exact source07ca0e807 run37081539615 passes build/format/governance, units/scalar and process recovery; the comparison report matrix and following-session regression pass, but the final genuine Neo4j mismatch fixture fails with InvalidQueryResponse during seed restoration. The first report or setup failure is hidden by await-using cleanup. Actual Aspire13.6.0 AddParameter(name,string) source captures the supplied value directly and does not read Parameters configuration; BenchmarkResources supplies its own random secret, while ComparisonTests incorrectly retains a different command-line password. The measured process correctly uses the actual resource; the independent private fixture must resolve that same ParameterResource.GetValueAsync(token), as existing AC-GH-002/005/006 and ADR049 explicitly require.

TASK-WAL-CI-NEO4J under WAL005 and REQ-BC-023/AC-GH-002/005/006 grants the bounded gate worker only ComparisonTests.cs and Neo4jHarnessMismatchObserver/Regression.cs: remove the ineffective private password override, resolve the actual secret in memory, preserve code-only native errors before mandatory HTTP202 validation, dispose every returned mutation document, and preserve original plus all owned cleanup failures through the existing collector. Existing native runner/oracles/sample/CSV/restoration assertions remain exact; no HTTP200 acceptance, query/topology/provider mutation or test skip. ADR N/A: this fulfills the accepted real-resource/lifetime contract, not a new product contract. Root reviews exact source and every error/lifetime boundary, builds/formats the comparison project, and complete current-source GitHub CI remains required.

Latest07 RF3 has three genuine failures (RecoveryRequired, restarted node failed-start, bounded operation success assertion); these are tracked separately for exact native diagnostics and the concurrently delivered topology fixes, not silently accepted as green or retried indiscriminately.


## Native initial-RPC cancellation join

Exact07 RF3 native artifact11258952802 reports60/63 passes. Snapshot test failed00:23:30–00:23:48UTC after killing node1: node2 diagnostic00:23:43.359 labels CredentialDispatch/OperationCanceled with no operation ID, while middleware's generic RecoveryRequired proves the incoming caller token was inactive. DatabaseCredentialResolver delegates only to the initial OrleansNode request RPC; OrleansRpcFailure currently excludes OperationCanceledException. Classify this boundary failure according to trusted intent and retain caller cancellation precedence, rather than retrying RecoveryRequired or implying WAL damage.

TASK-WAL-CI-RPC-CANCEL under REQ/AC-ROUTE-009/010 and WAL005 owns only OrleansRpcFailure.cs plus its existing real-exception UnitTests: add a closed Cancellation category for OperationCanceledException/TaskCanceledException, retain first callerToken.ThrowIfCancellationRequested, readOwnershipLost/writeUnknownWriteOutcome, every domain-error exclusion, no retry and no private exception logging. Existing native-failure/privacy/read/write/caller-cancel tests cover the new classes; author the failing inputs before implementation. ADR036 accepts this preserving initial-RPC extension before source edits. TASK-WAL-CI-SERIES-DIAG owns only named diagnostic Because reasons on the existing latest/windows success assertions; actual safe problem code/detail must be retained before any retry change. All sample/value/window/count and original deadlines remain exact. Native leader-loss logs show Docker running with stale exit137 state; the separate63ac WaitOnResourceUnavailable repair already addresses that fixture and is preserved.

Root source/branch/lifetime/privacy review, normal enabled Release builds, scoped formatter/static governance and complete next-source GitHub unit/scalar/process/RF3 are required; no local runtime or substitute result. Neo4j's reviewed3-file prerequisite patch was applied by root after confirming actual owning mandatory-gate scope; delegated auto-review rejection was resolved without a child retry.


Final prerequisite source review keeps all RPC outcomes/privacy, native Neo4j mismatch/restore/report assertions and original RF3 budgets. The enabled comparison Release build passes0warnings/0errors after sole-client ownership transfer and layered await-using scopes; the six owned files pass required formatter verification, and static governance/diff checks pass. The full dirty solution's Unit/Integration/Comparison projects build; final solution result fails only two concurrent SiteIsolatedGitHubArchiveAuthorityTests MatchesBytes diagnostics outside this scope. No local tests ran, and this is not full runtime qualification. The nine-file stable WAL gate checkpoint excludes every concurrent Site/isolated-comparison file; complete new-source GitHub build/unit/scalar/recovery/RF3 remains required, with the Series safe response diagnostic retained for any repeated failure.


## Mechanical Site build prerequisite

Exactcfe8c1608/run37083535329 enabled GitHub build fails only SiteIsolatedGitHubArchiveReceipt.cs IDE0032/IDE0290; native analyzer118 gate passes and RF3 runs independently. TASK-WAL-CI-STYLE under WAL005 changes only that committed receipt class to the required primary constructor/get-only backing properties, preserving constructor parameter order, exact DeepClone/copy-on-access and Matches behavior. Root prepares an exactHEAD index patch so the active owner's different receipt/authority/MatchesBytes work stays visible and unstaged. No behavior/API/security/test assertion changes, new runtime tests or ADR: N/A for mechanical style. Source/formatter/governance review and full next-source GitHub gate remain mandatory; skipped unit/recovery/comparison after failed build are not passing evidence.
