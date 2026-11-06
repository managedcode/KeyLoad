# Document and key-value execution review

Review started 2026-11-06 against `fde02c1a5`. Scope: document insert, replacement,
deletion, point reads and indexed ranges, including the SDK, HTTP admission,
Orleans routing, physical ownership, authorization, native storage and replication.
Key-value currently uses the same document path without secondary indexes.
MongoDB and Redis are the existing comparison drivers; no benchmark contract or
dataset change is part of this review.

Status: repairs passed local qualification; Linux delivery in progress. Findings distinguish source observations,
runtime evidence, completed repairs and remaining qualification. This document
does not claim a whole-repository audit or product readiness.

## Ownership and required behavior

The server owns public connections and issues independent request identities.
Each ordinary request activation owns one bounded operation and its joined work;
Orleans owns placement and scheduling. The node-local physical session owns the
ZoneTree handles. The database commit grain orders durable commands and copies.
Fresh persisted principal, resource, row and field authorization remain mandatory.
Writes preserve command deduplication, revision checks, atomic effects and actual
persisted-copy acknowledgements. Cancellation must join owned execution and never
assert that an interrupted write did not commit.

## Reviewed execution path

| Boundary | Owning implementation | Review result |
| --- | --- | --- |
| SDK and HTTP | `KeyLoad.Client/DocumentStorage/Transport/KeyLoadClient.cs`, `ClientApi/Transport/KeyLoadClientTransport.cs`, server `DocumentApi`, `CanonicalOperationGateway`, `OrleansNodeRequestExecutor` | SDK reuses its supplied HTTP client, bounds responses and distinguishes interrupted writes. The server already uses unary RPC for terminal requests. |
| Authentication | `DatabaseCredentialResolver`, `UserGrain`, `GrainIdentityContext` | The credential grain caches verified credential data; operation owners reload persisted policy. No authentication or authorization cache was added. |
| Request scheduling | `RequestGrain`, `RequestExecutionGrain`, `BoundCqrsStreamLifetime`, `RequestWorkOwner` | Independent request activations preserve synchronous isolation and joined cleanup. Local progress-stream construction remained on terminal batch/query calls. |
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

### Remaining structural limits

The database commit grain orders the canonical copy sequence for one store
incarnation. This is a common coordination point, and three-copy writes also join
all physical copies. Replacing it with independent writers requires a coherent
sequence/read-cut/copy-repair design; removing that ordering alone is unsafe.
The current patch removes avoidable work around it. It does not claim to implement
partitioned write execution or to make one-copy and three-copy costs equal.

The point-read path already has a direct document seek, unary request execution
and fresh policy within its native snapshot. Adding another index, request cache
or stateless-worker pool is not justified by this review. Complete native-work
and remote-routing attribution remain separate investigations.

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

## Verification

| Gate | Current result |
| --- | --- |
| Restore | Passed. |
| Full solution formatter and verify-no-changes | Passed on the complete repair batch. |
| Full solution Debug build, `-warnaserror` | Passed, zero warnings/errors. |
| Repository governance | Passed, 20 projects and six modules. |
| Document functional operations | 68/68 passed, including all nine new admission controls. |
| Orleans operation, cancellation and joined-lifetime controls | 193/193 passed. |
| Grouped commits and copy acknowledgement controls | 16/16 passed. |
| Mutable command ownership controls | 2/2 passed. |
| Actual document process-kill recovery | 3/3 passed. |
| Actual Aspire three-copy document SDK/MCP parity | 6/6 passed. |
| Post-change one-node 250,000-document diagnostics | Both complete runs passed; one carries the active post-change capture. |
| Fresh three-copy 100,000-document diagnostic | Passed, including exact full-body/revision readback, three persisted copies and joined shutdown. |
| Exact-source Linux Build and Tests, Release and comparison artifacts | Pending delivery. |

The initial document test invocation hit an MTP live-reporter sorting crash after
one test expectation incorrectly treated malformed duplicate admission as a
business result. The final control requires its exact `Corruption` exception and
unchanged physical position. Final runs keep Detailed output with live progress
redraw disabled; no assertions or tests were removed. All final counts above have
zero failures, skips, cancellation and flaky outcomes.
