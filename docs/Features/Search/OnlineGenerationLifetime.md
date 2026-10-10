# OnlineGenerationLifetime within Search

Accepted L1 implementation for original KL-039 under
[ADR-095](../../ADR/ADR-095-online-generation-leases.md). Native FTS remains a
disposable node-local derived index, with exact source-cut/policy/schema manifests
and the existing selected provider. L1 delivers leased publication/retirement;
base-cut capture, delta catch-up and a background build outside the canonical
read gate are separate required L2 work, not completed by this stage.
The native capture/lifetime L2-A contract is
[NativeReadCuts](../StorageRecovery/NativeReadCuts.md) under ADR-097; it does not
alone provide delta retention, catalog switch or an online-index completion claim.

| Requirement | Acceptance and mapped tests |
|---|---|
| REQ-LEASE-001: publication retains borrowed old generations | AC-LEASE-001: an actual native generation lease remains usable and its owned files remain present while another source-cut generation publishes; current acquisition selects only the fully verified replacement; last old release retires only that generation. `NativeTextGenerationLifetimeTests` |
| REQ-LEASE-002: finite lifetime and admission | AC-LEASE-002: at most2 active leases, one native builder, one retired leased generation and one current generation; at most3 physical generations including the staged builder; native total disk/file/metadata/posting limits account every owned generation. Exact saturation/cancellation releases admission and a following request succeeds |
| REQ-LEASE-003: cleanup and shutdown preserve truth | AC-LEASE-003: failed native refresh/close/delete retains the unsettled owner/handle for joined retry, both primary and cleanup failures survive, repeated release/dispose cannot double-release or delete another generation; shutdown joins leases/build and closes every owned handle. Existing settlement tests and new overlap/shutdown cases |
| REQ-LEASE-004: existing runtime behavior stays exact | AC-LEASE-004: actual SearchEngine/GraphSearch text/vector/native corpus results, visibility/cut mismatch, restart and native process interruption remain exact through the unchanged ITextProjection interface; Aspire full unit/recovery/RF3 and delivered-source Linux gates pass |

No raw borrowed storage view may escape its gate. Native leases operate on their
owned generation; each query still performs current canonical candidate checks
inside its authorized read cut. A source-cut mismatch builds a new generation;
it never borrows an old manifest as current truth. Publication closes/verifies
the staged index and flushes its existing manifest before changing the manager's
current pointer. A retired lease can finish its originally scoped work, not read
new canonical state or make a current-authorization claim.

Replace the lifetime-long single semaphore with a short synchronized manager
transition plus independent bounded lease/build admission. No monitor or store
gate is held across waits or user callbacks; no polling wait on a grain thread.
Keep the existing ITextProjection/lease public shape, faults and generated
identity unchanged. Active generation readers may share only actual provider
operations proven safe; otherwise use one reader per generation and explicit
saturation. Never mutate or close an index borrowed by another lease. The maximum
counts above are ceilings, not permission to invent provider thread safety.

Root owns this contract, cross-cutting docs/joins/gates/receipts. Luna query_wave
owns only Server Features/Search Storage/Lifecycle new helpers and existing
NativeTextProjection/NativeTextProjectionLease/NativeTextSettlement lifetime
joins, NativeTextGenerationFiles.EnsureGenerationCapacity and
NativeTextRootFiles.RetireRestartGenerations capacity/preflight joins, plus new
UnitTests Search Cases/Helpers/Assertions/Models. Prepare a private
patch against3985008 while root tests that immutable compilation. Do not alter
provider APIs, Query read-cut ownership, canonical outbox/data epoch/public wire,
shared configuration or existing fixtures to hide failures. Root integrates the
actual consumed manager change, reviews and executes Aspire gates before commit.

The overlap fixture owns a linked cancellation source for its admitted native
replacement task. Any observation deadline cancels that source, resumes its
actual posting barrier and joins the original task before disposing the lease
or projection; a second timeout-abandoned wait is not cleanup evidence.

TASK-LEASE-LIVE-PREFLIGHT, accepted 2026-10-05, refines AC-LEASE-001/002/003
after the actual unfiltered Aspire unit61b failure. Capacity preflight currently
hash-opens the existing published WAL while its native index still owns that
file; replacement therefore fails before reaching its posting barrier. Inside
the existing physical gate, capture only the manager's at-most-three actual
generation references. An exact manager-owned, published generation with a
still-open native index receives a bounded live ownership/layout/size preflight
without reopening native payload files. Verify its root, leaf, source-node and
scope against the retained verified generation and owner/manifest metadata;
reject unknown, linked, nonregular or foreign paths and retain all file, depth,
generation and aggregate-byte ceilings. Runtime generation references cannot
be supplied by a caller, inferred from a sharing exception, or substituted with
an arbitrary trusted-leaf list. Every other generation keeps the complete
existing checksum validation. Closed publication, reopen, retirement, deletion
and restart retain full inventory checks; no format or provider sharing mode
changes. Native mutation/census ownership remains in the physical gate, while
manager admission is never held across callbacks or awaited work.

Query worker owns only a private patch for the consumed NativeTextProjectionLifecycle,
NativeTextFiles/NativeTextGenerationFiles capacity join and feature-local live
preflight helpers, plus the real Search overlap regressions. Root reviews the
exact live-object provenance and every cold validation call before applying it.
Keep the existing corpora, deadlines, byte/file limits, native provider and
old-reader/result assertions. Cleanup resumes the actual barrier even if old
lease disposal fails, cancels and joins the original replacement task, then
reports every distinct failure. Exercise real published-reader replacement,
invalidation, unknown-path denial and closed/restart corruption controls through
the actual Aspire caller. This source repair does not qualify L1, L2 or RF3;
full suites and exact-source Linux evidence remain required.

TASK-LEASE-LIVE-SIZE, accepted 2026-10-05, corrects the actual native-text71
failure while preserving REQ/AC-LEASE-001/002/003. The retained manifest describes
the fully closed publication inventory; a manager-owned reopened native WAL may
have a different current length. Live preflight must inspect every tracked native
file without reopening it, reject nonregular/linked files and any current file
over MaximumDiskBytes, and retain the existing per-generation and aggregate
actual-byte census. It must not compare a live file length with its closed
manifest length. Retained manifest scope, records and file metadata must still
match the manager's verified publication. All cold generations, publication,
reopen, retirement, deletion and restart keep exact lengths and checksums.
Luna query_wave owns only a private NativeTextLiveGenerationMetadata correction;
root reviews and joins it before the same original native tests run through
Aspire. No provider, sharing mode, ceiling, deadline or persisted format changes.

TASK-LEASE-COLD-FIXTURE, accepted 2026-10-05, preserves the original native
capacity and retirement oracles. Luna lifecycle_wave owns only a private patch
for NativeTextGenerationCapacityTests and NativeTextGenerationLifetimeTests,
with a feature-local cleanup helper if the numeric policy requires extraction.
Sort the independently created expected generation leaves ordinally before the
existing exact ordered comparison. Every borrowed lease must settle before its
owning projection's shutdown even when an earlier operation fails; retain every
primary and cleanup failure through ServerFailureObserver. The restart fixture
must first preserve its real borrowed retired generation by the existing owned
foreign-file failure, settle that lease, then run and observe failed projection
shutdown so both native indexes are actually closed. Only after closure may it
remove the fixture-owned foreign file, create the third leaf through the
unchanged cold WriteOwner call, and restore that same owned foreign entry.
Restart must still reject the entry before deleting any of the three generations,
preserve every original generation, and succeed after removing only that entry.
Keep the original old-reader, revisions, three-leaf bound, denial, healthy retry
and real provider assertions. Do not pass fabricated live slots into cold calls
or weaken full cold inventory validation. Root runs the unchanged acceptance
suite and captures actual source/runtime and terminal results.

TASK-LEASE-CANCEL-SETTLEMENT, accepted 2026-10-05, addresses the actual Aspire
native-text72b failure in SaturationAndCancellationReleaseNativeLeaseForHealthySearch.
The final aggregate physical census in NativeTextProjection.Release must use the
same completed-operation rule as NativeTextSettlement.Refresh: an incomplete
or cancelled operation settles without consulting its exhausted caller budget,
while retaining all real file, byte and generation ceilings and distinct cleanup
errors. A successfully completed operation retains its existing budget checks.
Luna query_wave owns a private patch to NativeTextProjection.Release only,
unless the unchanged original regression identifies a further owning defect.
Do not suppress cancellation globally, hide physical failures, change deadlines,
or weaken tests. Root reviews the joined change and reruns the original 36-case
native-text selection through the Aspire AppHost; full qualification stays open.

Frontend/new SDK/MCP syntax N/A: unchanged public search surfaces consume this
manager. Migration N/A: no canonical record or persisted index format changes.
Rollback stops the capable node and rebuilds disposable indexes; no canonical
data is deleted. L1 cannot close KL-039's concurrent canonical-write/delta/restart
criteria until L2 and original qualification exist.

```mermaid
flowchart LR
  Build[One verified native builder] --> Current[Publish current generation]
  Current --> Retired[Previous generation retains old lease]
  Retired --> Drain[Last reader settles]
  Drain --> Delete[Verified owned retirement]
```


## Original KL-039 L2-B contract proposed for owner review

Status: proposed, unimplemented, unqualified. This does not close original
KL-039 or authorize a new persisted format. L1 leased lifetime and L2-A native
snapshot remain separate supporting contracts.

| Requirement | Complete operation acceptance |
|---|---|
| REQ-ONLINE-001 | AC-ONLINE-001: same-gate capture pins the actual native snapshot and captures persisted principal/policy/resource, applied position, outbox upper and node/placement identity with an active canonical projection consumer retaining necessary deltas; copied-byte seed traversal runs outside the canonical gate while genuine public update and delete commands complete. |
| REQ-ONLINE-002 | AC-ONLINE-002: a distinct staged native generation consumes actual ordered retained deltas to a finite fresh upper; full independent literals prove updated terms replace original terms, deleted entities are excluded, unchanged entities and complete metadata remain exact. |
| REQ-ONLINE-003 | AC-ONLINE-003: final authority/cut/retention and complete corpus validation precede fully flushed verified catalog publication; rejection, cancellation or exhausted bounds preserve usable original active generation and joined cleanup. |
| REQ-ONLINE-004 | AC-ONLINE-004: genuine same-root process interruption at publication boundaries and same-owner restart select the last complete active generation, preserve exact canonical records/receipts and reject incomplete or corrupt staging; process-kill proof is not power-loss durability. |
| REQ-ONLINE-005 | AC-ONLINE-005: a real old native lease keeps its exact original result and files through replacement; its final release retires only that generation, and actual cleanup failure retains ownership/failures followed by repaired healthy continuation. |

Ordered stages: freeze internal capture composition and current-only derived
catalog contract; capture metadata/native pin; bounded off-gate seed build;
ordered delta catch-up; final authority and complete corpus validation; durable
selector publication; interrupted-publication recovery; leased retirement;
actual Linux native discovery and complete operation qualification. Removing the
existing RequireUpper equality is not delta catch-up. Existing enrollment,
manifest and explicit selection contracts remain exact until the new path is
reviewed. No monitor or borrowed IKeyValueView escapes a gate.

Core/Search owns canonical capture/auth and consumer retention; provider
StorageRecovery owns native snapshot/physical-gate lifetime; Server/Search owns
staged native build, derived catalog and retirement; Orleans/Search retains the
separate authorized request grain and current maintenance coordination;
Query/Search consumes a validated selection without owning storage.
Unit/Search owns complete real-provider concurrent update/delete and reader-pin
flows; Recovery/Search owns interrupted publication and same-root recovery;
Integration/Search owns real RF3 SDK, official MCP, Q1 SDK and Q1 MCP full
literal/receipt/auth/cancellation/cleanup flows. Test symbols and compiled UIDs
will be bound from actual discovery after implementation, not invented here.
Root owns guarded live joins, compiler, commit and Linux original evidence.

Required owner decisions before production seams: exact internal provider capture
composition; generated catalog alias/field IDs/path and flush/publication order;
resolution of the existing explicit consumer/generation selection during swap.
No new public wire, trusted caller role, provider, topology, deadline or capacity
knob is selected by this proposal. Use centrally validated existing bounds.
Rollback drains real leases and stops the capable owner before rebuilding only
verified disposable derived files. Canonical documents, replication/atomic
journals and consumer records remain authoritative; no legacy reader or fallback.

```mermaid
flowchart LR
  Cut[Same-cut metadata and native pin] --> Build[Off-gate staged build]
  Build --> Delta[Ordered retained deltas]
  Delta --> Validate[Fresh authority and complete corpus]
  Validate --> Catalog[Durable derived catalog]
  Catalog --> Retire[Leased retirement]
```


## KL-039 distinct online maintenance contract, owner selection 2026-10-09

This is a docs-first source contract, not implementation or qualification.
Original Search stays read-only. Explicit TextIndexSelectionV1, its original
single enrollment, and immutable Build/Restore indexed upper U remain exact.
The new operation uses an already administrator-configured canonical projection
consumer. It cannot manufacture retention pins, caller roles or grants.

Reserve appended OperationKind.MaintainOnlineTextIndex=34; preserve Batch0 through
MovePartition33. New generated OnlineTextIndexMaintenanceRequest uses alias
keyload.search.online-text-index-maintenance-request.v1 and IDs0 CommandId(Guid),
1 Consumer(ProjectionConsumerRef),2 Collection(string),3 Field(string),
4 ConsumerGeneration(long),5 NodeId(Guid),6 Placement(PhysicalShardRecord).
There is no caller-provided cut, trusted principal, capacity or deadline.
Persisted current administrator, consumer definition/generation/checkpoint,
resource/field policy and placement are reloaded before every effect.

Reserve new generated OnlineTextIndexMaintenanceResult alias
keyload.search.online-text-index-maintenance-result.v1 with IDs0 CommandId(Guid),
1 Consumer(ProjectionConsumerRef),2 ConsumerGeneration(long),
3 BaseCut(TextIndexSourceCut),4 PublishedCut(TextIndexSourceCut),
5 IndexSha256(string),6 TrackedRecords(int),7 Checkpoint(ProjectionBatchResult).
BaseCut identifies the real initial snapshot; PublishedCut identifies final
validated applied/outbox/owner authority. No later canonical head is claimed
from an original completed result. Whole original stable-ID replay must retain
all fields exactly; changed-content same-ID request is rejected.

OnlineTextPhase uses explicit values0 Captured,1 Seeded,2 CaughtUp,3 Validated,
4 Published,5 RetirementMarked. GrainRequestProgress adds nullable OnlineTextPhase at
native Id4 only; existing RequestId0/Ann1/Text2/Move3 remain exact. This is a
closed operation phase, not a payload log or fabricated completion counter.

A distinct existing separate request grain owns the authenticated parent and
native ManagedCode CQRS ICqrsStreamWriter<GrainRequestProgress,GrainOperationReply>.
Await each actual bounded producer write; use existing stream admission/frame
limits/backpressure/request cancellation and terminal failure mapping. Exactly
one terminal result follows actual publication/settlement, never a phase emitted
before its owner completed. Producer, native readers, child operations, pin and
CTS are joined on success, cancellation and failure; preserve primary plus all
cleanup errors and original fatal semantics. No one-way delivery, new grain
concurrency attribute, polling, deadline reset or detached background task.

The parent bounded ordered loop uses existing configured maximum pages and
original request expiry/buffers. ReadProjectionBatch.ThroughSequence selects
actual retained upper; stable native record IDs/digests and original canonical
ACKs remain authoritative. New head observations allow bounded catch-up, never
remove existing immutable RequireUpper. Exhausted page/time/byte allowance or
lost retained history is a terminal refusal preserving the original active
index, followed by fresh healthy continuation in the regression.

Internal Core/Search copied-byte disposable pin + Server adapter is selected;
no public IAtomicStore addition or Core-to-provider reference. Add only Server
friendship for actual internal ZoneTree capture inside the existing gated Read
callback. Capture metadata and pin atomically; visit owned native bytes off gate;
provider retains physical iterator/slot/cancellation and joined disposal. Exact
snapshot identity and policy stay fixed within that capture. Final authority
checks compare an actually captured fresh validation cut; no count-derived cut.

The distinct current-only derived catalog is proposed as NativeTextOnlineCatalog,
generated alias keyload.server.native-text.online-catalog.v1, IDs0 FormatVersion,
1 NodeId,2 Scope(TextProjectionScope),3 Placement,4 Consumer,5 ConsumerGeneration,
6 Leaf(string),7 AppliedPosition,8 ThroughSequence,9 ManifestSha256(string),
10 BuildCommandId(Guid). Proposed same-owner root native-text-online uses
catalog.bin/catalog.pending. Root/path and catalog values need final owner review;
all native physical/file/byte/generation/lease limits account existing plus staged
and retired ownership together, not one independent allowance per directory.

Publication order: close/flush staged native index; validate complete manifest,
record digest map and entire file inventory; encode existing native SHA256 envelope;
CreateNew pending with WriteThrough/Flush(true); final fresh authority/retention
and unchanged validated cut check under original canonical gate; same-directory
atomic rename; then runtime pointer; retain borrowed old generation through last
lease and original full ownership verification before retirement. Failure before
rename preserves old selector; ambiguous after-rename failure is not converted to
no-effect or success. Startup validates the actual current catalog/manifest/cut/
owner/inventory, never promotes pending or scans for the newest leaf. Corruption
is explicit refusal, not fallback. Process interruption remains separate from
power-loss qualification; no directory-fsync guarantee is claimed.

The remaining exact storage join is the original operation outcome: selected
catalog alone cannot forget older command results after later swaps. Before seam
code, trace owning native CommandOutcomes retention/authentication APIs and freeze
where the complete original result and request digest survive canonical replay,
publication interruption, supersession and same-owner restart. No last-only
catalog receipt, unbounded file history or duplicate consumer implementation.

Shared paths are root-coordinated: Abstractions/Contracts.cs discriminator;
Orleans/ClusterRouting/Models/GrainRequestProgress.cs Id4; signed request codec,
operation descriptor/dispatch, SDK/SQL/MCP schema and gateway catalog registration.
Feature-owned DTOs/aliases/validation/owner/streaming/tests stay Search-local.
Movement/session owners were informed of reservations; guards reconcile their
actual root-joined changes before any source packet is sealed.

REQ/AC-ONLINE-001..005 remain the whole traceability set in the preceding proposal:
real concurrent update/delete during native build, exact complete search literals
and all original receipts across SDK/MCP/Q1 SDK/Q1 MCP, actual process cuts/cold
recovery, old-reader files, refusal/healthy/cancellation/shutdown full flows.
No test declaration count is compiled discovery or runtime PASS. Exact Linux
normal/scalar native source-image census and every required operation gate remain
open. Rollback drains actual owners and stops the capable node before discarding
only verified disposable derived files, never canonical data/WAL/journals/outcomes.

RetirementMarked confirms only the actual manager transition marking the old
generation for leased retirement. Successful publication can retain a borrowed
old reader and all its files; physical deletion remains owned by the final real
release. Never await a fabricated drain or report deleted files while pins exist.
The online parent does not invoke ConfigureProjectionConsumer; its capture
requires the existing configured generation and unchanged retained history.


## KL-039 canonical online publication and restart contract (v5 owner review)

This complete contract supersedes v4's unresolved original-outcome/storage join and publication ordering. It is proposed source, not implemented, compiled, discovered or runtime qualified. Original REQ/AC-ONLINE-001–005 and every original KL039 acceptance predicate remain mandatory. Preserve explicit TextIndexSelectionV1, its single enrollment and immutable Build/Restore U; implicit Search performs no canonical maintenance effect or enrollment. Public MaintainOnlineTextIndex34 and private OnlineTextPublicationPhase35 are distinct from Batch0–MovePartition33. Ordinary50/heavy RF3 exclusive1, all original cancellation/deadlines/options/auth/RF3 and required full suites remain unchanged.

### Frozen native contracts and source ownership

Public SDK extension is MaintainOnlineTextIndexAsync returning Task<Result<OnlineTextIndexMaintenanceResult>>, using the original KeyLoadClient.Send write/commandId/cancellation path. Distinct route /v1/search/text/online/maintain and on-demand tool keyload_search_text_online_maintain bind public34's typed request/result. Q1 SDK and official MCP use CALL keyload_search_text_online_maintain(@arguments) through the existing SqlOperationRequest/catalog executor. No additional SQL grammar or full-SQL conformance claim. Register exact schemas/effect hints through the existing official server and persisted-authority gateway; do not enlarge initial discovery catalog. Preserve the v4 public request alias/IDs0–6, result alias/IDs0–7, phases0–5 and GrainRequestProgress nullable OnlineTextPhase Id4.

Append private GrainReadKind.OnlineTextMaintenance only after the exact joined ControlledBlob then SampleChunkWindow ancestry; preserve all existing ordinals and take the exact native predecessor, never infer a compiled UID. Private capability purpose is keyload-online-text-capability-v1; private35 command purpose is keyload-online-text-publication-verified-v1. Both are signed through the existing GrainRequestCodec/identity restoration path. Generic public command/read purposes reject these private identities. Public generic CreateNativeOperation must continue excluding35. The distinct internal Core factory alone invokes existing IssueNativeOperation after native prepared-candidate validation; VerifyOperationAuthority and coordinator.SubmitVerifiedAsync preserve exact native HMAC/incarnation/command/kind/principal/fingerprint/payload authority. Never reinterpret movement's purpose, grants, phases or owner context.

Core/Search internal generated OnlineTextPublicationPhaseCommand uses alias keyload.core.search.online-text-publication-phase.v1 and IDs0 Request(OnlineTextIndexMaintenanceRequest),1 Result(OnlineTextIndexMaintenanceResult),2 DataEpoch(int),3 Leaf(string),4 ManifestSha256(string),5 GenerationId(Guid),6 ExpectedCurrentCommandId(Guid?),7 SessionId(Guid). Its commandId is the original public request.CommandId. Fresh signed admission binds actual parent input/principal, original request expiry, native session, prepared generation and exact payload; caller supplied results, leaf names or metadata cannot create a native candidate. Session and prepared bytes are node-local, bounded and owned; the replicated payload is copied immutable native data. Core has no provider, Server or Query type dependency.

Append nullable StoredOutcome.OnlineTextAuthority at native Id8, preserving existing0–7. Internal generated OnlineTextOutcomeAuthority alias keyload.core.search.online-text-outcome-authority.v1 has IDs0 ParentFingerprint(string),1 Collection(string),2 Field(string),3 DataEpoch(int),4 Placement(PhysicalShardRecord),5 Leaf(string),6 ManifestSha256(string),7 GenerationId(Guid). The full immutable public original result remains StoredOutcome.Result, not the derived catalog. Parent fingerprint uses the exact existing normalized Id/Kind/PrincipalId/PayloadJson identity with public kind34. Phase35 retains its independent full native fingerprint. Capture authority for a validated private phase before execution; retain it with genuine persisted terminal outcomes, while changing the current pointer only on success. No projection-batch field, RuntimeJournal identity or existing outcome format is reinterpreted.

Core/Search generated OnlineTextCurrentPublication alias keyload.core.search.online-text-current-publication.v1 uses IDs0 FormatVersion(int,1),1 PrincipalId(string),2 CommandId(Guid),3 ParentFingerprint(string),4 Consumer(ProjectionConsumerRef),5 ConsumerGeneration(long),6 Authority(OnlineTextOutcomeAuthority),7 PublishedCut(TextIndexSourceCut). The new canonical online-text-current key family is explicitly registered as partition-owned metadata for the exact consumer partition/collection/field/generation, including native roster/snapshot/key validation. No caller document or user collection stores private control data. Existing canonical outcome retention owns historical original results; there is no unbounded per-command file history.

Orleans/Search generated OnlineTextCapabilityRequest alias keyload.orleans.search.online-text-capability-request.v1 has IDs0 SessionId(Guid),1 Request(OnlineTextIndexMaintenanceRequest),2 Kind(OnlineTextCapabilityKind),3 Page(ProjectionBatch?),4 CheckpointIntent(CommitProjectionBatchRequest?),5 Acknowledged(ProjectionBatchResult?). OnlineTextCapabilityKind is closed:0 ResolveOriginal,1 Capture,2 Seed,3 PreparePage,4 ApplyIntent,5 SettleCheckpoint,6 Validate,7 IssuePublication,8 ReconcileCommitted,9 Abort. Generated OnlineTextCapabilityResult alias keyload.orleans.search.online-text-capability-result.v1 has IDs0 SessionId(Guid),1 BaseCut(TextIndexSourceCut?),2 CurrentCut(TextIndexSourceCut?),3 ThroughSequence(long),4 TrackedRecords(int),5 IndexSha256(string?),6 OriginalResult(OnlineTextIndexMaintenanceResult?),7 IssuedPublication(ReplicatedOperation?),8 CheckpointIntent(CommitProjectionBatchRequest?),9 CurrentCommandId(Guid?). These are actual phase results, never caller authority or progress counters. No raw handle/iterator crosses a grain.

The derived Server catalog keeps its accepted alias keyload.server.native-text.online-catalog.v1 and IDs0–10/root native-text-online/catalog.bin/catalog.pending. BuildCommandId10 is the actual canonical35 command identity, not an inferred newest leaf. Existing Core and Query friendship already permits Server; only the approved Storage.ZoneTree internal capture friendship is added. Internal Query ICurrentTextProjection acquires with the actual current IKeyValueView/principal/resource/request/budget inside the existing callback. Explicit selection stays first and unchanged; the current-only adapter may reuse the canonical online generation only after complete cut/manifest/corpus validation, otherwise the existing authorized bootstrap path handles implicit reads. It performs no canonical write. No borrowed view escapes, public IAtomicStore addition or Core-to-provider dependency.

### Ordered execution, original result and CAS

1. The distinct parent runs in the separate request grain and existing ManagedCode CQRS stream. Reload fresh persisted administrator, field/resource policy, active already-configured consumer/generation/checkpoint and placement; validate original expiry/cancellation/buffers. Resolve original outcome through a separately signed private capability and actual quorum barrier before new native work. Current principal policy/incarnation and exact parent fingerprint guard both original success and failure. Changed same-ID content conflicts; current revocation/policy/ownership fences remain native refusals. Later generation retirement does not erase an original completed result.
2. Capture metadata plus the actual internal native snapshot pin in the canonical gate. Core supplies a callback-based disposable copied-byte pin contract; Server composes the node-local ZoneTree adapter. Traverse the charged native bytes off gate while actual canonical update/delete operations can complete. Keep exact identity/read generation/policy within each capture. Admit only existing bounded sessions, generations/files/bytes/leases; reject exhausted admission, never create an unbounded queue or independent directory allowance.
3. Seed a distinct actual native generation; read ordered retained projection pages to observed finite upper; prepare/apply actual native intents and commit original stable-ID projection ACKs through independent child request grains. Preserve existing CommitProjectionBatchRequest/ProjectionBatchResult semantics exactly. If a genuine final empty batch has no prior ACK, execute the native empty-effects ACK to obtain an actual receipt; never manufacture a checkpoint or receipt. All extra real child calls and progress frames consume existing total frame/page/byte/time allowances.
4. Capture a fresh final validation cut, validate complete records/revisions/token digest/native inventory and current policy/consumer/placement/retention. Close/flush the prepared native generation and its original bounded native intent. The Core factory rechecks the actual prepared witness and fresh principal/cut before sealing35. Expected applied/outbox/data epoch/read generation/policy/schema/resource/consumer/placement/current-pointer identity are verified again in the actual ordered AtomicCommandCommit transaction; an intervening canonical commit is a refusal, not an invented stable count or timeout retry.
5. Existing AtomicCommandCommit.ExecuteAndBuildOutcome/BuildStoredOutcome/PersistCommandOutcome commit the full original OperationResult, typed authority, canonical current pointer and apply watermark together through the unchanged native RF3 ordered gate. PublishedCut identifies the genuinely validated document cut before this metadata publication; do not relabel a later head. No native file becomes canonical storage. Failed CAS/cancellation preserves the prior active selector and original failures. Uncertain submission remains UnknownWriteOutcome until actual original canonical resolution, never inferred no-effect.
6. Only the exact committed pointer/outcome authorizes local catalog publication. Encode the existing native SHA256 envelope, CreateNew pending/WriteThrough/Flush(true), validate exact committed identity/manifest/owner/files, rename in the same directory, then replace the runtime pointer. Preserve leased old files, mark retirement only after publication, and delete only after the final old lease joins. Await actual progress writes and one terminal result only after reconciliation/settlement. Join producer/reader/children/pin/native worker/CTS and primary+cleanup+fatal failures on every path. No one-way, detached work, request renewal, new scheduler/config knob or retry.

### Committed-authority restart and rollback

Startup/retry first obtains actual owner readiness/current committed authority; timeout, unreachable quorum or a lagging local absence cannot prove no publication. A complete local current catalog must match the exact canonical current pointer/outcome plus native manifest/owner/file inventory. No scan for newest leaf, legacy reader or blind pending promotion is allowed. A committed35 with catalog missing/pending authorizes reconstruction only from that exact recorded generation/manifest after full validation; missing/corrupt committed native bytes remain a genuine refusal, preserving the original outcome and prior verified ownership. Restore only the exact fixture-owned image in a repair regression. If no committed35 is authoritatively present, retain the original current generation and reject/reclaim only proven uncommitted owned staging. Uncertain state remains retained/unsettled.

Same-owner cold identity requires exact NodeId/incarnation/data epoch/placement/policy/schema/resource/consumer generation. ReadGeneration and applied position may legitimately advance under native committed snapshot replacement. Each new continuation acquires a real fresh current cut; it never reuses an old snapshot/cursor or rewrites an immutable Build U. Catalog/native scope and original result remain historical immutable observations. Reuse on a fresh query requires complete current canonical corpus equality, full policy/cut/manifest validation and the actual nondecreasing generation/applied fence; changed corpus is not eligible. A superseded completed command returns its exact retained original result without reactivating retired files or replacing the current pointer. Explicit selection's existing exact-generation check remains unchanged.

Rollout enables34/35 only on the coherent capable RF3 source/codec cohort; old serializers cannot consume new typed outcomes. Preserve stable aliases/IDs and existing binary/raw-byte/public JSON/digest/journal contracts. After any canonical35 is written, rollback retains capable decoding/outcome/authority support, disables new online admission and drains joined ownership before removing only verified disposable derived files. Do not downgrade to an old reader, drop committed results, remove canonical pointers or fabricate migration compatibility. No deployment action is executed by this source proposal.

### Whole operation evidence and exact implementation joins

Core/Search owns Contracts/OnlineTextPublicationPhaseCommand.cs, OnlineTextOutcomeAuthority.cs, OnlineTextCurrentPublication.cs; Commands/OnlineTextPublicationCommands.cs; Queries/OnlineTextOriginalOutcome.cs; Identity/OnlineTextOperationAuthority.cs; Validation/OnlineTextPublicationValidation.cs and Serialization/OnlineTextPublicationProtocol.cs. Shared Core joins are AtomicCommandCommit, CommandOutcomes, OperationDispatcher, native payload/authority type map, scoped command outcome resolver, native serializer alias/field IDs and partition-roster key registration. Server/Search owns native snapshot adapter, capability owner/admission, actual staged seed/delta/validation, catalog reconciliation, reader leases and retirement; StorageRecovery/PartitionHost owns registration/readiness/joined shutdown. Query/Search owns the internal current-view acquisition interface and existing SelectedTextProjectionAdmission branch only. Orleans/Search owns typed capabilities, special-purpose scopes/envelopes, sealed35 submission/partition routing, bounded CQRS parent/child calls and terminal cleanup. Abstractions/ClientApi/Client/SQL own public34 DTO/descriptor/SDK/Q1/MCP/schema/hints; private35 and capability never enter public discovery. Root joins all shared paths after peer ancestry guards, compiles/formats and commits/pushes; no worker live write.

Unit operation tests must execute real native capture/build/query while public update/delete complete off gate, then verify full independent original/new/deleted/unchanged payload/ref/revision/score/order/page oracles; actual old reader/files remain pinned through replacement; real cancellation/bounds/stale CAS/spoofed public35/authority refusal each leads to a distinct fresh healthy operation. Verify full original result after later swap/retirement and same-owner cold continuation. No getter/property/logger-only cases. Process Recovery executes actual same-root cuts before canonical commit, after canonical commit, pending flush, catalog rename, runtime activation and retirement, retaining the exact old/new outcome and native file identity; missing/corrupt exact committed artifacts refuse before genuine exact-image repair/healthy continuation. Process kill does not prove power loss.

RF3 executes the same complete concurrent mutation/ordered delta/catalog/original-result/old-reader/cold/policy/cancellation/repaired-failure flow through all four actual SDK, official MCP, Q1 SDK and Q1 MCP routes. Coordinate with genuine native phase/CQRS observations, not fake public probes, sleeps or manufactured counters. Native discovery authenticates real constructor instances/UIDs/source/PDB/image after implementation; source declarations are not compiled cases/PASS. Linux normal/scalar-caller operation outcomes, all mandatory full suites, resource/performance/coverage/endurance/power-loss gates remain OPEN.


### Native replica and captured-read settlement appendix, 2026-10-09

This source-backed corrective contract supersedes only v5's replicated35 comparison of capture-local NodeId, Store.Position and ReadGeneration. NativeTextSeedCollector captures these from its actual node-local owner under Store.Read; ZoneTreeStore.CaptureNativeReadCut requires that same raw view and held native read gate. The prepared factory and local catalog acquisition must preserve their exact node/incarnation/read-generation/local-position fence within each captured request. No old cursor or snapshot crosses a fresh cold request. Native lease traversal owns actual immutable iterator work off the store gate; seed decoding receives only charged copied bytes, and existing shared native text memory/generation/file/session admission must charge retained data and leases before allocation. This does not authorize an independent directory allowance.

AtomicCommandCommit applies the same signed35 payload through each node-local Store.Commit. A healthy follower can legitimately have another local NodeId/local journal position/read generation, especially after snapshot installation. Therefore replicated publication validates deterministic canonical Applied/outbox/policy/schema/resource/consumer-generation/placement/incarnation/current-pointer expectations and the sealed native prepared witness; it must not compare those follower-local values against the leader's captured values. The native factory validates the complete prepared candidate against a fresh exact local cut before signing through IssueNativeOperation; generic public CreateNativeOperation still excludes35. No manufactured quorum fence or caller result/witness is admitted.

DataEpoch has been verified in actual native source rather than assumed: NativeTextSeedCapture.DataEpoch is StoreIdentity.FormatVersion. ZoneTreeIdentityFile.Open/Validate/Read require CurrentDataEpoch for every supported owner; ZoneTreeCheckpointGeneration.Prepare writes that format constant and increments only local ReadGeneration for tree replacement. This format gate is common for the coherent supported RF3 reader cohort and may remain in deterministic35 validation. It is not a physical generation counter. Local catalog/source authority also binds the exact captured format. A different/unsupported format fails closed; no upgrade reader or fallback is introduced.

Actual native partial-work settlement precedes source implementation. ZoneTreeReadCutLease already charges accepted records/raw bytes before copying and retains its real partial work on traversal failure; its existing successful result exposes that work only on normal return. Add an internal ObserveSettledWork method guarded by capture completion and no active traversal, available solely to the joined Server adapter. It exports only the existing exact cumulative records/bytes/advances within that owned operation, never user data, sampled diagnostics or authority. Preserve original VisitPrefix behavior and failure identities. Server records/imports only the actual delta after synchronous traversal returned or threw.

Existing ReadExecutionBudget.ImportReadGrant and CompleteReadGrant check active cancellation/deadline before accounting, which cannot settle already performed native work after original cancellation. Add internal settled-work import/completion using the exact existing owner/completed/reservation/aggregate byte and record checks, without admitting new work or checking/renewing a future lifetime. Existing active methods retain their checks and semantics. Import actual accepted native work after traversal joined; complete/release only unused reservation after actual pin disposal successfully joined. If pin cleanup remains failed/unsettled, retain its reservation and every actual failure. Preserve primary stack identity, full ordered aggregate and fatal semantics through existing ServerFailureObserver; no retry, timing relaxation or negative-control getter tests. The pin uses only the original budget's remaining bytes/records/lifetime and cancellation.

Ordered source ownership: this feature and ADR095 contract first; internal Core metadata-only borrowed-view callback and budget-settlement append next; ZoneTree internal settled-work/state guard and Server friend adapter; exact signed35 canonical outcome/current-pointer publication; local checksummed catalog and reader-pin reconciliation; original separate-grain CQRS/public SDK/MCP/Q1 composition; meaningful real operation failure/cancellation → fresh healthy continuation, process cuts and RF3 whole flows. Shared predecessors must be refreshed against the current root-joined source before sealing. Root alone joins/builds/formats/commits/pushes; native compiled identities and complete normal/scalar Linux operations remain required. Neither this contract nor private source preparation is compilation, UID discovery or a PASS.


### KL-039 shared native ownership correction (2026-10-09)

The catalog uses the actual existing `KeyLoad.Query.Features.Search.TextProjectionScope` serializer contract; the earlier spelling `QueryTextProjectionScope` was a source-map error. Its existing aliases and field IDs remain unchanged. The catalog retains the exact frozen local owner/pin fence, while canonical operation 35 compares only the separately documented replica-common authority.

A single internal Server `NativeTextResourceOwnership` is created at the native text composition boundary and passed to the bootstrap, explicit incremental and online owners. It uses only the centrally validated existing `NativeTextExecutionOptions` ceilings. Root registration identifies canonical full paths; shared physical entries are counted once, and each native owner root remains verified against its existing node-owned receipt. Physical file, byte and actual generation accounting includes all three roots rather than giving each root another allowance. Reservation is admitted before work and remains owned until actual joined native disposal/retirement. Reader/session reservations account for actual admitted work, not inferred file counts or diagnostics.

Ordered integration: (1) introduce the internal ownership object and exact root registration/accounting; (2) pass the same object to bootstrap, explicit and online composition owners, with rollback on failed admission and retained ownership on failed cleanup; (3) serialize only physical mutation/accounting, separately from storage commit/apply and original read leases; (4) stage the online candidate while original readers retain their files; (5) commit full canonical outcome/current pointer, verify and publish the checksummed local catalog; (6) retire replaced physical media only after the actual last lease joins. Existing explicit Build/Restore upper-cut and enrollment rules remain exact.

Owned source paths are Server Search `Lifecycle/NativeTextResourceOwnership.cs`, feature-local reservation/lifetime helpers, existing bootstrap `NativeTextProjection`/lifecycle composition, explicit `NativeTextIncrementalMaintenanceService`/root composition, and online owner/catalog lifecycle. Core remains provider-independent and public `IAtomicStore` is unchanged. No new resource constants, configuration knobs, global storage lock or reader-blocking exclusive mutation rule is introduced.

REQ/AC and original KL-039 gates remain unchanged. Meaningful regression flows must execute real competing maintenance/reservation refusal followed by fresh healthy continuation, include actual native roots and original retained readers, verify no lost concurrent update/delete, and retain all cleanup failures. Unit/process recovery/RF3 SDK, official MCP and both Q1 routes still require authentic final-source qualification; this appendix is a source contract, not runtime evidence.


The later root `Grain per connection, owner decision 2026-10-09` (AGENTS.md lines 598–602 at this observation) supersedes the earlier per-request activation map. Public operation 34 and its bounded internal capability work compose the actual connection-owned execution API; each operation retains its original signed identity, fresh persisted authorization, native context, cancellation/deadline, result/progress and joined cleanup. No per-operation activation or rejected request-grain fallback is introduced. Exact connection-owner API integration and authentic matched-cohort resource/activation qualification remain required gates. Core ordered operation 35 and node-local derived-index ownership remain unchanged.


Prospective disk admission covers the existing owned native file provider and metadata envelope writer. Each admitted file mutation reserves its actual canonical path and target file length before the original native write/create/set-length begins. Shared accounting projects only the positive difference between that target and the actual observed native file length; already materialized growth is counted once. Original async/Begin-End writes must join before reservation completion. `ToStream` remains inside the same bounded forwarding adapter. Metadata envelope publication admits its exact encoded length before CreateNew/WriteThrough; checksums, flush, readback and rename ordering remain unchanged. Native source is bound to the ZoneTree 1.9.9 restored package repository revision `111908be43b42d231db0926d4503e7f864b3a6cb`, rather than an unpinned main branch. This is KeyLoad-owned resource admission, not a change to or replacement of the selected native engine.


The freshly authorized administrator's publication-head read exposes only the exact current canonical command ID needed for compare-and-swap; it does not replay another principal's original result or reuse that principal's catalog. Full original-result resolution remains bound to the original request fingerprint, principal, current persisted authorization and outcome authority. A fresh maintenance operation may therefore replace an older policy/principal-scoped derived generation using its own current pin and authority, while an old result/catalog remains subject to its unchanged refusal rules. Corrupt head format/scope/incarnation is refused; no newest-leaf lookup or authorization promotion is introduced.


ONLINE-only cold reconciliation preserves the original canonical35 outcome, pointer, manifest and source-cut bytes. It first verifies exact current committed authority, original manifest checksum and full native inventory, then captures a fresh same-owner current pin with matching incarnation/dataEpoch/persisted policy/schema/resource/placement. A complete independently bounded corpus comparison against the original manifest must succeed at the exact current outbox horizon. Only legitimate local read-generation advancement and nondecreasing applied/cut positions may rebind the disposable local catalog/acquisition fence; that fence records the real fresh generation and horizon. Canonical35 itself advances applied position without changing the indexed document corpus; its original result cut is never rewritten to the later acquisition cut. Changed corpus/authentication authority, unknown/corrupt authority or missing original media refuses reuse and requires genuine new authorized maintenance or exact-image repair as applicable. No old pin/cursor, newest/pending promotion, history effect or explicit Build/Restore/RequireUpper change is permitted. Required whole tests include native snapshot-replacement cold rejection, exact original image repair and fresh healthy full operations with unchanged original full result.


The internal prepared publication witness also retains the actual owned checkpoint intent that produced the acknowledged `ProjectionBatchResult`. Before signing private35, Core verifies that intent's exact stable command fingerprint against its scoped canonical stored outcome, matching original principal/incarnation/current policy, configured consumer and empty effects. Deterministic ordered apply verifies the acknowledged complete checkpoint result against the same scoped stored outcome bytes and current consumer checkpoint. No new serialized field, public fake-result argument, receipt fabrication or second enrollment is introduced. The ephemeral witness is not persisted or transported to followers; their comparison uses the real replicated checkpoint outcome.


The public34 MCP descriptor uses actual generated typed request/result schemas and the existing canonical command decoder/Q1 catalog. Its advisory hints are readOnly=false, idempotent=true and destructive=true: publication/checkpoint metadata changes are real, a stable command/payload replay returns its complete original outcome, and replaced disposable derived media is physically retired after leases join. Guidance requires an already administrator-configured consumer and fresh persisted administrator/data/field authority; timeout/disconnect remains an unknown write outcome. Existing31 hints and implicit query behavior are unchanged. Private35 is excluded from public descriptors and generic input factory.


### Corrective same-grant pre-copy admission (whole KL-039)

The original grant is shared by actual native key/stored-value examination and the retained decoded seed representation plus collection arrays. Settled observation alone cannot admit allocations already performed. Add an ONLINE-only internal `VisitPrefix` overload accepting an opaque `Action<long,int>` admission callback, without a Core/provider dependency. The existing overload remains unchanged. Actual native work counters retain each examined matching record or boundary key; invoke the original active grant admission immediately afterwards and BEFORE owned key/value copies. A callback refusal retains the actual attempted native observation and original exception but does not manufacture an accepted allocation. Native limits, cancellation and elapsed deadline remain exact.

Before decoding a retained DocumentRecord, charge its complete serialized representation length against that SAME grant, consistently with the existing seed materialization policy. Before allocating/replacing the List backing array and before the final array, charge the actual reference-slot capacity times `IntPtr.Size` against that grant. No independent byte allowance, renewed deadline, count-derived authority or measured-heap claim is introduced. Existing configured read-byte/record ceilings are unchanged; known simultaneous native copies, retained representations and array allocation charges consume them together.

Keep exact successful native-admission byte/record counters separately from materialization charges. After traversal joins, the actual native observer must cover those counters; a traversal that completes normally requires equality. Do not import those native bytes twice. Refused attempted work remains a real native observation and terminal refusal, without relabelling it as admitted copying. Complete unused grant reservation only after native pin disposal actually joins; retain every original traversal/admission/observation/cleanup failure and its original stack/fatal semantics. Genuine complete-operation regressions must exhaust the combined grant during actual seed traversal, settle the true pin, then execute a fresh healthy operation, and cover cancellation with the same original deadlines.


Native capture failure settlement is source-bound to `ZoneTreeReadCutLifecycle.Capture`: it always joins `lease.Dispose`, and emits an AggregateException when that cleanup fails. The online adapter completes the untouched grant on an ordinary capture failure whose native cleanup settled. An aggregate capture failure retains the reservation because cleanup ownership is uncertain, preserving the actual primary and cleanup exceptions without guessing an ownership classification. A successfully returned pin retains its grant until its actual Dispose joins.


Same-grant automated source mapping: Search/Cases/NativeTextOnlineSeedBudgetTests exercises actual configured canonical documents and native snapshot seed traversal. An independently literal full DocumentRecord corpus, exact original write time/row access/ref/revision/deleted state, and genuine native key/value sizes determine the bounded trial. The combined grant admits the first native row plus its retained representation/backing slot but refuses the next raw row before copying. Joined pin disposal is followed by a fresh full native seed and exact complete corpus equality. A separate actual captured-pin cancellation trial joins the native pin and follows with the same full healthy seed. These source declarations supplement, rather than substitute for, whole maintenance/cold/recovery/four-route RF3 flows or authenticated compiled UID qualification.


### Native connection and exact child expiry join

Re-query actual connection source before sealing: ServerConnectionOperation owns real connection Id and caller+transport Token; ServerConnectionFeature.Acquire(HttpContext, token) refuses absent actual Kestrel feature. The existing IConnectionGrain ExecuteStreamAsync and signed AlwaysInterleave CloseAsync remain the owner boundary. Online parent and its sequential child operations use the SAME actual connection identity, each with fresh scoped signed operation identity/current persisted administrator authority. They consume existing MaximumConnectionOperations admission (current default8); no hidden slot expansion, per-operation fallback grain, session role cache or synthetic connection is introduced. Existing bounded non-client owner identity is used only by its genuine existing non-client context.

Add owning Search partial GrainRequestCodec methods for exact original-expiry children; old public codec methods/callers remain unchanged. An internal capability read uses the frozen keyload-online-text-capability-v1 purpose and only OnlineTextMaintenance read kind. A factory-issued35 command uses that same closed envelope purpose and exact original expiry, with VerifyOperationAuthority proving the existing distinct native35 HMAC before signed envelope construction. Payload is the actual verified ReplicatedOperation, not a caller supplied full result or reconstructed phase command. Closed scope validation accepts this purpose only for that capability read or35 command, and rejects those private kinds under another purpose. Core/native publication independently verifies original operation authority before existing SubmitVerifiedAsync. ProjectionBatch reads and CommitProjectionBatch child writes retain their existing standard purpose, fresh identity and native semantics, but exact capped parent expiry. Original parent cancellation/frame/page/byte bounds and joined producer/session/child cleanup remain mandatory; all prior per-request grain prose is historical and superseded by the actual owner connection rule.


Online frame accounting is operation scoped under the SAME original MaximumTotalFrames. The parent has already awaited its actual StartedAsync before entering the maintenance body; keep that observed frame and reserve its single framework terminal frame. Wrap each actual native child enumerable and admit each yielded CQRS chunk before the existing native drain consumes it; join the original child enumerator on rejection/cancellation/disposal. Parent progress frames consume that same allowance before the real ProgressAsync. Do not infer child frame totals from source declarations/page counts or grant an independent allowance. The existing native serializer/drain and original frame-byte/result/page limits remain unchanged. A terminal refusal and its joined producer/session cleanup retain the original failure; no retry or renewed lifetime is added.


Shared accounting registration binds each actual root receipt to its exact node owner and retains original secure regular-file/directory traversal. Its callback is resource inventory admission, not a replacement for explicit enrollment/read validation or canonical online publication proof. Do not call the explicit CheckRoot enrollment validator recursively from a prospective metadata-file reservation: it would reject the legitimately incomplete, already reserved generation between directory creation and its owner/enrollment publication. Original explicit CheckRoot/RequireUpper/layout checks remain at their existing admission/read/settlement boundaries; online current reads additionally require exact committed outcome/pointer/manifest and complete fresh corpus proof. Whole-index EvictToDisk/WaitForBackgroundThreads and joined index disposal must remain outside the shared reservation lock, because native writers use that same lock for their actual pre-write reservations. Serialize short actual file/create/replace/metadata mutations and accounting without awaiting or synchronously joining a native worker while holding that lock.


### KL-039 original coordinator settlement admission

The internal online publication owner retains the exact `AdmittedCommand.Completion` returned by the existing coordinator inbox admission. The public `ICommitCoordinator` remains unchanged. Only verified `OnlineTextPublicationPhase` operations may use the internal retained-completion entry; the existing worker, principal lookup, command-size limits, evaluated timestamp and admission governor remain authoritative. The Orleans operation boundary admits this native task, while the feature-local session retains it through caller cancellation and joined cleanup. `WaitAsync(callerToken)` cancellation may report `UnknownWriteOutcome`; it neither settles that original task nor permits catalog/staging release. Even an original task terminal `UnknownWriteOutcome` requires authenticated canonical current-publication reconciliation before generation retirement or promotion; no absence is inferred.

This change is an internal ownership seam, not another dispatcher or public store API. The normal coordinator submission continues to use the same inbox entry and original cancellation mapping. Unknown or failed reconciliation retains ownership until explicit joined shutdown or an authenticated recovery decision. Qualification requires the genuine queued publication, cancelled caller, original task settlement, canonical reconciliation and fresh healthy maintenance flow; source construction is not runtime evidence.


### KL-039 owned native file groups and detached streams

The shared native resource owner assigns a disposable file group only after actual successful native open/create, under its short physical gate. All linked handles for that same canonical path share one group, counted once; the group is not database authority or a fabricated persistent identity. Every native stream and `ToStream` write is charged through that owner. Successful native delete detaches its group. Successful native replace moves the source group, transfers the original destination to its actual backup when present, and detaches overwritten groups. Detached native handles retain actual length and prospective write reservations until real stream disposal joins. Native mutation failure preserves the original exception and retains affected groups conservatively as ambiguous; no exact-byte qualification is claimed for that state and no directory scan releases handles or infers identities. Existing MaximumFiles/MaximumDiskBytes remain the only ceilings. Actual registered open streams, not pathname absence, govern release. This applies to bootstrap, explicit and online native providers under the same owner; corruption/enrollment/manifest validation remains independent.


### KL-039 catalog proof construction and publication recheck

A disposable online catalog proof is constructible only by authenticating the actual canonical current-publication pointer and its original retained outcome, validating the immutable original manifest envelope checksum and complete native inventory, then capturing a fresh owned native seed and comparing every reference/revision/deletion/full canonical digest with the original manifest. The manifest's original scope binds the original result BaseCut; its final horizon and complete files bind PublishedCut and IndexSha256. The fresh local catalog records the genuinely captured current position, applied cut and read generation. Scope identity, incarnation, data epoch, persisted administrator, policy, schema, resource digest, configured consumer and physical placement remain exact; only position/applied/read generation may advance. ThroughSequence and complete corpus must still match. This does not modify explicit enrollment or RequireUpper.

The proof owner must already retain that real generation against retirement, and must re-read the authenticated canonical pointer before catalog rename under its exclusive publication ownership. A changed pointer refuses publication. Pending bytes are read back and compared with the exact prepared catalog; no raw DTO or integer-only comparator creates a cold rebind proof. Original canonical bytes and original caller result remain immutable. Fresh acquisition must match this fresh scope under the existing read-cut admission; proof construction alone does not authorize later stale query acquisition.


### KL-039 stable online checkpoint child identity

Online checkpoint IDs use the distinct native serializer alias `keyload.orleans.search.online-text-checkpoint-identity.v1`: Id0 original public maintenance CommandId, Id1 actual projection page ThroughSequence. The child Guid is the first 16 bytes of SHA256 over that native typed serialization. This preserves stable replay while separating the distinct online operation from existing explicit-maintenance checkpoint identities; existing aliases/IDs/digests remain unchanged. No caller can supply a trusted checkpoint result: every page is read through the actual authorized projection API, its original token is committed with empty Effects, and the native stored outcome and current configured consumer checkpoint are verified before publication.


### KL-039 native session deadline and active capability cancellation

A retained online seed pin belongs to its bounded feature session, not to a completed child operation. The session CTS uses the earlier of the original signed absolute expiry and the centrally configured original QueryDeadlineSeconds, linked to feature shutdown. Its single cumulative ReadExecutionBudget is constrained to that same original expiry and never renewed. Every active capability uses the already-native `ReadExecutionBudget.EnterStageCancellation(actualCallerToken)` scoped lease; budget checks and the pre-copy native grant admission observe that caller cancellation during real traversal. Successful child completion disposes only this scoped stage lease, without cancelling the retained seed pin. Parent failure/early stream disposal joins actual Abort; abort and shutdown preserve original35 completion/reconciliation ownership before disposal. No caller role or completed request-context cache is retained.


### KL-039 canonical native generation locator validation

The internal35 payload retains the actual native `generation-` plus lowercase Guid `N` leaf format. The separately retained GenerationId is exactly that native leaf Guid, not another generated identity. Core validates this frozen locator and canonical lowercase SHA256 shape without a provider-type dependency, validates same-node/incarnation base/published lineage and nondecreasing original captured positions/generation/horizon, and enforces the existing MaxScanRecords bound. Server obtains the locator from actual native generation creation and computes hashes from the real closed native inventory/manifest. This shape validation does not substitute for full candidate validation or imply a published result.


Closing native file handles first join every real write, then capture their actual final length under the short resource gate. That observed length remains charged while actual close runs outside the gate; concurrent accounting never accesses a partly disposed stream and never releases it from a directory scan. Concurrent sync/async disposal joins one shared native close task and retains all original write/close/accounting failures. Only actual joined closure removes the handle.


The native open-file ownership regression performs real provider create/write/flush/open/unlink, attempts a genuine bounded second write while the first native handle remains open, requires that rejected write to leave the complete second file empty, joins the actual first handle close, then repeats the genuine write/read with complete independent bilingual bytes. Its typed disk bound derives from the fixture's actual existing owned files plus both original payload lengths minus one; no guessed authority counter or changed global default is used. This is a component operation regression supporting the shared owner, not the full public maintenance/RF3 acceptance proof.


### KL-039 genuine checkpoint original completion ownership

Before the real checkpoint15 child is submitted, PreparePage registers one session-owned expected native CommitProjectionBatchRequest with exact command bytes, stable ID, principal and original signed expiry. Existing bounded sessions hold at most that current checkpoint work and their publication work; existing connection operation/grant ceilings remain unchanged. Physical command execution matches every original value before using an internal coordinator entry returning the actual AdmittedCommand.Completion. Ordinary unmatched checkpoint callers retain their existing path. The feature work lock retains this actual task atomically with native enqueue and subsequent matching submissions join the same original task.

An unknown checkpoint RPC does not permit intent retirement, generation deletion or ownership release. Cleanup joins the exact original native completion and verifies its original native receipt before retiring that intent; unknown/missing authority remains retained. Publication35 follows the same original-task rule. No new operation discriminator, public store API, consumer grant, fabricated checkpoint result, automatic retry or deadline is introduced.


Session cleanup closes new checkpoint/publication admission and captures the original native task under the same feature work lock used for enqueue. A child that already obtained the work cannot enqueue after this closure; an already admitted original remains joinable. Each session retains at most one current exact checkpoint registration. Its original native request bytes, command ID, authenticated principal, original expiry and actual page horizon are captured before submission. Matching another request with the same registered command ID but different bytes/identity/expiry refuses; ordinary commands with an unrelated ID use their original path. Native completion success is verified as the original typed ProjectionBatchResult with its exact command receipt and actual intended horizon, and the caller acknowledgement is compared in full under the original active budget before intent retirement. Unknown completion or failure cannot manufacture this proof. Cleanup closes both work admissions before joining their actual original tasks; retained uncertain work keeps its generation/resource ownership.


While the real seed pin reserves the original remaining read grant, exact subsequent request revalidation representation/hash bytes are admitted before allocation through that same owning grant, separately from actual native examined-byte/record observations. They do not borrow a second budget or fabricate native work. After actual pin disposal joins and the remaining reservation is settled, later stages charge the original cumulative budget directly. Original deadlines and total byte ceilings remain unchanged.


### KL-039 same-view readonly implicit acquisition adapter

Query owns internal ICurrentTextProjection, receiving the actual existing WithQueryView view, fresh persisted principal/resource, original SearchRequest and original budget. Server implements it alongside unchanged ISelectedTextProjection. SelectedTextProjectionAdmission and WaitForIndex use that same-view adapter only for implicit selection; no nested Store.Read, new read cut, checkpoint/publication, catalog-file write or await occurs under the canonical read lock. Existing Query→Core project reference and Server friendship remain unchanged; Core lookup uses only Core/Abstractions types.

The bounded canonical online-current family lookup validates exact keys and full retained native authority. Only the actual matching local node, persisted principal, collection/field and active administrator-configured consumer is eligible. A genuinely released or positively superseded consumer generation is ineligible; unknown/corrupt matching authority refuses. Two eligible configured consumers are ambiguous and refuse, without newest/lexical selection. No eligible online authority retains the existing readonly bootstrap path for that distinct scope; corrupt/mismatched eligible online authority never falls back. Acquisition validates immutable original manifest/checksum/native inventory and full canonical corpus in the same actual query view, compares the resulting fresh scope exactly with the original admitted query scope, and transfers ownership from the inspection pin to the actual native reader lease before the inspection pin is released. The query neither promotes a caller nor configures a consumer.


Canonical `online-text-current` participates in the actual ordinal PartitionRecordFamilies.All after message-meta and before outbox, and in native movement control-owned metadata beside canonical outcomes. Existing AtomicPartitionRosterKeyValidation consumes that same roster, retaining original current authority through snapshot/backup/movement without transferring open derived handles. The new-maintenance capture uses the separately frozen fresh-authorized ReadOnlineTextPublicationHead CAS identity, not original-result replay admission: legitimate changed placement/policy may require a genuine new prepared35 publication. Original outcome replay/current query validation remain strict and never reinterpret old authority.


The host's shared-resource composition pre-registers the three actual owned root paths (`search-indexes`, existing `text-projections`, and `native-text-online`) before any new root receipt or native generation work. Re-registering the same exact canonical root validates both existing and new owning verifiers and keeps one accounting entry; overlapping distinct roots still refuse. No root registration releases handles or infers their identity. The original native bootstrap/explicit constructors remain available for existing standalone fixtures, while the real host supplies one identical shared owner to all three paths, provider streams, metadata writes, generation reservations and real read leases. Their existing local semantic checks remain unchanged.


### Finite shared-owner composition completion (2026-10-10)

The existing PartitionHost composes one NativeTextResourceOwnership for bootstrap, explicit text-projections and online native-text-online roots before opening any of those services. Each original root receipt and its existing full ownership validation remains authoritative; the shared accounting verifier validates the root receipt during short staged file publication, while the original complete generation/enrollment validators still run at their original admission and completion boundaries. It does not interpret a partially written enrollment as a completed generation.

Bootstrap generation, explicit maintenance session, online maintenance session and actual selected-reader reservations use the same existing limits. The existing explicit maintenance service retains prospective generation reservations in its bounded live-generation registry until the actual owning delete succeeds after all native owners/readers close; aborting a session does not release files that intentionally survive for Restore. No extra service owner, request dispatcher, ceiling, public operation, deadline or history registry is introduced. A failed create/delete keeps unresolved ownership and the original failure; only actual joined closure and an owning successful deletion release its reservation. The original explicit enrollment, immutable Build/Restore U, RequireUpper and checkpoint receipt contracts are unchanged.

The executable integration endpoints are the existing PartitionHost open/shutdown boundary, OrleansSiloConfiguration.RegisterBorrowedServices and canonical ClientApi long-operation endpoint registration. All resource adapters remain feature-local and internal. Their source does not establish compiler, native discovery, runtime acceptance or resource measurements.


### Exact catalog recovery and read admission completion

An implicit online read must validate the actual checksummed catalog.bin against the already proven canonical current publication, original manifest and complete corpus. Missing or mismatched current catalog refuses the online read; the read never writes or promotes catalog.pending. An online-only generation rebind may use a catalog with an older read generation/position/applied/horizon only after the same-view full proof shows identical node, incarnation, data epoch, partition, collection, field, principal, policy, schema, placement, consumer, generation, leaf, manifest and original command. Every fresh fence/horizon is nondecreasing and acquired within that actual query view.

Fresh authorized ResolveOriginal uses a separate fresh request budget, never extends the original operation expiry, joins any retained exact original coordinator work, validates the full positive canonical receipt/current/manifest/corpus authority, and repairs the disposable local catalog before returning the exact immutable original result. A retained pending file can be removed only after checksum/shape/identity validation against this fully proven current publication; it is never blindly renamed. Unknown, corrupt or changed pending authority refuses. Only successful original join and actual catalog commitment permit the retained session/staging ownership to retire.


Stable-result replay after supersession reads the original verified StoredOutcome Id8 authority and immutable full result through the existing Core outcome resolver. This internal read-only resolver reconstructs the original publication identity solely from those authenticated persisted fields; it never writes a current pointer or accepts caller publication fields. A retained original session may transfer its reservation to that exact original generation only after its actual coordinator completion and full original receipt join. Once the fully validated newer current catalog is acquired, the superseded original generation retires through the same actual reader-pin join; replay still returns its immutable original result.


### Whole executable operation regression ownership

REQ/AC-ONLINE-001/002/003/005 map to NativeTextOnlineWholeOperationTests using the actual fixture-owned native ordered apply/materializer, native capture/seed/delta/publication and original receipt APIs; this is Unit evidence, not Docker RF3. The real pinned snapshot is acquired before actual native update/delete commits, and the published complete bilingual literal corpus, deleted exclusion, original result replay and held reader/file retirement are independently asserted.

NativeTextOnlineRf3Tests.OnlinePublicationRefusalUpdateDeleteFullReplayAndColdContinuation has the four original NativeTextMaintenancePath arguments, uses the canonical fixture-owned Aspire RF3 entry and official SDK clients, and maps ONLINE-001/002/003/004 to persisted administrator consumer configuration, all-four-route owner refusal followed by complete healthy publication/search, native update/delete, exact full original result replay and same-owner cold continuation. Only authentic Linux discovery can bind compiled cases; source arguments are not UIDs or passing qualification. Original deadlines/options/heavy scheduling remain unchanged. The full native original failures and every owned client/container shutdown are joined. Genuine publication-boundary process interruption remains its separate mandatory recovery suite.


### KL-039 online staging identity and cold recovery completion contract (2026-10-10)

REQ-SEARCH online generation lifetime / AC online recovery: `native-text-online/<generation>/staging.bin` is ONLINE-only generated `keyload.server.native-text.online-staging.v1`, checksummed and written CreateNew/WriteThrough/Flush(true)/readback after the native owner receipt and before native seed writes. Stable fields are Id0 format, Id1 actual owner NodeId, Id2 canonical leaf, Id3 complete immutable public maintenance request, Id4 persisted principal identity, Id5 existing canonical principal/operation/request fingerprint, Id6 captured expected current command (nullable), Id7 actual original captured source cut, Id8 actual observed DataEpoch. No expiry or absent outcome authorizes deletion.

Ordered owners: NativeTextOnlineSeed creates and verifies this receipt under the original resource grant; NativeTextOnlinePublication verifies complete generated identity equality against its retained original session before issuing signed canonical35. NativeTextOnlineRoot verifies the complete ONLINE-only generation metadata roster and regular-file/type boundaries before native inventory validation or actual joined deletion. Existing explicit Build/Restore and bootstrap layouts remain unchanged. An owner-only partial directory or a staged receipt without positive committed authority is retained and charged; cold lookup never promotes it, rewrites its parent, or calls it absent. Only a full positive canonical original outcome plus exact committed current publication, full native manifest/inventory/corpus proof and joined original work/native ownership may reconcile its disposable catalog. If ownership settlement cannot be proven, refusal retains original staging and failure.

Recovery tests must kill real CrashHost at actual canonical35 completion and pending-catalog Flush/readback, then reopen the same native root, verify full original receipt/current authority and complete bilingual results, reject exact checksum corruption, restore the original fixture bytes and perform fresh healthy maintenance. Process kill is not power-loss durability. RF3 retains all four caller routes and same-owner cold continuation. Compiler/discovery/runtime and resource qualification remain open until authentic runs; no source declaration is a native UID or PASS.

Canonical parent correction: staging Id5 is the EXISTING `OnlineTextParentIdentity.Fingerprint(persistedPrincipalId, originalRequest)` canonical principal/operation/payload identity, not a second request-only digest. The complete generated request bytes are independently compared. Existing authority Id0..7 remain exact (Id7 GenerationId); append nullable Id8 ExpectedCurrentCommandId, populated only from signed35. Cold staging proof compares this original expected head and original BaseCut with the positive native StoredOutcome result and current authority; no mutation of the receipt. Native fault-stage values0..9 remain exact; append actual original35 joined callback10 and pending-catalog Flush/readback callback11.


### KL-039 finite executable wave source and authentic qualification gates

The proposed owning wave implements the native snapshot capture → bounded seed → exact ordered replay intent → joined original15 checkpoint → complete inventory/corpus validation → signed35 full canonical original outcome → checked disposable current catalog → old-reader retirement flow. The concrete production entry owners are Core OnlineTextPublicationFactory/Commands/OriginalOutcome, Orleans OnlineTextExecution/OnlineTextParentFlow/OnlineTextChildCalls, Server NativeTextOnlineMaintenanceService and NativeTextHostSearch, existing PartitionHost/OrleansSiloConfiguration/SearchApi, actual connection-bound CanonicalOperationGateway, SDK OnlineTextIndexMaintenanceClient, official MCP OnlineTextMcpCatalog and canonical Q1 operation catalog. Existing request and connection settlement owners remain authoritative.

Automated source map (source declarations, not compiled discovery): six NativeTextOnlineWholeOperationTests bodies cover full ordered publication/bilingual literals/original replay, already-cancelled admission→healthy, real competing captured owners at the actual centrally configured shared lease ceiling→joined abort→healthy, delete/swap→original replay, true pinned snapshot with later update/delete and ordered tail, and an actual retained native reader across swap/cold healthy. The existing supporting new NativeTextOnlineSeedReader tests cover genuine byte refusal/cancellation→joined pin→healthy and the native file ownership case covers unlink with a live handle→bounded refusal→actual close→healthy. NativeTextOnlineProcessRecoveryTests has two native argument declarations for actual completed35 and pending-catalog flush kill/reopen boundaries, with full original mutation/publication receipts, complete bilingual deleted-exclusion literals, staging checksum corruption→exact fixture bytes repair→fresh authorized healthy publication and second reopen. NativeTextOnlineRf3Tests has four original caller argument declarations (SDK/MCP/Q1 SDK/Q1 MCP) for ownership refusal→healthy seed/update/delete, full independently literal results and exact original receipts across all four routes, then same-root/node cold continuation. Full official MCP schema/effect-hint expectation inventories add exactly the new public tool to the current actual 79-entry source inventory; 80 is a source catalog expectation, never a discovered native-case count or PASS.

Remaining gates are explicit: guarded owner review/join, canonical native compiler/analyzer/formatter checks, actual generated serializer contracts and native discovered source/PDB/image/UID binding, normal and scalar Linux Unit/process/RF3 execution through the canonical Aspire/TUnit composition, actual resource/latency/backlog/old-reader concurrency qualification and endurance/fault qualification. The authored RF3 flow proves four-route publication/read/replay/cold operations; actual overlap of a public RF3 reader retained while publication completes still requires a genuine caller execution observation and is not inferred from the Unit native held-reader case. Process kill proves process recovery only. No status/task acceptance or production-readiness promotion accompanies this packet.


### TASK-KL039-PUBLIC-RETAINED-READER: finite original public reader overlap

This successor implements the frozen original REQ/AC-LEASE-001..004, REQ/AC-ONLINE-001..005 and REQ/AC-CUT-001..004 using the current connection-owned execution contract in ADR-125. Public Search DTOs/transport IDs, explicit TextIndexSelectionV1 enrollment and Build/Restore U/RequireUpper, original deadlines, one native snapshot, two shared active native leases and three physical generations remain unchanged. Implicit online selection retains the exact SAME publisher/principal/partition/collection/field/native-owner scope. Nonadministrator/bootstrap acquisition remains distinct; this wave does not broaden that selector or promote a query caller.

Ordered implementation and join points:

1. Query SearchCapturedExecution validates the original request/fields/budget inside DatabaseEngine.WithQueryView. Server NativeTextCapturedReadFactory acquires that SAME actual selected native text reader and canonical ZoneTree snapshot under this SAME authorized gate. NativeSearchReadAuthority contains only observed scalar owner/incarnation/format/read-generation/position/applied/policy/schema/resource/placement identity; no signing key, credential, live view or trusted role cache escapes.
2. StorageRecovery owns internal IScopedReadCapture plus ZoneTreeCapturedReadBuilder/View/Lookup. Original native snapshot callbacks charge actual examined key/value/record work BEFORE copies. Capture the bounded target document prefix, exact resource metadata and, for hybrid search, exact vector/lineage/effect/source-resource/source-document dependency closure. Completed prefix exhaustion or an actual point lookup admits only that exact scope and observed absence; an unadmitted lookup refuses without a live-store fallback. Captured byte keys/values and scope/index/list/array capacity stay on the SAME original grant. Conservatively modeled array/header/decode representation charges are separate from native work and are not measured CLR heap/RSS claims. There is no whole-database map or new limit.
3. Release the canonical live gate before off-gate closure work. Join the SAME native snapshot/iterator completely, then settle only unused reservation; accepted retained charges remain on the original cumulative ReadExecutionBudget until original operation disposal. Retain the SAME independent FTS reader through actual posting visitation/ranking/projection. No storage lock spans await, no nested Store.Read, no extra reader and no delayed response after the original reader was already released.
4. Before exposing ANY result, NativeSearchTerminalPrivacy reacquires fresh persisted principal/resource/placement and validates original owner/incarnation/format/read-generation/policy/schema/resource identity. Normal committed position advancement is allowed; changed authority, expiry/cancellation, revocation or current selected/source row privacy refuses the entire result. Original-cut document/vector/revision/rank/projection semantics remain exact. A revoked publisher repair strictly advances policy epoch and requires a genuine NEW authorized35 maintenance; old policy-bound replay refuses and its complete stored outcome bytes remain immutable.
5. ConnectionReadCapabilities uses an armed call-local NativeTextPostingObservation scope only around the existing Query.ExecuteAsync path. The SAME selected native posting iterator reports appended closed phase NativeTextOriginalPostingRead after a genuine Next while its original lease is still owned and after snapshot join. Existing authenticated codec/context/probe observation owns the bounded hold, cancellation and original callback task join. No per-operation activation, diagnostic payload/history, transport fallback or extra native reader is introduced. Existing phase ordinals remain exact; the new phase is appended.
6. Original reader/task and callback cancellation/disposal join before releasing owned data. Query exposes direct owner.Dispose in its finally and aggregates original plus cleanup failures outside finally. Unknown publication outcomes never authorize retirement, catalog promotion or absence. Root alone joins guarded source and performs native compile/generated serialization/analyzer/formatter and authentic runtime qualification.

The fixture obtains the actual current leader from native Status.Leader and exact signed discovery VoterId without retry/new timing. Its real persisted c1-probe administrator has explicit query/document permissions, configures the canonical consumer and publishes/reads the exact same scope through the real SDK/official MCP connection. All original topology/options/deadlines/resource limits remain unchanged.

Source test mapping (declarations only, never native UIDs/PASS): NativeTextCapturedPublicOperationTests has a two-argument genuine public Search positive/cancel flow plus persisted revocation→no-result→exact policy repair→NEW canonical publication→healthy/cold a genuine same-owner snapshot install while the SAME original public FTS reader remains held→old-cut TokenInvalidated→fully proven fresh read-generation rebind/full replay/cold, and one centrally validated tiny-grant admission refusal→actual owner join→full public healthy/original replay/cold. The positive/cancel flow verifies complete original literal JSON/reference/revision/redaction/score, actual original canonical manifest and complete native file lengths/SHA inventory while its public reader remains held, successful replacement update/delete receipt and full healthy/replay/cold continuation. NativeTextCapturedRf3Tests has eight declared parameter combinations: SDK/MCP/Q1 SDK/Q1 MCP × original completion/cancellation. Every original public operation holds its SAME actual native posting reader while a genuine update/delete and complete34/35 publication finishes; authenticated marker/producer settlement, full old literal or canonical cancellation refusal, all-four-route full mutation/publication replay and independent fresh literals, and same-owner actual RF3 cold restart remain mandatory. No compiled identity/count is inferred.

Remaining authentic gates: native compile/analyzer/format; full normal/scalar Unit/recovery suites; exact-source Linux native discovery/source/PDB/DLL/image/cleanup binding for the expanded RF3 inventory; actual eight overlap executions; snapshot-replacement/authority invalidation and nonadministrator/bootstrap privacy scope qualification; measured retained memory/resource/backlog/latency and endurance/fault gates. These source tests do not claim RF3 persisted-revocation qualification or that every legitimate snapshot replacement permits the old operation to return data. Explicit authority invalidation must refuse. No source count, private synchronization or process-kill result promotes acceptance/production readiness.

```mermaid
flowchart LR
    Gate[Fresh authorized live gate] --> Capture[Same selected FTS reader and one native snapshot]
    Capture --> Release[Release live storage gate]
    Release --> Closure[Bounded owned original-cut lookup closure]
    Closure --> SnapshotJoin[Join snapshot and settle unused reservation]
    SnapshotJoin --> Posting[Same native posting reader retained]
    Swap[Real update delete and canonical swap] --> Posting
    Posting --> Terminal[Fresh persisted terminal privacy]
    Terminal --> Result[Full original result or refusal]
    Result --> Join[Original reader callback task data joins]
    Join --> Healthy[Fresh full replay and cold continuation]
```

Rollback removes only this read execution adapter after closing admission and joining original public operations, callbacks, snapshots and native FTS readers. Existing canonical34/35 outcomes/pointers, explicit index manifests, source data, provider version and public transport IDs remain unchanged. Root owns integration/evidence; sol61_read_finish owns this immutable private successor; the independent KL037 additive connection-router fragment composes AFTER this exact final ConnectionReadCapabilities ancestor and does not replace this query scope.


### KL039 R23 native maintenance lifecycle/compiler correction

This source-only correction preserves REQ/AC-ONLINE-001..005, REQ/AC-LEASE-001..004 and the complete sealed public retained-reader operation flows. `NativeTextOnlineMaintenanceService` retains its exact worker, shutdown token, sessions, native root and coordinator-original completion. Its same native generation dictionary/gate, active generation and selected-read admission now belong to the composed `NativeTextOnlineCatalogOwner` in the existing Catalog/Reads/Retirement owning paths. `NativeTextOnlineStageExecutor` preserves each original stage dispatch and its same authority callback; `NativeTextOnlineOriginalReplay` borrows those same owners and original cleanup function. No second state, cache authority or new request/grain/context/transport contract exists.

Ordered joins remain: close session admission and original child completion; cancel/join the real worker; join session cleanup, original retirements and original readers; dispose every already-retiring generation's native index; directly dispose the original shutdown source. `NativeTextOnlineGeneration.Dispose` refuses unless original retirement has already been marked and all real reader pins have joined. It neither deletes a directory nor releases a generation reservation. The existing final actual directory deletion remains the only release authority. Failed native index settlement retains the real pin/grant and now preserves every original settlement fault in both returned and retained failure ledgers.

Acquired catalog proofs and native projection leases transfer explicitly. Failed transfer disposes the real acquired owner; successful transfer keeps it owned through the original reader's final joined close. Visible direct disposal and outside-finally primary/cleanup aggregation preserve original exception stacks and all genuine additional cleanup faults. Domain constants name original zero/count values without changing limits. The PartitionHost failed-construction cleanup helper preserves the exact original construction and cleanup sequence, physical stores and all opened native owners.

The owning source paths are the five existing Execution maintenance partials, `Lifecycle/NativeTextOnlineRetirement.cs`, `Lifecycle/NativeTextOnlineGeneration.cs`, `Storage/NativeTextProjection.cs`, the two new feature-local stage/replay executable helpers, and `Features/StorageRecovery/Hosting/PartitionHost.cs`; responsibilities remain within the existing Search/StorageRecovery slices. Native file streams, writes, file groups and digest disposal remain in their distinct owning correction packet and original resource interfaces. Rollout joins docs first, then the coherent guarded owners; rollback restores that entire source checkpoint before executing it, with no persisted/wire migration or fallback.

Validation must run the canonical coherent build/analyzers/format, all original native online publication/cancel/shared-admission/update/delete/reader-retirement Unit flows, the five public retained-reader Unit source instances and eight RF3 four-route source instances, process recovery and Linux hardware/scalar source-image/UID/cleanup binding. Existing test declarations are retained; source structure or static length/literal inspection is not compiler/runtime PASS. Actual observed whole operation outcomes remain mandatory, including unresolved earlier original failures.


### TASK-FTS-NATIVE-PHYSICAL-FILE-JOIN-001: original write and physical cleanup ownership

REQ/AC-LEASE-001/002/003 and REQ/AC-ONLINE-003/004, retaining REQ/AC-FTS-003/004/005 retain native physical files, exact disk reservations and genuine joined shutdown. Before implementation, freeze this owning correction: one short native Lock protects active write/position work, closing and retained settlement failure. Concurrent mutation returns existing Busy immediately; close marks admission closed and joins the actual existing completion Task without a new timeout, semaphore, configuration or background writer. Completion occurs only after original native work and reservation settlement; closing cannot release an unjoined file.

NativeTextBoundedStreamWrites, NativeTextBoundedFileStream, NativeTextFileStreamProvider.Open, NativeTextOnlineManifestDigest and NativeTextResourceOwnership.Files retain initiating, fatal and cleanup exception identities through the existing ServerFailureObserver ledger. Original physical mutation failures mark ambiguity before later layout checks; bookkeeping failures cannot replace the original mutation failure. Failed open joins the exact created stream before its initial reservation settles, and unsuccessful native close retains ownership. Stream.DisposeAsync invokes the native base implementation even after joined close failure; duplicate observation of the same cached close exception is not a second physical failure. Digest uses the original native FileStream and SHA256 with direct visible cleanup, no fallback/copy.

Automated operation mapping: NativeTextOnlineFileOwnershipTests.OriginalCancelledWriteAndAsyncJoinedClosePreserveBytesThenFreshHandleReadsCompleteHealthyFile and the original genuine unlinked-handle disk denial, full byte write/read and joined close; NativeTextFileStreamProviderTests real replacement/no-effects/healthy operations remain intact. Add meaningful native cancelled-write/DisposeAsync/complete healthy file continuation and actual failed physical mutation/full cleanup aggregation controls using existing real file ownership. No mock provider, suppressions, payload/format/limit/time changes. Root owns coherent build, normal/scalar native execution and exact-source Linux recovery/RF3 qualification; source alone grants no PASS. Rollback restores these five owners and removes only additive tests/contract before release.


### R26 original reader identity and settlement correction (2026-10-10)

REQ-SEARCH-ONLINE-001..005 and REQ-SEARCH-LEASE-001..004 retain their original public/signed/persisted contracts, cancellation, snapshot, native resource and deadline ceilings. Internal selected native Enter/Exit bind the same existing NativeTextSelectedProjectionLease instance by reference identity; this identity is local lifecycle ownership only and supplies no authority. The native slot and online generation retain reference-keyed original reader registrations rather than a LIFO association. Concurrent A-enter/B-enter/A-exit must settle A alone while B continues the complete original public read.

Ordered implementation: (1) original read budget admits retained ledger capacity/entry native field sizes and conservative existing map-frame overhead before allocation; no new quota or claim of exact measured CLR heap; (2) record the exact selected reader before opening its native index, retaining registration/failure on uncertain open; (3) transfer original pin/grant only after successful ledger insertion; (4) actual per-reader exit closes its native registration, with final-reader removal only after successful actual index close; (5) dispose that exact original pin; (6) complete its grant only after successful pin closure; (7) remove the owned generation entry only after all actual settlement succeeds. Every failure retains original entries/charges and joins original plus cleanup exceptions. Failed Enter may clean its own pin/grant only after the actual slot proves this exact reader has no registration; another active reader is not a global settlement predicate. No AsyncLocal, copied authority, retry, fallback or new owner layer is introduced.

Owning implementation is INativeTextSelectedIndexLeaseOwner, NativeTextSelectedProjectionLease, NativeTextSelectedIndexSlot and NativeTextOnlineGeneration; existing NativeTextProjection and NativeTextHostSearch keep unconditional nullable-local finally disposal and explicit successful transfer. Public operation regression extends the existing NativeTextCapturedPublicFlow without new test declarations: genuine public A posting hold, genuine same-generation public B hold, A full literal completion first, retained B original files, real update/delete and ordered online publication using freed original two-lease headroom, B complete original literal or actual cancellation/join, full healthy original receipt replay and same-owner cold continuation. Existing two parameterized cases remain unchanged.

Root joins exact guarded source; actual coherent SDK analyzers/format, original hardware/scalar Unit flows, fixture-owned process recovery and original eight RF3 caller variants with authenticated compiled UID/source/image/cleanup binding remain mandatory. Source receipts do not prove these gates. Rollback remains whole coherent checkpoint; no storage format migration or explicit Build/Restore contract changes.

Native allocation detail: the initial reference ledger capacity is three native hash slots, sufficient for the unchanged centrally validated MaximumActiveLeases ceiling of two. This is fixed retained allocation shape, not a new admission quota. Before either container allocation, the same original operation budget admits the existing conservative MapSlotBytes structural frame plus the actual native bucket/entry field widths through sizeof(int) and Unsafe.SizeOf of the reader/reference/value fields. The pre-admitted capacity avoids further retained array growth within the unchanged real lease ceiling; no exact measured CLR heap or performance claim is made. The public test releases A before starting maintenance so A+B do not manufacture a third active-lease allowance.

The actual dictionary/hash-set constructor capacity is supplied by the unchanged centrally validated NativeTextExecutionOptions.MaximumActiveLeases, not a new constant or option. The native minimum hash allocation of three is used only for conservative pre-admission of the framework bucket/entry representation when the accepted original capacity is one or two; it is not passed as an operational capacity policy.


## R27 native acquisition and compiler-visible transfer correction

REQ-SEARCH-ONLINE-001..005 and REQ-SEARCH-LEASE-001..004 retain the original reference-keyed reader, pin→grant→registration settlement contract. Actual Release R27 reported CA2000 in host maintenance assembly and bootstrap lease acquisition; this is compiler evidence, not runtime qualification. No diagnostic suppression or analyzer configuration changes are admitted.

Ordered construction remains bootstrap→explicit maintenance→online maintenance→selected projection→online projection. NativeTextHostSearch.Open owns the bootstrap until OpenMaintenance returns the complete original tuple. OpenMaintenance owns each maintenance local until that tuple exists; direct unconditional nullable-local DisposeAsync calls occur in separate finally regions, and every initiating and cleanup failure is retained outside finally. Failure in online cleanup cannot bypass explicit cleanup or bootstrap closure.

The existing NativeTextOnlineProjection and NativeTextResourceProjectionLease gain internal one-shot acquiring constructors. Borrowed fields and the original reservation are assigned before calling the acquiring delegate; its returned SAME native reader/projection is assigned directly to the actual readonly owning field, with no fallible work or await after acquisition. The delegate is not retained and introduces no authority, lifetime owner, quota, task, lookup or fallback. Existing value constructors and their explicit-index callers remain unchanged. Native projection Dispose still owns only the original projection; maintenance remains owned separately by the actual host tuple. A failed native acquisition aggregate retains the original grant as before; only known acquisition failure without uncertain cleanup permits joined grant completion.

The existing public A-enter/B-enter/A-exit→update/delete→swap operation remains unchanged. Cleanup joins B before a direct final await of A, while both caller cancellation sources and the fixture remain alive. Ordered initiating/cleanup failures remain observed; cancellation method groups invoke the same real CTS CancelAsync. Full original literal, files, healthy receipt replay and cold continuation remain mandatory. Native compiled normal/scalar Unit, recovery, and genuine SDK/MCP/Q1 RF3 qualification remain open.


### TASK-KL039-HOST-OWNERSHIP-HANDOFF: ordered original construction failure closure

The existing ADR095/097 lifetime contract requires each newly acquired host resource to stay owned until the exact original full host tuple returns. Construction stages now have direct failure guards: explicit maintenance surrounds online acquisition, online maintenance surrounds projection acquisition, and the existing bootstrap guard surrounds both. Successful direct tuple return transfers the same resources without a fallible reference-clearing helper. Any later constructor failure directly closes the same native online owner, then the same native explicit owner, then bootstrap; each real initiating and cleanup exception remains in the existing failure ledger and is rethrown with fatal failures retained. No new lifetime owner, API, options, quotas, aliases, payloads or fallback. Native diagnostic fix lookup for the two actual R29 CA2000 rows was NotFound; implementation retains resource guarantees rather than suppressing analysis. Full canonical build and original operation regressions remain required.


### TASK-KL039-OLD-ACTIVE-REPLACEMENT-ROLLBACK: complete original operation regression

Before source, freeze REQ/AC-ONLINE-001..005 and LEASE-001..004 on the existing native APIs: first complete real online35 publication; capture a fresh authorized replacement with its same actual CTS and original snapshot/grant; cancel that captured operation before Seed; require actual OperationCanceledException; await the original session AbortAsync to complete before source/CTS cleanup or healthy work. No expired request, absent outcome or unknown child receipt is an absence proof. This test reaches no checkpoint/publication phase before its joined abort.

After real owner settlement, require complete generated current publication bytes unchanged (including canonical head/authority/cut), complete independently literal old bilingual public reads and exact full original receipt replay; perform genuinely fresh authorized replacement, then join runtime, same-owner reopen and full old/new original receipt replay plus complete literals. Read current publication under one fresh Store.Read with current persisted principal and original typed read budget; no cached role or authority getter-only test. Existing source-cut equality within request and legitimate fresh cold generation handling stay exact.

Owner: Unit/Search Cases/NativeTextOnlineWholeOperationTests adds one whole operation declaration delegating Helpers/NativeTextOnlineReplacementRollbackFlow; existing six declarations/Args remain unchanged. Native new-path NotSupported must precede private creation. Existing ADR095/097/125 contracts suffice: no production/API/serialization/options/probe boundary changes. Complete original runtime and fixture cleanup failures join outside finally; no throw lexically in finally. This addition is source coverage, not compiled discovery or PASS; authentic normal/scalar Unit and full original required suites remain open.


## TASK-KL039-R32-WHOLE-ORACLES: retained tombstones, complete literals and actual admission

Before source correction, bind REQ/AC-ONLINE-001..005, LEASE-001..004 and CUT-001..004 to the actual R32 original native operation outcomes. Both online process-kill cases initiate failure at NativeTextOnlineProcessAssertions.VerifyAsync: expected TrackedRecords1, observed2; the cleanup chain preserves/rethrows that assertion and is not evidence of a cleanup defect. SeedPlanner creates one tracked record for every canonical source row including Deleted; Tokens.Capture excludes tombstones from postings, and OnlinePublication reports manifest.Records.Length. The independent bilingual expected retained inventory is exactly2 before/after deleting one row; independent public search remains one complete live row or the original exact empty/deleted exclusions. Correct Unit/recovery/RF3 inventory oracles together without changing production accounting, canonical state, original process stages, options or deadlines.

Independent constructed literal DocumentRecord/MutationReceipt graphs compare every complete value in original order using the existing JSON value contract. DocumentRecord exposes all six native fields; MutationReceipt exposes all four public fields and its generated internal CompositionReferences is separately required empty for these original non-derived put/delete commands, as proven by ApplyMutations/CompositionMutationReferences. Preserve strict original stored result/replay native byte equality, canonical publication bytes and original receipt/cold oracles. Orleans session-encoded bytes are not declared canonical value equality across independently constructed reference graphs. The R32 boolean mismatch alone does not prove its reference-sharing cause; complete value assertions must still expose any real content difference. No public JSON/runtime persistence/serializer changes or private-field omission.

The genuine competing flow reserves the original configured session union while holding only the one permitted native snapshot: first actual session Capture succeeds; the second actual admitted session's Capture must refuse ResourceExhausted at the unchanged native snapshot gate, while another ResolveOriginal must refuse BudgetExceeded at the original session ceiling. Join every original session Abort before complete fresh publication, independent bilingual literals and exact original receipt replay. Do not increase either ceiling, simulate admission, retry or change caller cancellation. All original RF3 Args/routes, real process-kill and full privacy/budget/cleanup tests remain intact.

Ownership: read worker prepares this finite guarded correction; root alone joins/builds and reruns the exact original Unit/recovery/RF3 flows through canonical native TUnit/Aspire. Rollback restores these guarded test oracles only and retains immutable original R32 logs/TRX. Source-derived corrections are not runtime PASS or qualification; whole039 native discovery, Linux profiles, receipts, resource/fault/endurance gates remain open.


## Completed native Capture before Seed: RF3 delta window (2026-10-10)

TASK-KL039-POST-CAPTURE-RF3 maps REQ-ONLINE-001..005 and AC-ONLINE-001..005 to the existing eight NativeTextCapturedRf3Tests SDK/MCP/Q1 completion/cancellation arguments. Source implementation is not compiled discovery or qualification.

The existing authenticated private phase appends OnlineTextCaptured after DistributedSearchStatisticsCaptured without changing prior enum values, aliases, IDs, arm JSON, defaults, quotas or public DTOs. Only actual MaintainOnlineTextIndex34, nonempty original command and Hold with no ReadKind may claim it. The reused ConnectionGrain supplies its actual context. The callback occurs after the original Capture capability stream fully drains and its independently signed child identity restores, before Seed. The parent checks original scope/identity, expiry/cancellation and fresh persisted administrator authorization after the hold. No storage gate spans this await; no additional snapshot or reader is created.

The same original public reader remains retained while the replacement Capture is held. A real public update/delete commits after the observed capture marker, then release allows Seed, bounded delta replay, validation and canonical publication. Require PublishedCut > BaseCut and PublishedCut > the original publication cut, complete original mutation/publication receipts, independent bilingual changed/deleted literals, original full page or actual cancellation, both original producer settlements and same-owner cold continuation. The former BaseCut > old publication assertion described pre-Capture mutation timing and is deliberately replaced by these stronger post-Capture delta predicates.

Cleanup releases every original arm, joins both actual caller tasks, then both producers under the single unchanged cleanup deadline before clients, Aspire resources and controls close. Optional additional-arm cleanup preserves every existing caller default. Rollback is removal before deployment only; no persisted format or runtime migration. Root owns integration/build/qualification, the read worker owns this guarded proposal. Required exact-source Linux normal/scalar RF3 outcomes, actual image/native discovery and resource/fault evidence remain open.


### KL-039 original caller terminal-aware posting observation (2026-10-10)

REQ-SEARCH-ONLINE-003/004 and REQ-SEARCH-LEASE-001..004 retain the same actual public selected reader, reference-keyed registration, original pin/grant settlement, resource ceilings and complete A/B update/delete/publication/cancel/replay/cold flows. The fixture observation awaits either its actual original posting callback or that SAME public caller task's terminal outcome under the original execution token. A caller fault/cancellation before posting must propagate its original exception immediately; a successful caller without an observed still-held original posting is a fixture failure, never overlap proof. No renewed deadline, polling, simulated reader, production admission change or timeout classification is introduced.

Ordered stages: launch the actual public Search caller under the existing posting observation; await callback-or-original-terminal; only an observed callback with the original caller still active permits the existing overlap work; on any initiating failure release both actual A/B gates, cancel only still-active callers and join each original task before runtime/index/resource disposal. The existing PublicFlow finally preserves every original and cleanup exception by reference identity. PolicyFlow and SnapshotFlow use the same terminal-aware wait and their existing release/cancel/join ledgers. The existing genuine budget-refusal whole case additionally launches an already-cancelled public Search under its actual posting observer, requires the original cancellation through this wait, joins that SAME caller, then performs the existing complete healthy search, immutable full receipt replay and same-owner cold continuation. No standalone observer/getter case is added.

Exact owning paths are the four existing Unit Search Helpers NativeTextCapturedPublicFlow, NativeTextCapturedPolicyFlow, NativeTextCapturedSnapshotFlow and NativeTextCapturedBudgetFlow. Source audit of current OnlineCatalogOwner and IncrementalMaintenanceService shows their NativeTextSelectedReadAdmission is constructed without resource ownership: each online public reader reserves one shared lease in NativeTextOnlineGeneration.Enter, each explicit selected reader reserves one outer AcquireSelected lease. Generation inspection pins only increment the local real pin ledger. There is no proven redundant shared reservation and no quota or product lease correction in this stage. The unfinished R32 original A/B test outcome remains unclassified; the source-proven observer wait defect does not establish the running child's cause or native PASS.

Dependencies and integration: preserve the current r3 reference identity and SDK acquisition fixes; root alone joins these guarded sources and executes coherent build/analyzers plus the original native normal/scalar Unit, real process recovery and RF3 gates. Rollback restores these four helper preimages and removes only this appendix. Original immutable logs, receipts and qualification gates remain unchanged.


## Same-owner search parent children, 2026-10-10

TASK-SEARCH-CONNECTION-CHILD-001; REQ-CLIENT-CONNECTION-001/002 and AC-CLIENT-CONNECTION-001/002; ADR-125. Related existing REQ/AC-ANN-007, FTS-003/004/005 and ONLINE-001..005 retain all native generation, receipt, authority and recovery gates.

REQ-SEARCH-CONNECTION-CHILD-001: Ann, Text and OnlineText connection parents execute their separately signed child capabilities by borrowing the SAME actual ConnectionGrain.ExecuteStreamAsync implementation, never an outgoing RPC to that same activation. The actual private owner supplies the delegate. No public caller/DI observer can supply execution authority; this introduces no activation, dispatcher, scheduling attribute or Graph transition change.

AC-SEARCH-CONNECTION-CHILD-001: original native and Docker/Aspire SDK, official MCP and both SQL caller operation flows retain owner-mismatch refusal with full unchanged source corpus, genuine maintenance build/restore/publication, exact original receipt replay, bilingual update/delete complete literals, native lease/cancellation/unknown and joined Abort semantics, and same-root cold recovery. Existing tests are the regression flows, with their exact Args/complete assertions unchanged; do not substitute setter/parser/validation tests.

Stages: the actual ConnectionGrain method-group is passed to each existing parent and child-call owner. Keep parent scope validation, identity validation, fresh persisted principal/Admin check, actual child RequestId/CommandId, native signed envelope and original expiry. Create the same native identity scope; call the borrowed existing ExecuteStreamAsync inside the original Drain producer; retain its bounded owner/work admission, signature and connection verification and original child cancellation. Partition commands still invoke the actual Graph-authorized CommandPartitionGrain path. Drain/dispose the actual producer before identity scope restoration. Preserve unknown-write mapping and nonfatal/fatal behavior, every Abort primary/cleanup ledger and original native session. OnlineText keeps frames.ObserveChild over that exact borrowed stream, original parent expiry and post-Capture observer's actual GrainContext.

Exact ownership: ConnectionGrain plus AnnMaintenanceExecution/ChildCalls, TextMaintenanceExecution/ChildCalls, OnlineTextExecution/ChildCalls. No leaf/public/persisted/wire schema, alias/Id, placement, generation, native storage, quota, timing or default change. All seven source edits are delegate type/parameter forwarding and the existing stream creation call only. Parent flow algorithms remain untouched.

Dependencies: ConnectionGrain starts at immutable MultiLane8 proposed postimage (itself includes KL039 post-Capture14); OnlineTextExecution/ChildCalls and OnlineGenerationLifetime start at the exact immutable KL039 post-Capture14 proposed postimages. ClientApi/ADR125 start at immutable MultiLane8 docs. Preserve native context observer and every existing appendix by explicit composition. Other files use exact current live bytes. Roll back only this invocation chain, together, without changing persisted/native authorities.

The clean original R32 MultiLane14 failed 14/14 with source and binaries coherent. Its Event3 CapabilityExecution/Unexpected/UnknownWriteOutcome is not an exception-chain proof of this self-RPC cause. Source establishes the invalid outgoing self-boundary against pinned Graph's same-ID contract; root must run original clean normal/scalar and the complete related native/recovery/RF3 cases after this repair. No runtime/PASS, native UID/count/selector or dependency release claim.


### TASK-KL039-FINAL-PUBLIC-READER-RETIREMENT: joined retirement before shutdown

REQ-ONLINE-005 / AC-ONLINE-005 and REQ/AC-LEASE-001/003 retain the SAME two existing OriginalPublicNativeReaderOverlapsCompletedUpdateDeleteSwapThenJoinsAndCompletesHealthyReplayAndCold Boolean cases, complete A/B original results, both original caller cleanup ledgers and all existing persisted authority, budget, deadline and cold gates. The prior old-leaf absence assertion after runtime.DisposeAsync demonstrated host shutdown; it did not independently prove last-reader retirement while the successor remained live.

Native source order is explicit: catalog PublishCurrentCatalog registers its actual RetireAfterReadersAsync task; that task awaits the original generation's MarkRetirement pin drain, then CompleteAfterJoinedRetirement, original root DeleteAfterJoinedOwnership and reservation release. NativeTextOnlineGeneration.Exit releases the SAME reader index, pin and grant, but its returned Search task need not await the separately owned retirement continuation. Therefore do not assume synchronous directory deletion after B completion or use polling, delay, a new timeout or a manufactured completion task.

Before source, freeze one internal read-only NativeTextOnlineMaintenanceService.CaptureOriginalRetirements forwarding method to the EXISTING catalog.CaptureOriginalRetirements. It exposes only the same existing owned Task references, changes no admission/lifetime/ownership, and creates no new public/signed/persisted contract, alias, field, enum, option or cancellation source. The service remains final cleanup authority on every failure path. Same-case fixtures capture the single original retirement task while B is still held after replacement; no new native reader, snapshot or publication is created.

Ordered test flow: retain A/B real posting holds and original manifest/file proof; complete A; native update/delete and successor34/35 publication; independently capture successor canonical original authority leaf/full manifest/native inventory while B remains held. Release or actually cancel and join B as originally selected. Await THAT same captured retirement task under the original whole-flow token; the actual service retains final join ownership if that wait fails or is cancelled. Before shutdown require exact old leaf absent, exact successor leaf distinct and present, and fresh persisted current-publication validation through the existing DatabaseEngine.ReadOnlineTextCurrentPublication same native read gate. Revalidate the independently captured complete successor manifest and every native file's original length/checksum/ownership. Full live literals plus both old and successor native original receipt replay must succeed and leave the successor authority/inventory exact. Preserve the original later runtime shutdown, root absence, same-owner cold full literals and original immutable receipt replay.

Owning files are existing NativeTextOnlineMaintenanceService.cs (read-only forwarding), NativeTextCapturedFileRoster (actual task join, exact old/new leaves, fresh authorized current head and full native inventory), NativeTextCapturedPublicFlow (same original task/CTS/release orchestration) and NativeTextCapturedPublicContinuation (both full original receipt replays). No new cases or UIDs are invented, no production retirement outcome is inferred from this source proposal, and no original failure/cleanup is suppressed. Existing wrapper ServerFailureObserver retains initiating/native retirement errors; final service disposal remains required. Rollback removes only these additions together after all actual owners settle; canonical data and authority bytes are unchanged. Root alone joins/builds/tests; complete exact-source normal/scalar, process/RF3 and Linux/source-image gates remain open.

### TASK-KL039-RETAINED-INVENTORY: real pin permits verification beside the owned WAL

REQ-SEARCH-RETAINED-INVENTORY-001 maps to AC-SEARCH-RETAINED-INVENTORY-001 and the existing REQ/AC-LEASE-001..004 and ONLINE-001..005. Catalog proof acquires the original NativeTextOnlineGenerationPin before examining a published generation. Pass that same live pin through original publication validation and native inventory capture. Require that its actual generation owns the exact requested root/leaf before and after capture, and around every file hash; do not create another reader, grant or lifetime owner.

Only this genuinely retained path may open an inventory input with FileAccess.Read and FileShare.ReadWrite beside the already opened native index WAL. This permission changes no file contents and does not authorize mutation. Pin-free cold, explicit-index, settlement, startup, backup and recovery callers retain FileShare.Read. Exact owner scope, manifest bytes/digest, complete owned roster, entry/depth/file/disk limits, regular-file and symlink refusal, original file lengths and SHA256 comparison remain mandatory. A changing length, changed digest, foreign path, disposed pin or corrupt inventory refuses admission; the original proof still owns joined pin cleanup and all initiating/cleanup failures.

AC-SEARCH-RETAINED-INVENTORY-001 is the two existing Boolean OriginalPublicNativeReaderOverlapsCompletedUpdateDeleteSwapThenJoinsAndCompletesHealthyReplayAndCold flows: A and B actually enter the same public native generation, then complete/cancel, update/delete, publish, join the original retirement, validate complete successor files/results/receipts and reopen the same owner cold. The existing corruption, ownership, generation-capacity and process-recovery whole flows preserve negative and error qualification. Original R40 normal/scalar failures are retained: second public reader admission attempted FileShare.Read against native/index1/seg/2.wal.0 and produced IOException. This is KeyLoad-owned inventory access, not a confirmed upstream ZoneTree defect.

Stages and ownership: root first freezes this appendix and ADR095; then modifies Search Validation/NativeTextOnlineCatalogProof, NativeTextOnlineCatalogSource, NativeTextIncrementalPublishedInventory, NativeTextInventory and NativeTextOwnedInventory plus Lifecycle/NativeTextOnlineGenerationPin; then builds coherent source and reruns the original normal/scalar and process flows, with authentic Linux RF3 and complete qualification still required. No public/persisted schema, alias/Id, defaults, resource limit, policy or migration changes. Rollback joins the actual owners before reverting this coherent internal access chain; canonical publication and file bytes remain authoritative.

The coherent R42 original 74-case run still refused both real second-reader admissions: the same native WAL handle is exclusive even when the new inventory read shares writes. SelectedIndexSlot alone may construct the existing bounded native file provider with its internal read-only-inventory purpose. For this already verified selected index, transform only requested FileShare.None to FileShare.Read; native index is set IsReadOnly before any posting consumer runs. FileShare.Read permits inspection but continues rejecting another writer. All mutable seed/build/replay providers keep their original share argument. Root/leaf identity, original owner lock, path/regular-file/symlink verification and resource reservations remain exact; a separate writer must not open while the original read-only index owns its native handle. This is KeyLoad's chosen file-provider composition, not a ZoneTree defect or provider replacement.

Pinned NuGet source confirms actual FullTextSearch constructor uses the supplied IFileStreamProvider and factory configurator before native OpenOrCreate; IsReadOnly propagates to its existing ZoneTree. Add only the internal purpose argument in Storage/NativeTextFileStreamProvider and its sole true caller in Lifecycle/NativeTextSelectedIndexSlot. No native file content or lock teardown is requested. Retain R42 original failure and repeat complete A/B, ownership/corruption/cleanup/cold operations after this coupled opening correction.
