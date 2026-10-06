# ClusterRouting: replicated runtime journal

Status: accepted implementation contract, unqualified. Related decision:
[ADR-110](../../ADR/ADR-110-native-orleans-execution-primitives.md),
[runtime adoption](RuntimeAdoption.md), REQ/AC-ORL-013.

## Authority and execution

Use the native Orleans10.4.0 Journaling and DurableJobs APIs with their matching
10.4.0-alpha.1 packages and explicit `orleans-binary` journal format. A named
KeyLoad provider implements native storage and catalog interfaces. Every read and
write uses a freshly signed, separately keyed IRequestGrain through the existing
native CQRS executor; canonical records are applied by the unchanged node-local
ZoneTree owner and RF3 commit path. No provider directly commits storage.

The journal is infrastructure state in the initial single RF3 authority group,
distinct from logical user partitions. A protected persisted
`keyload-internal-runtime-journal` identity has no public credentials, user grants,
administrator role or SchedulerManage. It can execute only the closed journal
commands and reads. A private RF3 bootstrap command under a freshly revalidated
existing persisted administrator creates that identity; missing or inconsistent
identity after bootstrap is a recovery failure. Public principal/API-key changes
and normal request purposes cannot configure or invoke this identity. Journal
metadata authority never authorizes saga effects: those use the persisted creator.

## Frozen canonical storage contract

Append one internal `RuntimeJournal` OperationKind without renumbering values.
Its generated attributed mutation has closed actions BootstrapIdentity, Create,
UpdateMetadata, Append, Replace and Delete. Private reads return a header, one
bounded opaque byte page or the complete bounded catalog. No new public SDK/MCP
tool, SQL opcode or parallel dispatcher is introduced.

Generated header/chunk/capacity records use stable new v1 aliases and field IDs.
Existing records, exact user content, storage codec, RF3 log and recovery journal
remain unchanged. This is an additive private key family with a version1 marker,
not a replacement storage format. Require homogeneous upgraded servers before
bootstrap and the first journal write. Retain a verified pre-bootstrap backup;
rollback after that write requires restoring that backup. Older binaries must not
be used to open/replay journals they do not understand. Mixed-version native-job
operation and cross-authority-group adoption are unqualified and unavailable.

Before native journal bootstrap, mark both exclusively owned server stores with
StoreIdentity Id8 MinimumReaderContract=1 and a distinct checked identity envelope
magic. Canonical data epoch7 and key bytes stay unchanged; readers accept only
legacy-magic/contract0 or journal-magic/contract1. Identity publication is monotonic,
atomic and idempotent; uncertain publication closes the runtime until reopening.
An existing nonempty unmarked store requires a verified pre-upgrade backup before
marking. Mark both stores after any prepared format upgrade has completed and
before snapshot recovery, consensus or silo admission. Checkpoint identity writes,
backup and restore must preserve the requirement. A raw snapshot containing native
journal records cannot be installed into an unmarked embedded store. Standalone
old-binary snapshot import is unsupported, and is not established safe by an
identity fence. A fresh authenticated all-voter reader1/transport-ready gate must
precede first bootstrap; later RF3 quorum recovery preserves its one-fault contract.

The public JSON identity omits `MinimumReaderContract` only when its value is the
legacy default0, preserving ADR-041's exact existing identity bytes. Contract1 is
serialized and retained on JSON round-trip. Native Orleans Id8, binary identity
fences and backup/checkpoint/reopen requirements are unchanged. The independent
`ReadOnlyCollectionContractTests` legacy and contract1 goldens enforce both cases.

Native startup begins background catalog discovery without awaiting it; async
provider calls await a separate cancellable readiness gate. After silo and physical
catalog startup, a dedicated signed request bootstraps and verifies journal identity
and catalog, then opens that gate. Shutdown closes public/due scheduling first,
leaves private journal routing and RF3 transport alive, and decorates only the
native manager's exact public Active lifecycle subscription. Its observer completes
native cleanup before joining NativeRequestWorkOwner, before grain deactivation.
Forward the native start and every lifecycle property unchanged; fail closed on
an unexpected descriptor, subscription or stage. No reserved lifecycle stage is
repurposed and the native receiver remains registered unchanged.

Each journal has a nonempty instance GUID, owner generation, independent content
revision and metadata ETag. Create is atomic and idempotent; delete/recreate has
a new instance. Append/replace/delete compare the captured instance, owner
generation and content revision under the atomic apply gate. Metadata writes use
full ETag CAS; mismatches return null through the native metadata API.
An explicitly null metadata ETag preserves the native unconditional-update
contract under the same atomic gate; it never refreshes a stale body-writer fence.
Metadata reads return a fresh snapshot without replacing that captured fence.
Body fencing failures map to native InconsistentStateException. Changes to
the tagged native `DurableJobsOwner` property increment owner generation, including
release/reclaim, and poison transitions also advance this fence. Same-owner `DurableJobsClosed`, adoption bookkeeping and format
updates do not fence the current writer. Every successful content write advances
its captured revision; an old handle cannot refresh itself into a new owner.
Reads pin the header/content revision across bounded pages and fail on change.
One native ArcBufferWriter retains incomplete binary entries across page boundaries
until the consumer reports completion; retained bytes are bounded by journal capacity.
Stable command identity resolves unknown write outcomes through existing receipts.

Native payload bytes remain opaque and never use JSON fallback. The centrally
validated RuntimeJournalOptions admits at most32 journals,2MiB per journal,
64MiB total data,64KiB chunks/read pages and8KiB UTF8 metadata per journal.
Journal names and metadata keys/values have explicit byte limits. Admission and
catalog reads enforce those limits on actual committed state, not only callers.
The provider owns a bounded striped asynchronous gate set sized by the validated
maximum journal count. Handles for the same journal borrow the same gate; each
retains its own captured instance, owner generation and content revision. Native
`IJournalStorage` has no disposal contract, so handles must not allocate an owned
disposable gate. Provider disposal follows native shutdown and joined work, and
idempotently releases the gate set without retaining every created handle.
The native catalog materializes all selected entries before its claim budget,
so the provider returns the whole bounded catalog or a terminal error; truncation
must never pretend recovery completeness. Delete releases capacity atomically.

## Native jobs consumer and recovery

The first consumer hands already-due sagas to native jobs. The existing canonical
Waiting saga record is the persistent pending-work intent. The leader-fenced
fixed-tail due sweep schedules at most one attempt per hint per cycle, with its
existing one-page/sequential bounds. It leaves the saga Waiting until expiry.
Lost schedule acknowledgements may produce duplicate native jobs on later sweeps;
there is no immediate unbounded retry. Strict journal quotas bound retained work
and fail explicitly under pressure. Configure native snapshot compaction so
completed jobs can shrink the journal. Existing due discovery recovers work after
restart. Immediate future-deadline registration and single persisted job handles
remain a separate stage requiring an atomic wake-intent/uncertainty contract.
Job metadata contains only canonical IDs, revision and deadline; no roles,
credentials, signed envelopes, user JSON or timeout message content.
Each schedule request supplies a fresh valid unsampled W3C trace parent and an
empty trace state, independent of the ambient activity. In Orleans v10.4.0 an
empty trace parent triggers ambient inheritance, so empty strings cannot opt out.
The actual native returned job must prove that caller trace state was not retained.

The native-mechanism regression schedules one held job and records its actual
native job/shard identity and persisted owner fence. Stop and dispose the first
Orleans runtime through its native lifecycle, preserving the genuine canonical
store, then create a fresh runtime and request-work owner. The new manager must
claim the existing shard, advance its owner fence, recover the same job without
rescheduling, and commit exactly one timeout effect. Every restart and wait is
bounded and cleanup joins both runtimes. This in-process runtime restart is unit
evidence only; it does not replace the required real-process and Aspire RF3 proof.

The existing RecurringDueCoordinatorGrain also implements native IDurableJobHandler.
The native receiver extension may AlwaysInterleave. Handler state is invocation
local; after leadership/quorum checks it reloads canonical saga/creator state and
creates the existing creator-authorized expiry request through native CQRS.
Expected saga revision, phase/deadline checks and timeout-message identity are
the effect fence. Stale, canceled, completed and revoked jobs have no effect.
Transient no-quorum/unknown results retry only within explicit bounded policy.

Keep current default-deny ManagedCode.Graph rules and native Orleans-call tracking
configuration. Extend the existing public DirectedGraph/GrainTransitionManager
descriptor by copying every existing edge unchanged and adding only discovered
native manager→IRequest and concrete coordinator ExecuteJobAsync→IRequest method edges.
Native receiver dispatch is handled by the unchanged native-call policy; never
turn on AllowAll, replace the receiver extension or impersonate a GrainService.

## Requirements, stages and acceptance

REQ-ORL-013 requires bounded native journal-backed jobs through RF3/request/native
CQRS authority, owner fencing and creator-authorized idempotent saga expiry.
AC-ORL-013 requires real canonical create/append/replace/read/reopen/delete,
metadata CAS and same-owner updates, stale/recreated-handle failures, capacity
exhaustion without partial writes, complete catalog, identity/credential denial,
unknown-outcome receipt resolution and actual native job execution. Process
restart/adoption, no-quorum, creator revocation, schedule-ACK uncertainty and
single timeout effect must pass through Aspire RF3 and real SDK/official MCP.
No source or native local fixture closes those gates.

The root join owns [RuntimeJournalStartupRequests](../../../src/KeyLoad.Orleans/Features/ClusterRouting/Hosting/RuntimeJournalStartupRequests.cs),
[RuntimeJournalStartup](../../../src/KeyLoad.Server/Features/ClusterRouting/Hosting/RuntimeJournalStartup.cs),
[OrleansNodeRequestExecutor](../../../src/KeyLoad.Server/Features/ClusterRouting/Execution/OrleansNodeRequestExecutor.cs),
the exact native lifecycle registration/observer, and both store reader fences.
The executor retains the existing cohort check, catalog admission, execution
deadline and complete native CQRS stream; it adds no dispatch or storage owner.

Authored AC-ORL-013 development cases are mapped to
[native storage and binary DurableGrain replay across confirmed new activations](../../../tests/KeyLoad.UnitTests/Features/ClusterRouting/Cases/RuntimeJournalNativeStorageTests.cs),
[canonical quota and protected identity](../../../tests/KeyLoad.UnitTests/Features/ClusterRouting/Cases/RuntimeJournalQuotaAndIdentityTests.cs),
[journal reopen](../../../tests/KeyLoad.UnitTests/Features/ClusterRouting/Cases/RuntimeJournalReopenTests.cs),
[reader backup/checkpoint/reopen lifecycle](../../../tests/KeyLoad.UnitTests/Features/StorageRecovery/Cases/RuntimeJournalReaderLifecycleTests.cs),
[reader rejection](../../../tests/KeyLoad.UnitTests/Features/StorageRecovery/Cases/RuntimeJournalReaderFenceTests.cs),
and [actual native saga expiry, stale completion and creator revocation](../../../tests/KeyLoad.UnitTests/Features/Messaging/Cases/NativeSagaTimeoutFunctionalTests.cs),
plus [native runtime restart and retained-shard reclaim](../../../tests/KeyLoad.UnitTests/Features/Messaging/Cases/NativeSagaTimeoutRestartTests.cs).
The latter preserves the genuine store across two native runtimes and asserts
the actual recovered shard, stable canonical journal instance, changed owner/fence,
settled native work and exact timeout effect. It makes no process-restart claim.
These cases have not executed. Retained-job process restart/adoption qualification,
schedule-ACK uncertainty, frozen old-binary probe and complete Aspire SDK/MCP
RF3 fault/resource evidence remain required before acceptance.

Development checkpoint, 2026-10-06: matching packages restored. Backend, native
adapter, startup/reader fencing, selective lifecycle join and native saga-handler
sources/tests are joined and unqualified. The final canonical build failed with456
shared CrashHost/Comparisons diagnostics, and canonical formatting with1117;
the104 reviewed stage paths had no reported finding in either check.
[The development receipt](../../implementation/native-orleans-development-2026-10-06.json)
preserves the exact commands and distinguishes source review from acceptance.
No native test execution or final successful build is claimed. A frozen
old-binary reader rejection probe and all Aspire RF3/fault/resource gates remain open.

| Task | Ownership and dependency | Required join/evidence |
|---|---|---|
| TASK-ORL-JOURNAL-CONTRACT / root | Shared generated DTOs/enums, options, auth/request/codec/dispatch joins, private format/rollout and docs. | Review every closed inventory and preserve existing identities. |
| TASK-ORL-JOURNAL-CORE / Luna high | New Core ClusterRouting journal commands/queries/records/validation and new real-store tests; starts after these contracts. No existing shared dispatch/auth/serialization/composition edits. | Atomic bounded backend and self-review; root owns runtime gates. |
| TASK-ORL-JOURNAL-PROVIDER / Luna high | Native provider/storage/catalog and genuine native journal tests after Core joins. No shared request/schema/Git edits. | Exact native API behavior, captured-version fencing and cancellation; root joins configuration. |
| TASK-ORL-JOBS / Luna high | Native saga wake consumer plus owned intent/reconciliation tests after provider/authority readiness. | Real schedule/restart/revocation/unknown-outcome/single-effect evidence. |
| TASK-ORL-ROOT-JOIN / root | All final format/analyzer/build, Aspire unit/scalar/recovery/RF3, functional coverage and original Linux evidence. | Remains open until every mandatory acceptance gate passes. |

```mermaid
flowchart LR
  Native[Native Journaling and DurableJobs] --> Request[Fresh signed request grain]
  Request --> Replica[Existing RF3 command or read grain]
  Replica --> Owner[Node-local canonical ZoneTree owner]
  Native --> Wake[Saga deadline wake]
  Wake --> Creator[Reload canonical saga and creator]
  Creator --> Due[Existing due coordinator]
  Due --> Request
```
