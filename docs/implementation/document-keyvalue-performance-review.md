# Document and key-value execution review

Review started 2026-11-06 against `fde02c1a5`. Scope: document insert, replacement,
deletion, point reads and indexed ranges, including the SDK, HTTP admission,
Orleans routing, physical ownership, authorization, native storage and replication.
Key-value currently uses the same document path without secondary indexes.
MongoDB and Redis are the existing comparison drivers; no benchmark contract or
dataset change is part of this review.

Status: the first repair batch passed local qualification. Its Linux run failed the
canonical test inventory because nine added admission cases were absent; release
and comparisons did not run. The partition coordinator redesign and its local
qualification are complete at the scopes recorded below. Exact-source delivery
is established by the separate Linux Build and Tests, Release and Benchmarks
artifacts. Findings distinguish source observations,
runtime evidence, completed repairs and remaining qualification. This document
does not claim a whole-repository audit or product readiness.

## Active implementation and validation plan

The expanded scope includes update, SQL/AST queries, full-text/vector/graph search
and their common admission, cancellation, physical publication and replica paths.
The invariant is one ordered command prefix per logical atomic partition, fresh
persisted policy at the actual operation cut, exact-effect copies and honest
all-copy acknowledgements. Native MVCC timestamps identify physical snapshots;
they are neither dense counters nor comparable across physical stores or cold
openings. The shared-volume physical owner and joined borrowers remain mandatory.

1. Complete the independent partition coordinator and exact-effect copy protocol.
   Prove causal ordering between catalog changes and earlier partition data;
   preserve interrupted-copy completion, replay, cancellation and shutdown.
2. Replace old global command-position tests and bookkeeping with the current
   partition prefix contract. Preserve complete byte images and exact outcomes;
   measure a native publication with actual publication events, not timestamp + 1.
3. Profile the existing real SDK diagnostic before selecting further common-path
   optimizations. Retain source, DLL/PDB, process, topology, payload, phase and
   collector identity under the executing test assembly. Inspect CPU stacks and
   allocations separately. A capture is diagnostic evidence, not a speed claim.
4. Audit update/search execution, including repeated principal/admission reads
   and unused per-step clock work. Keep fresh authorization, native snapshots,
   cancellation and resource-lifetime checks. Replace exclusive obsolete test
   machinery together with any removed runtime contract.
5. Review the complete combined implementation. Run restore, formatting and
   verification, Debug solution build with warnings as errors, governance, focused
   real functional/process/Aspire SDK/MCP tests and compiled test discovery.
   Reconcile the canonical inventory from that actual discovery.
6. Commit and push the qualified checkpoint on main. Follow Linux functional
   qualification, release publication and unchanged official comparison cells
   for document/key-value/update/search workloads. Retain original artifacts;
   compare only matched released measurements and disclose remaining gates.

Validation skills, in order: Orleans for identity/scheduling/lifecycle review;
analyzing-dotnet-performance for source findings without speculative gains;
dotnet-diagnostics-profiling plus profiling for actual process evidence; format
for repository style; Aspire for test-owned cluster lifecycle. Root Debug and
test artifact rules take precedence over generic profiling preferences.

The official installed tracing command is
`dotnet-trace collect --process-id PID --profile dotnet-common,dotnet-sampled-thread-time --duration 00:00:08 --buffersize 64 -o TRACE.nettrace`.
`PID` is the test-owned running KeyLoad server; `TRACE` stays under that test's
`bin/Debug/net10.0` output. Use the same case, phase and collector settings before
and after. Keep uninstrumented timing runs separate to assess collector overhead.

The latest completed full Debug solution build (`build-current-contracts-35`)
passed all 20 projects with zero warnings or errors. The earlier 26 actual-work
cancellation, fairness, reservation and shutdown controls passed, and the final
123-case ANN family repeat passed, including retained later-child cancellation
and healthy resume. These controls observe actual storage
reads, retained replay work, native frame writes or the request activation's
scheduler; none relies on a discarded clock callback. Genuine signed-page
expiry retains its five-minute lifetime.

The build-18 real replication campaign passed 17/17 cases with zero unobserved
task notices. It covers local DI and remote Grain Service execution, exact-effect
replay, compact queue-delivery payloads, held-copy isolation and causal
catalog/foreign-vertex ordering. Earlier document checks passed 68/68 and BM25
public/private admission passed 2/2. These results cover their stated builds and
operations; complete current-source execution remains required.

Recovery oracles now use the actual partition prefix and the native stamp of
the physical opening under test. They retain complete byte images, original
command outcomes, exact same-opening no-write checks and healthy continuation.
Native timestamps are not counted or compared across independent stores.
The old maintained-search tests composed only a lower selected projection;
they now use actual `TextHostSearch` composition, captured reads and joined
owners. Its wider consumers still require execution.

A prior broader campaign reported 90 unobserved
`ObjectDisposedException` tasks. A separate earlier diagnostic run with
`--fail-on-unobserved-task-exceptions` retained those notifications as failures.
The exact message is constructed by Orleans 10.4.2's
`AsyncEnumerable<T>.Element.CreateDisposed`: its terminal next-link task is
faulted without a consumer. This matches the existing
[Orleans issue 11660](https://github.com/dotnet/orleans/issues/11660); its
[correction PR 11661](https://github.com/dotnet/orleans/pull/11661) remains open
as checked on 2026-11-06. No dependency replacement, exception suppression,
public comment or local package override was added. First-chance network
exceptions from an EventPipe trace are distinct from these never-thrown terminal
task exceptions and do not establish the cause by themselves.
An external diagnostic startup hook recorded the actual unobserved task types:
`AsyncEnumerable<ClusterMembershipSnapshot>.Element`,
`AsyncEnumerable<MembershipTableSnapshot>.Element` and
`AsyncEnumerable<DirectoryMembershipSnapshot>.Element`. The one selected real
copy-acknowledgement case passed and produced three such terminal-task notices;
the hook did not call `SetObserved` or alter runtime behavior.

The normal CI command does not turn these upstream notifications into additional
test identities. No test-runner gate was removed or exception marked observed.
The existing-store fault-observer prohibition also remains unchanged. Complete
functional, process recovery, Aspire SDK/MCP, coverage, Linux delivery and
released comparison qualification remain open.

The current compiled roster contains 3,094 cases: 2,661 functional contributors
and 433 required controls, with no skipped or excluded cases. The redundant
clock-held maintenance test and its exclusive helper are removed; its memory
accounting assertions and requirement labels are merged into the existing real
owner reservation control. A cold session-authority case now also checks a
metadata prefix stored under a data-partition key. All four damage variants
passed on build 29. The original six-run discovery workflow passed all 3,094
cases and its 533/520/551/491/566-case selectors on unchanged build-34 images.
Discovery is not execution or functional coverage.

The complete build-29 functional campaign finished with 3,089 passes, five
failures and no skips in 19 minutes 15 seconds. Two source-manifest controls ran
while final build/inventory inputs were still changing; a stable-input repeat
of nine manifest/HTTP telemetry controls passed. The isolated repeat also passed
the extra-server-span assertion that failed once in the full campaign; no
product telemetry change or reduced assertion was made. Safe trace identifiers
are added to that assertion's failure message for any further reproduction.
Two time-series cancellation controls needed repair: a remaining discarded clock
callback and a polling observer that could miss a short synchronous read. They
now observe actual native point-read callbacks while preserving the original
provider, bytes, work charges, public rollup operation and caller token. Both
passed on build 33.

The complete build-33 functional campaign used the canonical 27 namespace
processes in four groups, with all build inputs and Git HEAD held unchanged.
It finished with 3,092 passes, two failures and no skips across 3,094 cases.
Storage and clients passed 582/582; execution and governance passed 282/282.
One retained ANN later-child cancellation could finish before its polling
observer cancelled it. Its current repair creates a substantial real projection
history while preserving the three literal final vectors and the original
cancellation, pending-intent and no-write assertions. The complete Search repeat
on build 34 passed 390/391; that case passed its cancellation and pending-state
checks but its healthy resume exceeded the tiny helper's 12 one-entry pages.
The helper now accepts the actual bounded page size for this case; the 12-page
limit and independent final vector oracles are unchanged. Build 35 and the
123-case ANN family repeat passed, including this exact case.

The other failure was durable-job shard settlement after the preceding Messaging
cases. The unchanged restart case passed alone, and the complete build-34 Messaging
repeat passed all 391 cases without skips. The earlier original logs show new jobs being
scheduled after restart into one shard without their execution starting. Published
`Microsoft.Orleans.DurableJobs` 10.4.2-alpha.1 identifies source
`876ffe0338e41bc6339e85ee5696dbee6cf9a4c4`: its `CreateShardAsync` can replace a
shard instance already opened by discovery. The sibling Orleans repository already
contains a correction in `d0a51a5c701274cf870df91306ab68430a7e7ed6` that uses
`GetOrOpenShardAsync` instead. This matches a possible cause, not yet a controlled
reproduction of this exact failure. No published package containing that correction
has been established, and no local binary override or reduced assertion is added.

The complete build-34 actual-process campaign passed all 154 cases without skips
in 4 minutes 29 seconds. It includes the five remaining roots from build 33:
native snapshot stamps in window read cuts, a scoped inbox prefix saved at
preparation, and a native baseline captured after the actual shared-domain reopen.
Cold comparisons normalize only the physical stamp and its corresponding
read-cut field; same-opening no-write checks remain exact. Complete row images,
original outcomes, literal sample arrays, revision counts, replay and healthy
work remain asserted. This is process-kill recovery evidence, not power-loss or
deployed filesystem qualification.

Earlier red and interrupted campaigns are retained under the executing test
assembly's `performance-review-20261106` output, including the interrupted
build-21 run (2,959 passes, 17 failures). They do not qualify this source.

## Coordinator redesign findings and current implementation

| Finding | Current change | Qualification |
| --- | --- | --- |
| One database queue serialized copy waits for unrelated partitions. | Each atomic partition has one ordinary Orleans command owner and one queue; metadata has its own owner. Local primary work borrows the original physical session through DI; remote primary work and copies use the typed silo Grain Service. | Real two-host test keeps another partition writable while a copy repair is held. Linux qualification remains open. |
| A synthetic physical-position record made independent native commits write the same key. | Physical snapshots use native MVCC timestamps. Canonical command prefixes and business clocks belong to their actual partition. Superseded global keys and last-command bookkeeping are removed. | Native grouping, documents and copy tests passed; broader current-contract tests are in progress. |
| Replicas repeated authorization and business decisions on different native cuts. | The primary retains exact decided mutations and outcomes atomically. Copies publish those effects, with predecessor checks and all-copy acknowledgements. | Interrupted RPC, incorrect acknowledgements, replay, lost reply body and healthy continuation passed locally. |
| Per-partition order alone allowed a catalog deletion to overtake earlier data at a copy; graph commands also read foreign-partition vertices. | The retained batch includes catalog dependencies and the actually observed foreign resource partitions; catalog decisions cover prior partition prefixes. A copy defers before effects, waits for only the required prefixes outside the native transaction, then revalidates at publication. | Catalog and foreign-vertex ordered-delivery controls passed in the 17-case real replication run. Broader lifecycle qualification remains open. |
| Shared-volume execution forwarded synchronous work through its one file-owner invocation. | One ordinary owner still acquires the files; independent request contexts borrow its joined physical session. The separate shared-volume coordinator and forwarding operation are removed. | The combined source builds; shared-volume lifetime and cluster checks remain required. |
| Request placement used a complete database vector for commands and many bounded single-partition reads. | Known canonical commands admit on the current write primary. Single-partition reads select their scoped owner plus current metadata. Graph traversal, incoming edges and multi-partition operations retain the database vector because they can read foreign rows. | Source review complete; current runtime qualification pending. |
| Query/search loaded the principal before a lower API loaded it again at the actual data cut. | Bound query/search operations use the lower API's current persisted policy check; the public identity guard and authorization diagnostics remain. Metadata-only capabilities still reload policy. | Source change; current authorization and whole-operation tests pending. |
| Crash injection guessed the next physical timestamp. | Arm after syncing the original seed, then stop at the selected actual publication stage and mutation index. Recovery commit-count oracles use the original persisted partition prefix. | Current actual-process campaign pending. |
| Native tuning forced frequent small mutable segments and larger decompression blocks. | Use the native mutable-segment capacity and the supported 1 MiB LZ4 compression block. Native maintainers, cache, WAL barriers and async APIs retain ownership. | The 250,000-document SDK diagnostic passed; memory tradeoffs and released Linux measurements remain open. |
| Copy batches duplicated full command results already present in the exact stored outcome changes. | `Applied` carries only typed sequence/success/error decisions. The selected primary returns full results; copies retain the exact original outcome bytes and return compact acknowledgements directly. | The real queue-delivery serialization, byte-identical copy/replay and healthy acknowledgement control passed in the 17-case replication run. Matched runtime measurements remain pending. |
| Per-batch ordinary activations added placement and activation lifecycle work without owning a separate invariant. | Local primary execution borrows storage through DI. Remote primary execution and copies use the typed Grain Service. Extra apply-grain contracts, placement helpers and activation IDs are removed. | Orleans 10.4.2 `SystemTarget.ReceiveMessage` schedules each request on its existing work-item group and tracks awaited requests separately ([source](https://github.com/dotnet/orleans/blob/v10.4.2/src/Orleans.Runtime/Core/SystemTarget.cs#L351)). Local/remote execution and held-copy isolation passed in the 17-case run. Broader lifecycle qualification remains open. |
| Every read-budget check called `GetTimestamp()` and discarded the value, including scan/search inner loops. | The budget no longer takes or forwards a clock. Cancellation, bounded reads, policy/lifetime checks and real business clocks retain their owners. Exclusive clock-driven test helpers are removed with the obsolete API. | The strict combined build and 26 actual-work cancellation, fairness, reservation and shutdown controls passed. A matched runtime profile is still required before a speed claim. |
| Some document/search cut readers decoded a scoped position but used only its number. | Existing canonical-prefix validation now validates decoded bytes against the requested partition in every document, ANN, text and corpus reader. Missing-record behavior and original error details remain explicit; borrowed typed reads retain their existing allocation behavior. | All four cold document-session damage controls passed, including a valid metadata prefix under a data-partition key, refusal, exact repair, replay and healthy continuation. |

There is one current write protocol. Metadata and data decisions select a common
current primary so native transaction conflicts cover resource changes; their
independent partition queues do not serialize each other's replica waits. This
full-copy topology does not establish partition fan-out or three independent
physical primaries. Replicas of a partition are never separate query inputs.

The additional 250,000-document run used PID 35038 and retained its exact Debug
DLL/PDB and tracked/untracked source hashes. All read, update, literal-body,
revision and joined-shutdown oracles passed. A five-second MCP window observed
approximately 1.15 GB/s allocation and 4% GC time. These counters identify
allocation activity; they do not attribute it to a particular source method.
The official eight-second trace succeeded while MCP still classified this
managed process as NativeAOT. Its attach window spans active request work; an
exact phase marker was not captured, so it is not a matched phase comparison.
Its `dotnet-sampled-thread-time` stacks include waiting threads. Microsoft
documents this as sampled wall-clock thread time, so its percentages are not
reported as kernel CPU shares ([dotnet-trace documentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)).

The local diagnostic's former `physicalCommits` field is obsolete: subtracting
native timestamps does not count commits. It is replaced with the actual
native timestamp advance. Exact publication counts are asserted through the
native publication observer in the grouping tests.

## Ownership and required behavior

The server owns public connections and issues independent request identities.
Each ordinary request activation owns one bounded operation and its joined work;
Orleans owns placement and scheduling. The node-local physical session owns the
ZoneTree handles. Partition commit grains order their own durable command prefixes and copies; the database grain orders metadata.
Fresh persisted principal, resource, row and field authorization remain mandatory.
Writes preserve command deduplication, revision checks, atomic effects and actual
persisted-copy acknowledgements. Cancellation must join owned execution and never
assert that an interrupted write did not commit.

## Reviewed execution path

| Boundary | Owning implementation | Review result |
| --- | --- | --- |
| SDK and HTTP | `KeyLoad.Client/DocumentStorage/Transport/KeyLoadClient.cs`, `ClientApi/Transport/KeyLoadClientTransport.cs`, server `DocumentApi`, `CanonicalOperationGateway`, `OrleansNodeRequestExecutor` | SDK reuses its supplied HTTP client, bounds responses and distinguishes interrupted writes. The server already uses unary RPC for terminal requests. |
| Authentication | `DatabaseCredentialResolver`, `UserGrain`, `GrainIdentityContext` | The credential grain caches verified credential data; operation owners reload persisted policy. No authentication or authorization cache was added. |
| Request scheduling | `RequestGrain`, `RequestExecutionGrain`, `BoundCqrsStreamLifetime`, `RequestWorkOwner` | Independent request activations preserve synchronous isolation and joined cleanup. All ordinary terminal operations use the unary lifetime; only real parent workflows retain CQRS progress streams. |
| Physical ownership | `PartitionHostApplicationOwner`, `PartitionStorageSession`, `PartitionStorageRequestExecution`, `SiloDatabaseClient` | A local execution borrows the physical session; a remote execution uses Orleans. No handle crosses an RPC. |
| Ordered copies | `DatabaseCommitGrain`, `StorageCommitQueue`, `SiloStorageCopyExecution`, `PartitionStorageGrainService` | Commands share native commits and every selected copy acknowledges persistence. Membership-based owner discovery and compact copy acknowledgements already exist. |
| Command ownership | `CommandExecutionIdentity`, `SiloCommandCoordinator`, `StorageOperationBatchPreparation`, `StorageOperationBatchApply`, `SiloLocalOperation` | Public boundaries snapshot mutable commands. Classification unnecessarily snapshotted already owned commands again. |
| Resource admission | `ResourceEffectCapture`, `ResourceEffectBindingScope`, `ResourceEffectApply`, `ResourceEffectAdmissionValidation` | Immutable lifetime values were serialized merely to compare equality; zero/one-resource captures also constructed an ordered LINQ pipeline. |
| Document operations | `DocumentReadExecution`, `DocumentStorageExecutionDocuments`, `DocumentStorageCommands` | Point reads use native keys and one authorized snapshot. Mutations retain revision, row/field access, index ownership and exact JSON. |
| Native adapter | `ZoneTreeReadView`, `ZoneTreeDataRouting`, `ZoneTreeStoreCommitExecution`, `ZoneTreeTreeFactory` | Native snapshots, WAL publication and maintainers own engine work. No replacement engine cache, write gate or maintenance loop was introduced. |
| Comparisons | document and key-value `Drivers/`, `KeyLoadDatabase`, `BenchmarkConnection` | Key-value uses unindexed document operations. MongoDB uses indexed BSON documents; Redis uses strings and its declared fsync acknowledgement. Their different contracts remain explicit. |

Paths beginning with a project name are relative to `src/`; comparison paths are
under `benchmarks/KeyLoad.ComparisonHost/`. This inventory covers the selected
operation chain and its immediate owners, not every capability in those projects.

## Runtime evidence

The baseline solution build passed with zero warnings/errors. The existing real
Aspire/SDK `DocumentPathDiagnosticTests.TwoHundredFiftyThousandDocuments` passed:
250,000 unindexed documents, batches of 256, 16 clients, one silo and one persisted
copy. Its complete body/revision oracle and joined shutdown passed. The baseline
recorded 977 command batches grouped into 124 physical load commits.

`dotnet-diagnostics-mcp` attached to the actual server PID 4553, macOS arm64,
.NET 10.0.14 / SDK 10.0.403 Debug. Five-second counters and an eight-second
allocation capture expose the active path. The retained allocation trees show
`RequestGrain.CreateStream`, channel writes, repeated `OwnCommand` beneath the
classification step and serializer calls beneath `ResourceEffectBindingScope`.
Inclusive allocation-event counts are not CPU time or byte attribution.

CPU sampling was refused after the tool classified the managed server as
NativeAOT. The project configuration, JIT counters and managed allocation stacks
contradict that classification. The original refusal is retained; no CPU
percentage is attributed to a source method. Native engine work remains a separate
private investigation and is not treated as an Orleans defect.

Original local responses, TUnit reports and process/DLL/PDB/source identities live
under `tests/KeyLoad.ClusterTests/bin/Debug/net10.0/performance-review-20261106/`.
Local instrumented phase timings are not public comparison figures and do not
quantify collector overhead.

The post-change capture attached to actual server PID 10679 during the mixed
phase of the same diagnostic, with the same payload, count, concurrency and
one-copy topology. It retained the same five-second counter/eight-second allocation
settings. Its request subtree runs `ExecuteUnaryAsync` / `RunUnaryAsync`; the
inspected classifier subtree has no nested `OwnCommand`, and the inspected
lifetime activation subtree has no serializer call. Publication reloads its
principal inside the budgeted native cut. Source review confirms those removed
calls. The allocation subtrees are bounded sampling evidence, not exact operation
counts or CPU attribution. Baseline and post-change phase windows are not a
matched byte-per-operation experiment, so no aggregate allocation reduction is
claimed. The two complete post-change one-node diagnostics passed their full
oracles and joined shutdown.

The final build-35 diagnostics also passed their complete body/revision/copy
oracles and joined cleanup: the existing 250,000-document one-copy and
100,000-document three-copy cases, plus separate one-copy profiling runs.
Uninstrumented and instrumented local timings varied substantially on unchanged
binaries; they establish neither an isolated speedup nor the target ingest rate.
Only the original released Linux comparison workload supplies public comparison
figures.

The connected diagnostics server could not resolve the test-owned IPC endpoint.
The official tracing/counter CLIs used the target's actual IPC directory instead;
no dump or privileged live-memory capture was used. The five-second allocation
capture starts from the live SDK point-read event, records matching DLL/PDB hashes
and verifies that the same phase remains active at completion. Its leading sites
include document deserialization, TLS/JSON response buffers and request-lifetime
bookkeeping. These are measured costs, not proof of a library defect. An earlier
late attachment is explicitly unqualified. The separate sampled-thread trace
contains waits and supplies no source-level CPU percentage.

## Findings

### P2: terminal batch/query RPCs created and drained a local progress stream

The HTTP executor called unary `IRequestGrain.ExecuteAsync`, but the request grain
created a channel-backed progress stream and enumerated it for ordinary document
commands and SQL/AST queries. Only document point reads already used the unary
work lifetime. The active allocation tree includes the channel and Started chunk.

Repair: these terminal operations reuse `RunUnaryAsync`. The same execution,
verified purpose, producer lease, resource binding, terminal admission and awaited
settlement remain. Explicit streaming calls and parent maintenance commands retain
their progress path. No request grain or synchronous-isolation guarantee was
removed.

### P2: command classification repeated an already completed ownership copy

The coordinator receives an owned command from `CreateCommand` or
`SubmitVerifiedAsync`. Batch apply first calls `OwnCommand` for each input. Both
then called `IsSiloLocalOperation`, which copied that entire command again simply
to select its kind. Local apply had the same duplication. The active apply
allocation tree contains both copies.

Repair: internal callers classify their already owned snapshot directly. The
public classifier and all external admission boundaries retain their independent
copies and original validation. No caller-owned buffer is retained across an
asynchronous boundary by this change.

### P2: lifetime equality serialized immutable values into two byte arrays

`Activate` and `RequireComplete` encoded both sides with the Orleans serializer
before comparing them. The equality question concerns six immutable scalar/record
fields, not a wire format. The active capture also includes repeated ordered
iterator allocation for the usual single-resource command.

Repair: compare the ordered lifetime spans using their record equality. Every
field and its case remain significant; order and cardinality remain significant.
Duplicate and malformed bindings still pass through the existing validator.
Zero/one-resource captures skip sorting; creating an admission no longer captures
it twice merely to compare it with itself. Lifetime observation uses the set's
existing duplicate-aware insertion. Persisted bytes and RPC contracts do not
change. Nine whole-operation controls cover independent equal values, every
changed field, changed order/duplicates, refusal without either document, exact
replay and healthy continuation.

### P2: command-result publication read the same principal in two snapshots

`BindCurrentAsync` reloaded the principal only to pass its ID into
`BindAuthenticatedCommandResultLifetimes`, which immediately reloaded it in the
budgeted publication cut. The first snapshot supplied no authority retained by
the second; the final cut already owns the policy-epoch check.

Repair: the final budgeted cut invokes the existing request-authority rules once,
including the protected journal identity guard and authorization diagnostics.
Persisted principal expiry, revocation and the expected policy epoch are still
checked at publication. No principal or policy cache crosses operations.

### Remaining qualification and bounded costs

The independent partition queues and exact-effect protocol replace the shared
logical write coordinator. All-copy acknowledgement still waits for actual
persistence on the selected copies. The primary is common to data and catalog
so native transaction conflicts protect their decisions. This is a full-copy
execution design; it does not establish physical partition fan-out or three
independent write primaries.

Native MVCC timestamps identify a snapshot within one physical opening. Prepare
returns the current discard-only snapshot timestamp; a commit compiler receives
its native write identity and final publication reports the actual commit stamp.
Cold opening may reserve a later stamp without changing records. Physical stamps
are not copied as canonical command counts. Whole images and scoped prefixes
prove recovery; a native publication observer proves grouped commit counts.

The per-step read budget now checks caller/stage cancellation and retained uncertain
failures without reading a discarded timestamp. Its clock parameter and exclusive
clock-driven test helpers are removed. Real storage elapsed-time checks, cursor
expiry, operation time and timers retain their actual clocks. Active range/point
cancellation observes the actual storage read counters; prepared-result controls
state their exact boundary. These updated controls passed their focused execution.
Actual native allocation cost, memory tradeoffs and matched before/after phase
measurements remain open. A sampled wall-clock stack
is not a CPU-share measurement and a source deletion is not a speed result.

## Original Linux baseline

[Benchmarks run 42128099998](https://github.com/managedcode/KeyLoad/actions/runs/42128099998)
used released `b55245893c93409e41d7ca31edfec967da173045`, KeyLoad image digest
`sha256:6e40f30f0b025daff8daf4636150e8619482d124423e4bc3da331dbd155f70e1`.
All listed cases passed with zero errors, one million records, 100,000 planned
measured operations and 16 clients. Load uses one million records in batches of
256. Values below are rounded operations per second from the original artifacts.

| Type / database | Nodes | Load | Read / get | Range | Update / set | Delete | Mixed |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Documents / KeyLoad | 1 | 13,304 | 3,293 | 2,632 | 1,303 | 1,326 | 2,025 |
| Documents / MongoDB | 1 | 43,562 | 7,874 | 6,110 | 6,468 | 7,649 | 7,030 |
| Documents / KeyLoad | 3 | 4,497 | 1,394 | 1,433 | 291 | 280 | 500 |
| Documents / MongoDB | 3 | 27,660 | 6,903 | 5,439 | 2,256 | 2,595 | 3,271 |
| Key-value / KeyLoad | 1 | 16,457 | 4,646 | — | 1,743 | 1,846 | 3,288 |
| Key-value / Redis | 1 | 96,771 | 25,347 | — | 9,633 | 15,311 | 14,571 |
| Key-value / KeyLoad | 3 | 5,671 | 1,628 | — | 341 | 321 | 714 |
| Key-value / Redis | 3 | 30,299 | 60,906 | — | 8,762 | 12,496 | 20,014 |

The three-node configurations are replicated, not proof of partition fan-out.
These are different database contracts on separate hosted runners. A new run must
preserve every case, its verification and the original errors; run-to-run movement
alone does not establish the isolated effect of this patch.

## Verification and delivery

| Gate | Current result |
| --- | --- |
| First small batch `e49b5126d955aae2a805c8bda8705ef5c7545f5a` | Pushed to main. Its earlier local focused checks passed. |
| First-batch Linux Build and Tests `42192949029` | Failed the canonical inventory: nine added admission cases were absent. Release and comparisons were skipped. |
| Latest partition/query/search/recovery source | Complete at the local implementation and verification scopes listed here; physical partition fan-out and deployed filesystem qualification remain separate. |
| Combined Debug solution build 35 | All 20 projects passed with zero warnings/errors. Restore, full format/verify, governance and exactly-once shard assignment passed. |
| Focused replication/document checks | 17/17 replication and 68/68 document checks passed at their stated earlier builds. |
| Complete functional campaign 33 | 3,092/3,094 passed, two failed, none skipped, in all 27 canonical namespace processes. Original failures are retained; feature repeats are recorded below. |
| Complete feature repeats | Build 34: Messaging 391/391; Search 390/391. Build 35: all 123 selected ANN cases passed, including the corrected cancellation/healthy-resume case. No skips. The external durable-job race concern remains documented. |
| Complete actual-process campaign 34 | 154/154 passed, none skipped; 4 minutes 29 seconds. Exact outcomes, complete images, cold replay and healthy continuation are preserved. |
| Current real SDK diagnostics | Both original one-copy 250,000-document and three-copy 100,000-document cases passed. Separate profiling cases passed; local timings are diagnostic evidence only. |
| Current Aspire SDK/MCP/SQL | Both complete every-model cases passed on one and three nodes. Both public-query cases passed in each of isolated three-copy and shared-volume one-copy modes. The three-copy restart/missing-copy fault case passed. |
| Compiled inventory and governance | Six original discovery runs validated all 3,094 build-35 cases and the five current selectors. Governance and exactly-once shard assignment pass. Coverage remains a separate Linux gate. |
| Exact-source delivery and comparisons | Established by the original Linux Build and Tests, Release and Benchmarks artifacts for the checkpoint SHA, separately from these local checks. No released figure is inferred from local captures. |

Detailed TUnit output is retained with live progress and ANSI enabled. Diagnostic
excerpts omit repeated redraws. Tests keep their original operation, failure,
healthy continuation, real storage and joined-cleanup requirements.
