# ClusterReplication

Status: implementation in progress. Owner: KeyLoad lead. The owner's Orleans-only correction supersedes the DotNext candidate in architecture v0.3. Decision: [ADR-036](../ADR/ADR-036-orleans-foundation.md).

REQ-REP-052 / AC-DBHP-001..008 adds the accepted preserving
[bounded replica term metadata](ClusterReplication/ReplicaTermMetadata.md) work
under [ADR-061](../ADR/ADR-061-bounded-replica-term-metadata.md). This private
node-log scalar observation retains actual provider read checks and both public
authorized quorum barriers. Implementation/native performance proof remains open.

| Requirement | Acceptance and observable evidence |
|---|---|
| REQ-REP-001: Orleans carries votes, ordered appends, read barriers, forwarding and snapshot transfer. No DotNext runtime/package remains. | AC-REP-001: dependency/source inventory contains no DotNext reference; three Docker silos accept .NET SDK operations through request grains. |
| REQ-REP-002: terms, votes, log entries and commit positions are node-owned and process durable before acknowledgement. | AC-REP-002: real-process append/vote/commit interruption tests reopen the acknowledged prefix and reject complete-record corruption. |
| REQ-REP-003: an odd fixed RF3 voter set commits only with a majority; a current-term quorum barrier precedes public reads. | AC-REP-003: leader kill preserves acknowledged commands and stable-ID outcomes; an isolated minority rejects reads and writes. |
| REQ-REP-004: a restarted/empty replica catches up by a verified bounded snapshot followed by the ordered tail. | AC-REP-004: SDK-visible documents, topic/checkpoint/outbox/inbox outcomes survive replica erase/restart; interrupted/corrupt transfers preserve a complete recoverable cut. |
| REQ-REP-005: replicated principals and grants remain authoritative, including after leadership changes and snapshot catch-up. | AC-REP-005: SDK/MCP unauthorized and protected-field requests fail without canonical effects before and after failover. |
| REQ-REP-006: bounded anti-replay admission retains reserved capacity for votes, heartbeats, noop/current-term establishment and membership/lease control when application traffic saturates. | AC-REP-006: signed replay/tamper requests are rejected; saturated forward/read/data-append traffic is throttled without exhausting control capacity; real RF3 client load preserves leadership and minority denial. |

```mermaid
flowchart LR
    Request[Orleans request grain] --> Host[Node-local replica host]
    Host --> RPC[Orleans replica Grain Service]
    RPC --> Voters[Three durable voter logs]
    Voters --> Apply[Ordered canonical apply]
    Apply --> Outcome[Stable command outcome]
```

Slice map: `src/KeyLoad.Replication/Features/ClusterReplication/` owns protocol/log contracts and coordination; `src/KeyLoad.Orleans/Features/ClusterReplication/` owns the per-silo service/transport; `src/KeyLoad.AppHost/Features/ClusterReplication/` owns Docker resource composition; recovery and integration tests mirror `Features/ClusterReplication/`. Node storage adapter remains StorageRecovery. Frontend: N/A, this protocol has no UI. Public client shapes: existing ClientApi contracts; user-visible guarantees remain stable.

Traceability: AC-REP-001/003/004 map to `ClusterTests` and the new replica recovery tests; AC-REP-002 maps to real CrashHost interruption scenarios; AC-REP-005 maps to authorization/failover client cases. TASK-REP-LOG, TASK-REP-TRANSPORT, TASK-REP-INTEGRATE and TASK-REP-VERIFY are defined in [execution plan](../ADR/ADR-036-orleans-foundation.md). Qualification is GitHub Actions only. Historical CI 36926803549 qualifies the old implementation, not this replacement. Power-loss and endurance remain pending.

TASK-RUNTIME-REPLICA-READINESS-W4 maps REQ-REP-004 / AC-REP-004 and
REQ/AC-STORAGE-012. Exact main b533c80 / CI37032546228 fails the Windows
SnapshotInstalled reopen on target/database/tree/0.meta.wal; the prior two-store
barrier probes only owner.lock and commands.wal. Root replaces each store's
probe with the shared StorageRecovery exclusive-file probe, including metadata
when present, inside the existing single five-second/25ms loop. Cancellation
stops before another probe. No file creation, recovery retry, timeout increase,
weakened snapshot/tail/receipt predicate or attribution of the unknown holder
is permitted. A disjoint test worker owns new ClusterReplication real-file
barrier cases: hold canonical or replica metadata exclusively, verify pending
then release and success; cancel a held wait and pre-cancel an unlocked wait;
assert a permanent holder still fails under the unchanged bound. Use actual
closed CrashHost target stores and observe/dispose pending tasks and holders
before deleting their root. Existing native SnapshotInstalled process-kill and
ordered-tail tests are the primary regression. ADR-035/036/041 ownership and
lifetime contracts suffice; public/data/dependency boundaries are unchanged.
Root reviews both helper/caller scopes together, development-builds/formats,
and qualifies the full three-OS recovery suite in GitHub at the delivered SHA.

The same W4 criterion includes exact1bee2609 / CI37036628601's source-store
reopen failures. For the six typed snapshot/transfer crash boundaries, the child
also opened `source/database` and `source/replica`. Enumerate these exact owned
stores alongside the target stores inside the same single five-second/25ms loop.
Use the crash boundary's explicit source-ownership contract, never filesystem
existence to decide whether required ownership/journal files are optional. Other
boundaries retain their target-only path and must not create source storage.
Root owns the CrashHost store-path/source-boundary helpers, preserving the
existing scenario predicate, and ReplicaProcessTrial/ReplicaProcessFiles join.
The disjoint readiness test worker extends only its real-store fixture and cases:
each source canonical/replica owner, journal and metadata holder keeps the actual
combined wait pending until released. Existing source snapshot import and private
transfer crash cases remain the primary regression and retain all assertions.

The two permanent-holder elapsed checks and StorageRecovery's two missing-required-
file arguments retain their five-second lower and six-second upper observation
bounds. Run only those individual wall-clock measurement cases with TUnit's
keyless method-level `NotInParallel`, which the
pinned1.72.10 package documents as exclusive execution. Exact Windows evidence
shows the failed check overlapped74 distinct cases with16 active at peak; timer
or continuation delay is a supported inference, without a threadpool trace.
No class/assembly serialization, retry or timeout increase is allowed. Other
holder-release/cancellation tests and every real process crash remain parallel.
This is intrinsic readiness timing qualification, not a loaded-system latency
claim. Source review, native case discovery and full exact-SHA CI must verify
that the same bounds and all original crash cases remain.

TASK-RUNTIME-MAC-FIXTURE-W6 refines AC-REP-004 / AC-STORAGE-012 after
exact323d60499 / CI37044499074. All11 new macOS readiness cases fail during
construction at ReplicaSnapshotFiles.RejectLinks' reparse-point rejection,
before snapshot-data or readiness predicates. The fixture allocates an unresolved
system temp path; macOS temp aliases are the source-supported cause, while the
native report does not expose that run's exact TMPDIR. Reuse CrashHost's existing
ReplicaFixturePaths.NewDirectory for the fresh owned root, as other real replica
fixtures already do. It resolves existing directory ancestors using native .NET
ResolveLinkTarget before the private root is created; production RejectLinks
remains unchanged and fail-closed. Preserve one root, shared incarnation, both
target stores and required source stores, closure order, actual exclusive holders,
waits/assertions and owned cleanup. No second resolver, guard bypass, dependency,
public contract or persistence migration is introduced. The worker owns only
ReplicaFileReadinessStores; root owns contract/diff/evidence integration. All11
formerly red native cases plus original snapshot/tail/process recovery across
three OSes are the regression matrix. AC-REC-FUP-001 requires the existing
physical-temp helper and unchanged fail-closed guard; AC-REC-FUP-002 requires the
same genuine source/target stores, incarnation, holders, assertions and cleanup.
Either criterion fails on setup rejection, changed ownership or leaked work;
all11 native cases and original snapshot/tail cases must pass. ADR035/036/033 cover this preserving private
fixture reuse; rollback reverts only the allocator call. Environmental path or
cleanup failures require source lifetime review rather than a synthetic injector.

## Actors, entry points and failure boundaries

Actors are authenticated SDK/MCP callers, the node-local replica host, fixed voters, the membership provider and the recovery operator. Current source is composed by [ServerApplication](../../src/KeyLoad.Server/Features/ClientApi/Hosting/ServerApplication.cs): it starts the node-local [PartitionHost](../../src/KeyLoad.Server/Features/StorageRecovery/Hosting/PartitionHost.cs) and Orleans silo, whose [replica service](../../src/KeyLoad.Orleans/Features/ClusterReplication/GrainServices/PartitionReplicaGrainService.cs) routes peer operations. Aspire declares three Docker nodes with separate data mounts. This source is not yet qualified as a delivered RF3 deployment: current tests do not force request-activation migration and then verify storage ownership and a durable caller-visible outcome. Public HTTP/.NET transport belongs to ClientApi; required official MCP parity is pending. Frontend is N/A because replica consensus has no independent UI. Shared contracts stay in Abstractions and the exact ClusterReplication/StorageRecovery slice owners above.

Positive flow: an authorized command reaches its own request grain, the physical host orders and persists it, a majority crosses the declared acknowledgement barrier, and canonical apply returns the stable outcome. Negative flow: minority, stale term/owner, invalid peer MAC/replay or denied principal cannot establish committed success. Edge/error flow: an unknown response is resolved by stable command ID; interrupted/corrupt append or snapshot reopens one verified cut or fails explicitly; cancellation drains owned work without transferring locks to a migrating activation.

TASK-RF3-ELECTION-RETRY-R80 preserves AC-REP-003 after exact2ecbeee4d /
run37093992229/job111123331985:62 of63 tests passed, and LeaderLossScenario queue
Receive returned UnknownWriteOutcome with the documented same-command-ID retry.
Freeze queue Receive/Processing and projection replay requests before using the
existing election helper, as subscription writes already do. Preserve its strict
UnknownWriteOutcome/OwnershipLost whitelist,250ms cadence, original two-minute
scenario deadline and every receipt/single-delivery/ACK/minority/recovery oracle.
No new IDs inside retries, added sleeps, product retry/shim or exception waiver.
Existing leader-kill RF3 is the failing regression; actual later delivered-SHA
GitHub RF3 success closes it. Cohesive LeaderLossQueueScenario owns only this queue
flow, called with the existing fixed collection/queue names; all original exact
data/effect/ACK checks remain, while the original scenario stays under200LOC.
ADR-003/007/036 cover the unchanged fault contract.

AC-REP-006 additionally maps to existing cryptographic/replay source cases in the [ClusterReplication unit slice](../../tests/KeyLoad.UnitTests/Features/ClusterReplication/) and planned real RF3 saturation/failover cases. Pure envelope tests do not prove liveness under load. Every acknowledgement/recovery assertion needs the exact delivered GitHub run; fault/endurance/power-loss claims remain separate.

Related invariants: [ADR-003](../ADR/ADR-003-durability-ack-barrier.md) ACK/profile barriers, [ADR-007](../ADR/ADR-007-replica-consensus-bootstrap.md) consensus/bootstrap, [ADR-016](../ADR/ADR-016-atomic-physical-placement.md) atomic identity/placement and [ADR-017](../ADR/ADR-017-ownership-session-tokens.md) current ownership-session tokens. Physical movement must preserve or explicitly invalidate those tokens under its separately qualified contract.

TASK-REP-DISCOVERY removes the obsolete HTTP consensus/body-spooling protocol.
PeerSecurity authenticates only a bodyless GET of /internal/silo with no query;
native signed envelopes own votes, append payloads and snapshots. The discovery
signature binds a versioned purpose, exact method, recipient authority, path,
timestamp and nonce. A fixed-capacity nonce table rejects saturation explicitly,
retains a nonce through its inclusive future validity, and does not admit malformed
or forged requests. The clock is injected TimeProvider.System in production.
Real request-object security cases cover tamper/replay/expiry/body/path/method and
capacity, while RF3 CI exercises the genuine sockets and signed discovery reply.
No HTTP handler fake or legacy body fallback remains.

## Bounded intentional restart health wait refinement

REQ-REP-050 maps to AC-REP-050 and AC-ISO-004: after a deliberate native Docker kill
and one Aspire Start command, health waiting uses the native recovery wait behavior
with the original cancellation/deadline. Old unavailable logical-resource snapshots
must not immediately abort a requested restart. Actual healthy result, fresh Docker
Running/changed-start/source identity, public SDK readiness, persisted command/data/
subscription/outbox recovery and minority rejection remain required. Initial startup
keeps its existing fail-fast behavior; no retries, longer deadlines or swallowed
failures. Existing `ReplicatedAtomicBatchSurvivesLeaderContainerKillAndMinorityRejectsWrites`
is the real acceptance regression, executed in exact-SHA Linux RF3 CI. TASK-AISQL-024
retained run37072003906 evidence supports stale-terminal handling; exact selected
Aspire event generation remains unproven. TASK-AISQL-025/root owns only the two
post-restart health waits; ADR036 existing lifecycle contract is sufficient, new
architecture ADR:N/A. Qualification remains pending.

## Atomic checkpoint metadata publication

REQ-REP-051 maps to AC-REP-051: every node shares one log-owned protocol gate for
term/append/follower planning and checkpoint metadata publication. Real canonical
snapshot capture/verification/install IO remains outside that gate. A checkpoint
cannot advance the compacted cut between an authenticated protocol operation's
metadata, term and suffix reads; ordinary concurrent publication cannot poison a
healthy replica. The materializer borrows the gate, drains its apply work and does
not dispose the log owner's gate; physical log disposal closes it after consensus
and materializer drain. Existing snapshot cut, tail, quorum, incarnation, transfer
and process-durable acknowledgement rules remain strict.

AC-REP-051 requires tests on genuine ZoneTree log/canonical stores proving the same
gate instance, blocked metadata publication while a real planning scope is held,
then exact verified snapshot publication/tail read and preserved metadata after
release. Cover direct Create and completed incoming transfer publication,
successful and faulted materializer disposal, and actual log-owner closure. Existing
process-crash/snapshot tests and 1/2/3 native intensive seeding/read plus RF3 SDK/MCP
fault gates must pass at the repaired SHA. TASK-ISO-016K-F and ADR007 own the ordered
contract; no injected storage substitute, silent retry, lost cut or changed quorum
is acceptable. Source races are established independently from the unresolved
cf630 OwnershipLost runtime attribution; exact native node logs remain necessary.

TASK-REP-MATERIALIZER-FAULT-ORACLE refines AC-REP-002/004/051 without changing
production shutdown. In the real ZoneTree JournalFlushed failure flow, the pending
apply waiter must fail with RecoveryRequired and the memoized disposal task must
preserve its original UnknownWriteOutcome. Successful resource cleanup does not
require a successful terminal task. Assert repeated disposal observes the same
original exception, the borrowed log gate stays usable until its physical owner
closes, and reopening retains the committed prefix and storage identity.
The lifecycle fixture may acknowledge only that exact terminal exception after
the scenario has asserted it; any different exception, unasserted failure or
independent owner/directory cleanup error remains fatal to the test. Keep the
normal/concurrent disposal and filesystem-failure regressions. The owning paths
are RecoveryTests/Features/ClusterReplication/Cases/ReplicaMaterializerLifecycleTests.cs
and Fixtures/ReplicaMaterializerLifecycleFixture.cs. Native TUnit recovery and
exact-source Linux recovery reports qualify this test correction; they do not
close RF3, endurance or power-loss gates. ADR-007 preserves the failure contract.


## TASK-ISO-021 accepted application/control read separation

REQ/AC-REP-006 and AC-ISO-003/004/005 remain the correctness/qualification gates.
Source b474 proves application quorum rounds spend Critical capacity via empty
Append. Actual native RF2 OwnershipLost/RF3 ResourceExhausted do not yet prove
which replay pool first overflowed; preserve that uncertainty and failed samples.

Append signed method ordinals ReadProbe=7 and ControlReadBarrier=8; old0..6,
envelope version, exact HMAC method binding, generation, nonce, freshness,
full-lifetime cross-method replay and durable formats stay unchanged. ReadProbe
uses the existing strict AppendRequest parser with initialized empty Entries
and declared LeaderId equal to authenticated sender. Any data/control entry,
default/null entries, malformed/duplicate/unknown/trailing field, invalid header
or over-budget body fails before receiver/ObserveLeader/state mutation. Valid
ReadProbe consumes ReadBarrier. Ordinary heartbeat/control empty Append stays
Critical. Application-origin nonempty catchup fallback uses empty ReadProbe;
control/write fallback keeps ordinary Append. Snapshot/data semantics unchanged.

ReplicaLeader uses an internal closed purpose, never a public caller flag.
ReplicaConsensus adds trusted ReadControlBarrierAsync sharing the actual bounded
read core/readiness/activity/lifetime/10s/quorum/term/cut/apply semantics. Remote
control uses signed strict-empty-string ControlReadBarrier/Critical; local
control round uses ordinary empty Append. Existing public ReadBarrier retains
application purpose. No ICommitCoordinator/HTTP/SQL/SDK/MCP/authorization change.

ReplicaMembershipTable constructor now requires the actual ReplicaConsensus
instead of IReplicaEndpoint; the sole production construction already supplies
partition.Consensus. Change the source signature coherently, with no legacy
overload/cast/compatibility fallback. ReplicaMembershipStore receives that same
node-owned consensus and calls trusted control read; membership writes retain
coordinator/atomic CAS. Preserve true caller cancellation; independent native
deadline/lifetime cancellation maps OwnershipLost as the original coordinator.

Disjoint ownership: root enum, strict Orleans replay/sender classification,
membership source join, fixed quota diagnostics, benchmark-only profile/env,
shared docs and integration. gates_audit owns Replication ReplicaConsensus.cs,
ReplicaLeader.cs, ReplicaFollowerSender.cs, ReplicaRequestDispatcher.cs and NEW
cohesive prefixed purpose/guard helpers plus NEW focused real-node tests under
RecoveryTests/Features/ClusterReplication. Escalate if a test needs a shared
fixture edit; no synthetic successful transport, gate/quorum/term/retry relaxation
or public/shared abstraction edits. Tests first use actual stored nodes and
real protocol. Root new security TUnit tests cover valid signed methods, empty
guards, wrong sender/MAC, malformed/nonempty/null fields, cross-method replay,
separate capacity denial and unchanged old method/data/snapshot classification.

Quota diagnostic snapshot is captured atomically at capacity denial and emitted
after the lock: configured numeric sender index, closed method/pool, counts and
limits, Unix timestamp/oldest expiry. No identity/address/nonce/payload/credential/
exception text. Fixed per-sender/pool state emits first then at most once30s,
with saturating suppressed count; no success-path allocation/unbounded keys/IO.
Original authenticated ResourceExhausted classification remains unchanged.

Benchmark-only ReadBarrierPerVoter196608 with original Critical16384/Forward32768/
DataAppend32768 yields278528 per voter,835584 per RF3 node, below1048576.
Production defaults stay unchanged. Retain actual rolling occupancy/expiry and
exact node config; this arithmetic does not guarantee complete CRUD/queue cells
or throughput. Every public successful call still performs two authorized
quorum barriers; do not remove persisted authorization or cache it here.

Ordered stages: failing acceptance-derived tests; bounded source implementation;
root review/build/format/governance; exact-SHA full normal/scalar/138 recovery/
RF3 and native KeyLoad1/2/3; then genuine control-pressure/failover proof and
all270 before aggregation/site. Native control execution/membership-under-load
observer remains a separately frozen required join, not inferred from admission
or absence of warnings. Public auth/lease reserve liveness remains unqualified.
All test/runtime execution is GitHub only. Homogeneous all-voter rollout and
rollback preserves local stores/journals; old nodes fail closed on new methods,
no mixed-version availability promise. ADR remains Accepted pending all evidence.


TASK-ISO-021R source review closes the new-method index overflow boundary:
ReadProbe rejects PreviousIndex=Int64.MaxValue before replay admission, even
with otherwise valid positive term/previousTerm and empty entries. Genuine
signed MaximumPreviousIndexFailsBeforeReplayAdmission asserts Validation and
both one-slot pools remain available. Existing Append semantics are unchanged.
Source review also confirms fixed rate state/atomic snapshots and actual native
DI/Consensus ownership; native control-under-load and provider-failure evidence
remain open, with no inferred successful runtime result.


## Embedded final clock ordering, TASK-EMBEDDED-CLOCK-ORDER-001

EmbeddedCoordinator is the product-owned real standalone admission coordinator over DatabaseEngine and its native ZoneTree Store.Commit gate. For coordinator-owned JSON/native submissions, bounded validation/normalization stays before commit; the final EvaluationClock.GetUtcNow sample is selected inside the original Store.Commit callback immediately before the existing ApplyCommittedCommand. Retain exactly one commit and the shared authorization/outcome/fingerprint/strict ValidateCommandClock pipeline. Public Apply(ReplicatedOperation, replicationIndex) retains supplied canonical EvaluatedAt unchanged. This matches RF3 ReplicaLeader final timestamp selection under rounds and state.LockedAsync before ordered append; embedded ownership does not qualify RF3. No clamp, retry, backward-clock acceptance, fence reset or second coordinator lock is allowed. Cancellation is rechecked inside admission before clock selection; synchronous original apply settles before return. TASK-EMBEDDED-CLOCK-ORDER-001 maps REQ-REP-001 and AC-REP-001, and native durable-job clock/owner contracts under ADR-028/ADR-110. Preserve original R308 four journal-fence failures. Root validates all five NativeSagaTimeoutFunctionalTests actual native job operations, plus existing strict stale-clock/replay tests, normal/scalar and required Linux/RF3 gates. No result or source review closes these runtime predicates.

## TASK-KL009-NATIVE-APPLY-SCOPE-001 (follower-owner implementation contract, 2026-10-07)

REQ-REP-APPLY-SCOPE-001 / AC-REP-APPLY-SCOPE-001 binds original KL009 to a genuine node-local canonical apply owner and real independent replica-network progress. Preserve native ZoneTree, original ordered commit/apply gates, deterministic replay, bounded admission, RF3 acknowledgements and separate authenticated request grains. This is authored acceptance infrastructure; Linux/native execution and original task closure are not claimed.

Docs-first current private-format boundary: all RequestCqrsProbe owner/arm/release/marker producers/readers select version2 and reject version1 without fallback, migration or mixed records. Original request/read phases retain exact original semantics with nullable new arm fields all null and marker EntryIndex/EntryTerm null. CanonicalJournalFlushed is Hold-only and requires complete four-scalar Partition, nonempty SourceRequestId, distinct nonempty SourceArmId and exact TargetVoter. CanonicalOutboundObserved, CanonicalIndependentAppendCompleted and CanonicalOwnerDisposed are unarmable observation-only phases. Canonical markers contain positive real entry index/term and the genuine original request actor ID. Release still addresses the exact arm/request identity. Keep original 32-arm, 400-file, 8-marker, 8192-record-byte, 1048576-aggregate-byte and depth4 ceilings, original component/principal byte bounds, hold/poll/admission/shutdown deadlines and validated configurable lower bounds. No status/receipt is invented.

Identity bridge is bounded test control, never authority: the original real BeforeSubmit Hold publishes its actual signed-voter request actor marker. While held, the fixture writes a distinct canonical arm referencing that source BeforeSubmit arm, principal, stable Batch CommandId, observed actor ID, complete partition and an explicitly observed nonleader voter. Both arms retain their original immutable bytes until joined shutdown. The actual Channel worker opens its own synchronous scope from the genuine ReplicaEntry Id/index/term and exact physical voter plus active source arm. No request ExecutionContext/AsyncLocal propagation across Channel is assumed and no synthetic actor ID is created. Actual Database.Apply retains original persisted authorization, identity/fingerprint/replay and strict clock validation; only its construction-owned native JournalFlushed FaultObserver may hold that owner. Unmatched bootstrap/recovery/job/store work has no hold and the replica store gets no callback.

Independent network evidence is produced only by actual PartitionReplicaGrainService.ExchangeAsync after peer request authentication, original readiness/admission/native endpoint validation and successful signed reply construction. Begin captures the same currently held follower owner; completion decodes only already-admitted request/result bytes and requires an accepted empty native Append, exact term and matched/next position, with prior/committed cuts covering the held genuine entry. Completion must still see that exact held scope. Failed/cancelled/rejected exchanges do not qualify. It emits one bounded presence marker, never credentials, payloads or invented counts. Actual ReplicaGrainServiceClient.InvokeAsync separately observes any external invocation originating in the explicit canonical apply scope; any such marker fails acceptance. The real native RPC boundary is used, not HTTP discovery or a replacement transport.

The leader-owned draft cannot qualify this operation: ReplicaLeader owns rounds through WaitForApplyAsync, and that waiter synchronously reads canonical LastApplied. The accepted proof therefore holds a follower, leaves leader semaphores/cancellation untouched, and avoids node Status/State calls while held (they may read LastApplied under ProtocolGate). Discovery/leader identity is captured before the hold. Original health/readiness routes remain unchanged.

Lifetime: the physical host owns bridge, real materializer worker, storage callback and DI observer. Real GrainService construction resolves the observer before accepting replica RPC, including followers without a local public request. One exact scope owns hold, origin observation and callback admission, restores its prior execution context, writes canonical-owner disposal and joins original callback lifecycle. Synchronous apply errors and scope cleanup errors both propagate, preserving original failures; no second commit, retry, lock change, cancellation clamp or timeout increase. Existing host stop cancels hold and joins callbacks/materializer before store disposal. The fixture releases both arms, joins the actual SDK operation and original request producer plus canonical owner before deleting owned controls/resources.

Whole flow: actual Aspire current-image RF3, persisted non-admin principal and document+queue scope, original SDK atomic Batch, original signed BeforeSubmit marker, native follower JournalFlushed marker carrying real entry index/term, accepted authenticated Append settlement while held, and absent canonical-origin outbound evidence. Original leader SDK receipt must be successful under unchanged deadlines. Release the exact canonical arm and join its owner; verify independent literal complete document (reference/revision/JSON/redaction/empty fields) and queue inspection metadata/body/headers through SDK and official MCP. Assert exact receipt effects and native-byte same-ID SDK/MCP replay, changed-payload conflict with unchanged public state, then distinct healthy queue continuation. This proves the full scoped public projections, not a complete raw-store image or power-loss durability.

Owned paths: Replication ClusterReplication real ApplyBatch/worker scope; Orleans ClusterReplication real GrainService incoming/outgoing transport; Server ClusterRouting existing private control codecs/lifecycle and observer composition; Server StorageRecovery physical host/native storage callback; AppHost ClusterRouting strict current owner reader; IntegrationTests ClusterRouting actual Aspire wave/probe/SDK/MCP helpers; UnitTests ClusterRouting bounded private codec-negative control. That unit codec case is supporting infrastructure, not a product coverage contributor. Root-only format/full build, genuine new census/source-image binding, focused native codec in normal/scalar, existing probe request/read/fault flows, new current-image RF3 operation and mandatory full normal/scalar/recovery/RF3 gates remain required. No numeric coverage or successful native execution is claimed. Rollback removes the same-current private diagnostics coherently; persisted database, replication/native binary/public JSON formats and authorities are unchanged.

## TASK-KL020-ORIGINAL-ACCEPTANCE-CLOSEOUT-001

The original KL-020 acceptance in architecture-v0.3.uk.md is independently
qualified at source 4e18ba1ba29ae31970302e6bfa43ad9c04ead7e0 by the original
Linux [RF3 job 112948482972](https://github.com/managedcode/KeyLoad/actions/runs/37666943488/job/112948482972).
`ClusterTests.ReplicatedAtomicBatchSurvivesLeaderContainerKillAndMinorityRejectsWrites`
passed through the actual fixture-owned Aspire three-voter topology. It commits
an acknowledged QuorumProcessDurable document/event/queue/topic batch, kills the
elected container, retries the frozen command ID and compares its original token
and one document revision/one queue delivery, then observes processing and
subscription/projection effects. It kills the second voter, rejects both a strong
write and read, restores both voters, verifies the minority document remains
absent and checks replicated revisions and checkpoints across all three nodes.
This maps the three original task criteria to REQ/AC-REP-003.

The immutable original required RF3 TRX digest is
`8c9eb6c1b6fad38a0dd8532ea8f375465623939ef7784d9bb9ba9e24f938f75c`;
the original native RF3 test-image observation digest is
`086f26313d9d9311bcb544bf33290a90e51ebd49dd3d1f2d56d06a95a1d34c72`.
Its exact test/helper declaration hashes match original native PDB documents;
the operation bodies remain unchanged. Fixture and product sources have later
changes: this is an original task/source qualification, not current whole-module
equivalence. Required RF3 was 173/183 passed with ten retained failures; covered
RF3 was 0/11 due to selection admission. Consensus fault/unacknowledged-entry,
power-loss, endurance, performance, coverage and broader feature gates remain
open under their owning criteria. No failed report is promoted to full-suite PASS.

KL-021 remains open: the passed scenario performs ordinary quorum reads, not a
read carrying the previous commit/session token. Current GetDocumentRequest has
only EntityRef and the SDK GetAsync has no minimum CommitToken argument. Public
token-bound failover read and wrong-incarnation rejection need a frozen ClientApi
read-admission contract and actual SDK/MCP operation proof; internal token
validation and receipt equality do not supply that missing public flow.

## TASK-KL021-DOCUMENT-SESSION-READ-001

Accepted homogeneous first-release document session-read implementation contract; runtime qualification remains open.

REQ-SESSIONREAD-001 / AC-SESSIONREAD-001: Only document GET gains optional typed GetDocumentRequest.MinimumToken at generated native Id1, keeping Reference Id0 and alias. SDK explicit GetAsync(EntityRef, CommitToken, CancellationToken), HTTP/official MCP keyload_documents_get and Q1 CALL keyload_documents_get(@arguments) decode the exact same typed request via existing canonical catalog; absent option remains ordinary strong GET. This is not generic model/session cache support and does not add a dispatcher.

REQ-SESSIONREAD-002 / AC-SESSIONREAD-002: The existing unique request/read grain obtains fresh native quorum barrier under original bounded ReplicaReadRoundExecutor timeout, including existing actual WaitForApplyAsync(barrier.Position). Afterwards document execution reloads persisted principal/grants and validates minimum token in the exact same native Store.Read cut as row/field authorization and document projection. Token must match database incarnation, exact atomic partition and current persisted ownership epoch; position must be positive and no greater than actual lastApplied at that cut. This barrier applies the current quorum commit cut, hence it already meets every previously acknowledged minimum token. A token beyond that fresh applied cut is explicitly rejected; there is no speculative wait for a caller-invented future position. No physical-lineage translation, stale-mode API or authority cache is added.

REQ-SESSIONREAD-003 / AC-SESSIONREAD-003: Explicit existing TokenInvalidated category with distinct fixed safe reasons WrongIncarnation / OutOfScope / FuturePosition / InvalidPosition identifies token failures without exposing token values/credentials/records. Missing/corrupt applied authority is Corruption; unsupported ownership epoch is OwnershipLost or token invalidation per current placement witness policy. Persisted authorization is checked before token failure reasons; denial contains no row. Caller cancellation checked before admission and inside final cut, no partial result; native deadline/admission/drain unchanged. Former leader with no quorum cannot pass existing fresh barrier even for a valid historical token.

REQ-SESSIONREAD-004 / AC-SESSIONREAD-004: Real ZoneTree local whole flows prove literal document/complete state+position unchanged after invalid incarnation/scope/future/position and pre-cancel, fresh authorized minimum succeeds and later revision continues. Real fixture-owned Aspire RF3 SDK+official MCP failover operation proves acknowledged write token→elected leader kill→token-bearing complete literal healthy read→all invalid variants fail→healthy token read; isolated former surviving leader/minority strong token read fails, both voter restoration and healthy read follow. Existing native receipt replay/Unknown scenarios remain separate and unchanged. Auth revocation must deny valid token then renewed persisted grant/healthy read.

Ownership: Abstractions DocumentStorage DTO; Client overload; Core feature-local same-view validator and Documents reader; Orleans existing GrainCoreReadCapabilities routing; docs ClientApi/DocumentStorage/ClusterReplication + ADR017/036 amendment; unit DocumentStorage and RF3 ClusterReplication wholeflow. SQL Q1 CALL is exact typed operation envelope only; Q1 SELECT/AST and other read models do not accept session options in this stage. Same homogeneous current first-release cohort; native Id append/alias remains stable, no legacy reader/migration/runtime fallback. Root owns join/build/native discovery/test/Linux evidence and status; no qualification or closure inferred from authored source.

## Proven epoch meaning before implementation

DatabaseEngine.Token in Core/DatabaseEngine.cs obtains OwnershipEpoch from persisted ReadPlacementWitness.PlacementEpoch. AtomicPartitionPlacementReader Fallback/Explicit uses PhysicalShardCatalog.DefaultShard.PlacementEpoch; PhysicalShardCatalogRecordSerialization.InitialRecord initializes that physical-placement value. ReplicaElection.RunRound changes DurableReplicaLog term/vote, not physical catalog. Original authenticated 4e18 RF3 passed LeaderLossQueueScenario compares the entire pre-kill commit token to the actual replay token after elected leader kill. Hence elected leader failover keeps physical PlacementEpoch, and equality does not invalidate that acknowledged token. The new public wholeflow additionally checks a fresh postfailover command retains the same physical epoch/incarnation/atomic identity. Physical ownership movement with a changed placement epoch is explicitly unsupported by this minimal surface; it fails closed without invented lineage, and does not claim KL035/036/072 movement support.

The current native fixture supports Kill/Restart and retains actual owner receipts.
The authored authority-denial flow is a still-live surviving voter without quorum,
not a network-isolated former leader while another majority stays live. That
stronger KL021 authority scenario remains open until a bounded fixture-owned
network partition contract exists; no manual Docker or new fault hook is added.
Fresh barrier implementations are ReplicaReadRoundExecutor + ReplicaLeader.BarrierAsync:
both use native Materializer.WaitForApplyAsync at their authenticated quorum cut.
SDK method ownership is Features/DocumentStorage/Transport/DocumentSessionClient.cs.

### Native MCP no-quorum boundary refinement

McpHttpPipeline.RunAsync invokes DatabaseCredentialResolver.ReadAsync before
native tool dispatch. Its fresh signed Authenticate read itself needs quorum.
Therefore the no-quorum official caller must retain the actual native
HttpRequestException HTTP503 rather than invent a CallToolResult error; SDK
GET still asserts its actual typed OwnershipLost problem. The healthy
restored token-bearing SDK/MCP/SQL results remain complete literal checks.
This is not a tool outcome or session initialization success claim.

The final no-quorum oracle retains both SDK's exact OwnershipLost/503/NoLeader
problem and the official caller's actual native HTTP503 original exception,
with its bounded five-field Problem body and credential/document privacy checks.
It never converts that pre-tool HTTP failure into a fictitious tool result.

## TASK-KL021-OWNED-SILO-ISOLATION-002 — accepted namespace control implementation contract

REQ-MTOKEN-ISOLATION-003: native fault image derives from the exact existing Dockerfile pinned aspnet/runtime and server build. A dedicated feature-owned fault Dockerfile derives from the authenticated exact server @sha256 image receipt; fault-runtime installs native iptables and a bounded coreutils timeout wrapper as container UID0 at image build, then restores APP_UID. Normal root Dockerfile and its runtime final/default target remain byte-for-byte unchanged and receives no fault tools or capability changes. Aspire13.6 public WithDockerfile(context,Dockerfile,stage:fault-runtime) owns the selected three-node image build/start; WithContainerRuntimeArgs adds only NET_ADMIN to these exact owned resources. No privileged, host network/PID namespace, production default changes, manual topology, image digest fiction or whitelist expansion. Tool version/package/license and base/build/image/config identities are actual build/start receipts, not inferred from a web page.

AC-MTOKEN-ISOLATION-003: fixture opts into a separate owned native fault wave and requires Linux/running container Config.User APP_UID, only admitted additional capability NET_ADMIN, no Host/PID/shared namespace, exactly three model names and matching incarnation/config image/build source. Before every firewall mutation re-inspect owned ID/name/image/config/incarnation and verified unique IPv4 endpoints; reject malformed/ambiguous/IPv6/foreign source rather than accepting alias fallback. Native discovery SiloAddress IP/port must match inspected network address TCP11111; HTTP8080 endpoint remains discovered from Aspire. Unconfigured standard/default/covered fixtures cannot acquire this fault capability.

REQ-MTOKEN-ISOLATION-004: create unique per-run bounded-length chains inside only the old leader's network namespace. For each of exactly two owned peer IPv4s, INPUT source and OUTPUT destination rules block both sport11111 and dport11111; attach those exact chains first in the corresponding chain so existing connections and both directions are isolated. HTTP8080 remains outside all rules. Record chain/rule mutation intent BEFORE execution and actual exit/output/check receipt before next mutation, including partial chain creation/jump install. Never flush shared INPUT/OUTPUT rules or unrelated chains. Restore only own exact jump/rules/chains and verify absence.

AC-MTOKEN-ISOLATION-004: native docker exec --user0 uses inspected full owned container ID, executable argument list, no shell interpolation, no detach/privileged. Image timeout wrapper bounds remote iptables work; xtables lock wait fits within it. Host original exec task and bounded stdout/stderr readers remain owned and are actually joined even after initiating cancellation, then partial intents reconcile against native exact chain/rule state before restore. Docker CLI cancellation alone is not evidence of remote exec completion. Unsettled original remote/host work retains primary/cleanup/resource/image ownership and fails; never restores over an in-flight mutation or deletes retained receipts. Actual counter/rule checks and genuine surviving-majority new-term ACK provide fault proof, not HTTP reachability alone.

AC-MTOKEN-ISOLATION-005: real SDK/officialMCP whole-flow uses current optional MinimumToken public contract: original canonical command ACK/token; exact old leader/container/native term; namespace isolation; actual different surviving leader/newer term and majority ACK of one immutable next command; reachable running old leader rejects token-bearing strong read with exact native no-authority error, no stale/partial body; same exact nodes/rules restored and joined; literal current document/revision/token/receipt and healthy reads across restored replicas. Existing parent/start/cleanup deadlines unchanged, no timer sleeps or generic retry-to-green. Any canonical UnknownWriteOutcome is preserved and reconciled only under existing original-ID/receipt contract, never replaced by fresh IDs or fabricated ACK.

Public API evidence: Docker documents container exec UID selection and non-detached process lifetime at https://docs.docker.com/reference/cli/docker/container/exec/ ; namespace isolation and NET_ADMIN (rather than privileged) at https://docs.docker.com/engine/containers/run/ . Pinned Aspire13.6 local XML advertises WithDockerfile stage/build-before-start and WithContainerRuntimeArgs; these are source/API evidence, not native execution. Netfilter upstream https://www.netfilter.org/projects/iptables/index.html owns rule manipulation semantics. Current tool package version/image ID/namespace behavior will be measured in root's genuine native build/start gates; no runtime availability claim before execution.

Ordered ownership: docs/ADR017 contract precedes Dockerfile fault-only stage and AppHost ClusterReplication resource composition helper. Integration ClusterReplication models/lifecycle/processes own exact node/image/IP/chain/rule receipts, original process/readers and restoration; owning case/flow consumes disjoint KL021 MinimumToken API packet. Root joins source/serialization/API paths coherently, builds and runs actual native Linux RF3. Source/API readiness, functional proof and all broader endurance/performance gates remain distinct; no KL021 task closure from authored code.

REQ/AC-MTOKEN-ISOLATION-006: NodeStatus gains additive native Id9 ConsensusTerm, verified free after existing IDs0..8. NodeAdministration.StatusAsync copies Term from the same existing locked partition.Consensus.StateAsync snapshot used for Leader/Applied. Current persisted authorization, separate request grain, readonly status semantics and native/public serializers remain unchanged. Purpose is authenticated operational authority diagnostics, not a test hook. Original ACK and actual new-majority ACK whole-flow requires positive original term and strictly larger surviving term; changed leader alone does not prove it. No on-disk live inspection or invented term. Public SDK/official MCP status full outcomes bind actual native term; defaults of construction do not qualify it.


Frozen bounded native authority observation (owner approved2026-10-08): under the SAME original McpCallerDeadline token, sequential authenticated SDK Status operations, exactly one awaited original request in flight, on the two inspected surviving voters may observe readiness. No Task.Delay, sleep, polling worker, reset deadline, mutation retry or health-only inference. The latest32 actual status/error observations form a fixed-size closed diagnostic summary; replies do not accumulate. Readiness is bounded by the original deadline, never by an arbitrary attempt count. Only exact OwnershipLost/503/"The cluster has no current leader with a reachable majority." is a pending authority observation; any other failure remains fatal to the flow. Readiness requires an explicit positive ConsensusTerm strictly newer than the original ACK's status and Leader equal to one of those exact two surviving voters, different from the isolated voter. Then submit exactly one immutable next command; require its actual quorum ACK and fresh status/official MCP status native term. Original cancellation fails honestly after joining its actual observation. Default production status semantics remain diagnostics, not a new strong-read promise; the subsequent actual ACK is the authority proof.

Service-user invariant clarification: the fault Dockerfile inherits and restores the base APP_UID USER. The selected resources preserve the already existing ClusterResourceSettings --user uid:gid override exactly, captured through pinned public ContainerRuntimeArgsCallbackAnnotation before adding NET_ADMIN. They do not change default host/container identity or start service as root. Native tool/read commands alone use --user0 inside each verified owned namespace.

Persisted NodeStatus.NodeId is a physical GUID independent of Aspire node1/node2/node3 resource names. The real flow captures each original authenticated endpoint identity and requires it unchanged after restoration; public SDK/official MCP status comparisons bind that actual GUID rather than an invented resource-name identity.


## Accepted partial native restart ownership (2026-10-08)

TASK-REP-PARTIAL-RESTART-001 refines REQ-REP-004 / AC-REP-004 and REQ-TEST-007 / AC-TEST-007. Before implementation: an actual successful Aspire Start is retained independently from its health/readiness completion. Once attempted, an unobserved Start outcome cannot be repeated; it remains owned until actual application/resource shutdown retires it. Once accepted, subsequent recovery resumes only native inspection, health wait and receipt settlement under the caller's existing token/deadline. It never reissues Start because health wait was canceled. The first observed replacement ID/configured image/image ID/start timestamp is retained and exact unchanged identity is required after health, before the original receipt is published. Native runtime identity changing during settlement fails; no readiness success is inferred from running state. Kill cannot replace an unsettled prior restart owner. The original initiating error and all cleanup errors remain separate.

The fixture exposes a useful two-phase native ownership boundary: BeginContainerRestartAsync accepts the actual Start and observes its running replacement; RestartContainerAsync settles native health and receipts for the same owner. This is fixture lifecycle composition, not a product test hook. AC-REP-PARTIAL-RESTART-001: AcEvent008AcceptedRestartCancellationResumesSameReplacementAndPreservesReplay uses actual killed leader/accepted replacement, cancels at the exact-token original health-settlement admission gate, then resumes under the original operation deadline and proves the same running replacement identity, canonical SDK receipt replay, full literal SDK/official MCP aggregate state on all three replicas. No timer, sleep, fake resource, deadline increase or mutation retry is added. During-health scheduler timing is not claimed: the deterministic cancellation is at the explicit accepted-Start-to-native-health boundary. Existing original leader-loss case remains intact.

Original aa103 run37686560768/job113015778654 failure remains immutable. Its initiating Starting/unknown-health cause is unresolved; this repair addresses only the proven secondary repeated Start. Exact-source Linux native RF3 qualification remains open. Root owns join/build/native execution.

```mermaid
flowchart LR
  K[Verified kill] --> A[Attempt Start once]
  A --> U[Unobserved outcome: retain owner]
  A --> S[Accepted Start]
  S --> I[Retain native replacement identity]
  I --> H[Health settlement]
  H -->|canceled| I
  H --> V[Same identity and original receipt]
```

Restart diagnostic refinement: resumed attempts explicitly record AcceptedStartRetained, never a fabricated new Start success. Begin phase failures save the original exception/category at actual StartCommand or RuntimeInspection stage; diagnostic cleanup failure is attached through the unchanged failure-retention path.


## First-incarnation readiness evidence (2026-10-08)

TASK-REP-READINESS-EVIDENCE-001 refines REQ-REP-004 / AC-REP-004 and REQ/AC-TEST-007. This stage follows TASK-REP-PARTIAL-RESTART-001 without changing admission. For each exact Aspire discovered owned node endpoint, one native GET /health/ready is awaited and its bounded response reader settled/disposed. It shares the existing three-second per-node diagnostic deadline with signed discovery, rather than granting either a second budget. Only status200,503,other HTTP status or fixed unavailable is retained; response bodies are drained within the existing8192-byte cap and never retained as readiness explanations. Server's503 body does not identify DatabaseReady/compatible-cohort/catalog branch, so branch remains explicitly Unobserved. No server response/auth semantics change or inferred reason is introduced.

Each existing actual native ResourceId log subscription records its closed started/running/completed/canceled/faulted state plus observation timestamps of first/last received log lines. These are observer times, not invented native event timestamps; observed faults are rethrown for existing joined disposal. Save snapshots this evidence without replacing its original failure. ResourceId-keyed task lifecycle is retained; no unproved reattachment is introduced. Current24-line buffers and80-line/8192-byte total artifact limits remain unchanged. Evidence groups put resource/readiness/discovery/subscription state first, followed by newest retained logs so prefix clipping cannot omit the latest signal. Existing failure markers remain.

AC-REP-READINESS-EVIDENCE-001 maps to the actual AcEvent008AcceptedRestartCancellationResumesSameReplacementAndPreservesReplay flow: canceled accepted restart saves real diagnostic evidence before recovery; after real all-three SDK/MCP healthy replay, another one-shot observation must contain HTTP200 for all three owned discovered endpoints and actual log observation state. No fake response/source assertion/timer/retry/increased budget. Native execution and exact Linux original evidence remain root-owned and pending.


## KL035 bounded replica-prefix GC contract (2026-10-08)

TASK-REP-PREFIX-GC-001 / REQ-REP-GC-001 / AC-REP-GC-001 freezes actual prefix reclamation. A node-local Materializer holds its existing apply owner, then SnapshotStore gate, then native log ProtocolGate and log lock. CURRENT published snapshot is reverified against native complete image, incarnation, format, checksum and committed cut before any eligible key deletion. One batch deletes actual canonical replica-entry keys with absolute Index<=snapshot.Index using the native replica store atomic Commit; it changes neither hardstate snapshot/term/vote/LastIndex/CommittedIndex nor canonical apply/docs/receipts. Only existing MaxAppendEntries and MaxAppendBytes bound range/work/key retention, checked before allocation; cancellation before commit publishes no effects. The same verified immutable image remains and all suffix entries are retained. Unpublished/corrupt/missing image fails closed before deletion.

The GC batch then awaits the existing REPLICA store Compact publication under the same owner, rewriting replica commands.wal with surviving live keys; CANONICAL Compact is not called. Native ZoneTree derived segments may retain older deleted bytes, and eventual maintainer segment reclamation is explicitly separate/unmeasured. This is actual key deletion plus replica-journal live-key rewrite, not read visibility cutoff or immediate whole-tree byte reclamation. Native compaction failure/poison and original ownership cleanup propagate; snapshot+tail remains at every valid recovery cut. Existing journals/formats/aliases/term/index semantics are unchanged. No discarded atomic journal, migration or compatibility fallback.

REQ-REP-GC-002 / AC-REP-GC-002: maintenance schedules at most one existing joined checkpoint/reclamation task. Bounded later ticks use actual remaining prefix keys as progress; no persisted/stale cursor and no unbounded reclaim loop. Reader results are existing independent native arrays; per-call image files are closed under snapshot gate. GC deletes no snapshot image or transfer file and never invalidates borrowed native read cuts. Compact uses its actual storage write gate/current-generation contract.

REQ-REP-GC-003 / AC-REP-GC-003: real current-format store/CrashHost flows create committed snapshot+new retainedtail/full literal original receipts, reclaim actual eligible keys, inspect exact hardstate/terms/index/suffix/canonical state, native dispose/reopen, fresh-target complete install+tail and exact retry with healthy next command. Corrupt/missing snapshot and canceled GC preserve full native state; repair then healthy GC follows. Real owned child kills at existing publication and replica-store native checkpoint/journal fault boundaries prove at least one complete verified snapshot+tail path. All actual original children/readers/filelocks are joined. This proves process-kill recovery only; power-loss/endurance/current Linux RF3 remain separate pending gates.

Ownership: Replication ClusterReplication/Contracts existing interfaces; Storage native log prefix collector/reclaimer and snapshot owner; Execution Materializer/Maintenance. No new provider, public SDK/SQL/MCP operation or wire format is introduced: this is existing node-local housekeeping, never caller-authorized database mutation bypass. RecoveryTests/ClusterReplication and CrashHost/ClusterReplication own real native cuts and wholeflow assertions. Root joins/builds/native tests/records actual new census. ADR030 remains Accepted until exact required evidence exists.

```mermaid
flowchart LR
 V[Verify current complete image] --> D[Atomic eligible key deletion]
 D --> R[Replica journal live-key rewrite]
 R --> N[Next bounded tick: remaining actual keys]
 V --> P[Keep snapshot and committed tail]
 D --> P
 R --> P
```

### Exact private GC verification map

`ReplicaPrefixGcProcessRecoveryTests.AcRepGc003ActualPrefixDeletionAndReplicaRewriteRetainVerifiedSnapshotTailRecovery` owns ten native argument rows: HeaderWritten/0, PayloadWritten/0, JournalFlushed/0, MutationApplied/0, MutationApplied/3, ApplyCompleted/0, SnapshotWritten/0, SnapshotFlushed/0, InstallPrepared/0, JournalSwapped/0. Existing synchronous native `CommitStage` observer pauses only the admitted replica-store deletion/rewrite position; the real original CrashHost child is killed and its readers joined before reopen. No new product fault callback is introduced. Original cuts before durable journal completion permit only complete prefix retained or complete prefix reclaimed; all later cuts require complete reclamation. Partial key deletion is rejected.

Each actual row verifies snapshot cut4+term1, retained tail5, immutable complete hardstate bytes, canonical inventory equivalence, all literal original put mutation receipts2–5 and byte-identical replay without position changes. A fresh empty native target installs the retained complete snapshot and actual surviving tail entry, applies them, then commits independently specified healthy revision5/cut6 and reopens. The same owned trial additionally checks original-token cancellation, missing native image (FileNotFoundException) and hash-corrupt image (Corruption), full unchanged replica/canonical records and position, exact image repair, successful reclamation and healthy tail. Native images, stores, original child/readers and file locks remain trial-owned through cleanup; failures retain original root/evidence. New case discovery counts/compiled identities and all Linux qualification remain root-owned and unobserved for this packet.

Code ownership adds feature-local `ReplicaPrefixReclaimer`, `ReplicaVerifiedPrefixReclamation`, `ReplicaCheckpointReclamation` and cohesive `ReplicaCheckpointPublication`; the latter moves existing state calculation only, preserving its original locks/guards. New crash/test types are under their actual ClusterReplication Contracts/Helpers/Processes/Assertions/Cases roles. No native aliases/field IDs, storage formats, public SDK/MCP/SQL routes, runtime deadlines or qualification statuses change.

An explicit reclamation call with an already-deleted prefix still verifies the current complete image and joins one native replica WAL rewrite, returning zero without a no-op atomic commit. This settles process interruption between deletion and rewrite. Original cancellation is checked again before that zero-deletion rewrite; once a positive deletion commits, the original rewrite is joined despite later cancellation. The normal maintenance trigger remains actual remaining prefix keys or its unchanged snapshot threshold. No persistent cursor/new recovery format is added.

R2 GC test ownership: each actual native node/materializer operation is observed to terminal completion before every original disposal; all primary and cleanup identities remain retained. Invalid-image restoration observes deletion and original-image rename separately, preserving either failure and attempting both. ReplicaCrashNode construction/disposal joins Log, replica store and canonical store independently. These source corrections do not change GC semantics, fault cuts, resource caps or qualification.


### TASK-CRS-COHORT-NO-QUORUM-DETAIL-001

REQ/AC-CRS-002/005 and REQ/AC-SESSIONREAD-003 preserve the existing authenticated fixed-voter cohort and explicit majority. Only the final aggregate compatible-count-below-majority branch in ReplicaCohortAdmission.EnsureCompatibleCohortAsync returns the existing OwnershipLost/NoLeader diagnostic. SDK reads and MCP's pre-tool authenticated read therefore retain the same established no-quorum safe problem. Individual invalid/unavailable discovery, local transport-not-ready, substituted signature, wrong identity and incompatible reachable peer retain their existing strict diagnostics and rejection; no new catch, retry, threshold, deadline or cache authority.

Root owns this single Orleans branch and extends the existing StoppedSocketsRemoveFreshObservationsAndRequireACompatibleMajority native whole-flow to distinguish exact individual InvalidDiscovery from exact aggregate NoLeader, preserve actual stopped sockets/cache eviction/two-voter survival, and re-admit a fresh healthy signed cohort. Existing transition/signature/cancellation/shutdown controls and real KL021 SDK/official MCP no-quorum/body/privacy/restoration flow remain required. Stage order: docs, code and focused native normal/scalar, full build/format, current native case-source binding, delivered exact-source Linux RF3. Original46a4 safe-detail mismatch is retained; source diagnosis does not identify every historical initiating branch. ADR-082 owns cohort admission; ADR-017 owns public session-read authority.


### TASK-MTOKEN-ISOLATION-ADMISSION-EVIDENCE-001

REQ/AC-MTOKEN-ISOLATION-003 retains every original exact native container namespace guard. The original46a4 inspected-container rejection has no retained compound predicate member. Evaluate the existing conditions in their original short-circuit order and retain the first observed mismatch as one closed enum: running state, privilege, PID namespace, network count/mode, capability count/value, exact name/image/user/build source/incarnation. Invalid JSON/required fields and exact GUID parsing still fail; no alias, alternate namespace, permission or admitted-cohort expansion. On an actual predicate rejection emit one bounded fixed-schema stderr row containing only schema/kind/closed mismatch, then throw the unchanged primary rejection. Retain any output failure after that primary through the existing ServerFailureObserver. No native names, addresses, image refs, identifiers, payloads or credentials are emitted.

IntegrationTests ClusterReplication Contracts owns the closed enum; Validation owns native field evaluation; Diagnostics owns the bounded row and original failure order; Serialization reader consumes them at its existing admission boundary. Root joins these together and builds/formats; genuine Kl021ReachableFormerLeaderRejectsMinimumTokenWhileOtherVotersAcknowledgeNewerTerm and its original exact SDK/MCP/isolation/restoration gates remain the runtime evidence. A future observed row identifies only the actual failing predicate, not a consensus cause or successful fault. Original source/namespace guard, shutdown/resource retention, deadlines and all acceptance counts remain unchanged. ADR-017 owns this additive test evidence contract.

### TASK-MTOKEN-ISOLATION-PARTIAL-RETIREMENT-001: original Linux59 cleanup correction

REQ/AC-MTOKEN-ISOLATION-003/004/005 retain strict native namespace admission and joined ownership after any startup or mutation failure. Original run37744013727/job113201104531 Kl021ReachableFormerLeaderRejectsMinimumTokenWhileOtherVotersAcknowledgeNewerTerm fails first at NetworkMode admission and also fails cleanup because no complete admitted node set was recorded. The primary NetworkMode cause remains unknown; do not change or relax that predicate. Two independent definite lifecycle defects are corrected: partial startup must not require successful admission of every node before invoking the existing native stopped-namespace verifier, and native canonical/replica lock checks must open actual owner.lock, matching ZoneTree's real owner, rather than nonexistent store.owner.lock.

Ordered contract: require actual owned AppHost stop success; invoke existing VerifyNodesStoppedAsync for every exact admitted model name, using recorded full IDs where available and its existing exact-name/unique-full-ID/stopped-state checks for unobserved nodes. Any running, ambiguous, substituted observed ID or native Docker failure retains primary/cleanup/root/image ownership and fails. Only after all namespaces settle, acquire all9 actual native locks (3node owners plus3canonical/3replica owner.lock files); only then run unchanged actual derived-image/base/layer/source proof, no-referencing-container check, tag removal and remaining-tag verification. Capture original retirement/mutation evidence and original cleanup failures. Incomplete admission alone does not establish unsettled resources; actual native stopped and lock proofs do. No weakened namespace, lock, image, deadline, resource, exception or assertion contract is introduced.

IntegrationTests ClusterReplication Lifecycle/ReplicaIsolationOwner.cs owns partial-start shutdown admission; Lifecycle/ReplicaIsolationRetirement.cs owns exact physical locks. Cases/ReplicaIsolationNativeRetirementTests.cs and Fixtures/ReplicaIsolationNativeRetirementFixture.cs exercise real native3node lock files and6ZoneTree stores: committed literal rows, all9-held-lock retirement rejection without FileNotFound, actual owner disposal, all9exclusive canonical lock proofs, unchanged literal rows/positions after native reopen and joined private-root cleanup. This is supporting native storage/lifecycle regression, without claiming Docker RF3 or functional product-contributor coverage. The existing full genuine Kl021 SDK/officialMCP isolation/restoration/retirement flow remains mandatory exact-source Linux proof; source and original failure are not passing runtime evidence. Root owns docs-first guarded join, build/format, native TUnit and required Linux RF3 qualification. Rollback restores both cleanup corrections together; original primary errors remain visible.


### KL021 persisted session-read denial complete adapter continuation

REQ/AC-SESSIONREAD-001..004: extend the existing DocumentSessionReadRf3Authorization revoke→deny→restore operation, retaining its real persisted nonadministrator and valid acknowledged minimum. Direct SDK denial must be unsuccessful with no value and PermissionDenied/403. The same valid minimum through Q1 CALL must fail through both real SDK and official MCP, with no SDK JSON value and the actual dispatched safe PermissionDenied envelope. Credential and literal document content stay absent from official errors. An authorized administrator's full literal DocumentResult at the same minimum remains unchanged during denial. Renewed persisted grants then restore the existing full SDK/MCP/Q1 healthy reads.

Ownership: IntegrationTests ClusterReplication Assertions/DocumentSessionReadRf3Authorization only. ADR-017 owns current token and same-cut authorization, with no schema/product/storage/topology/lifecycle changes. Keep every existing assertion and original bound, failover, namespace, primary failure and joined cleanup. Ordered docs/private guarded review→root join/build→existing Kl021AcknowledgedDocumentTokenSurvivesElectedFailoverAndRejectsInvalidReads through native RF3→authentic current Linux originals; source alone is not acceptance.


## KL021 initial explicit follower snapshot capability

REQ/AC-FOLLOWERREAD-001..005 are canonically frozen in
[DocumentStorage](DocumentStorage.md#task-kl021-follower-snapshot-001-explicit-bounded-follower-document-reads)
and implemented under ADR-017 through the unique authenticated request grain,
actual local follower capture and final fresh quorum/apply barrier. Server-created
native credential witness is revalidated against current actual key/principal,
policy and current/captured row in the same final cut. Captured data is disposable
request-owned memory, never policy or durable authority. Data/authorization cuts,
term/owner/generation, explicit physical-position lag and minimum refusals are
separate and bounded; original strong/session/refusal paths remain unchanged.
Actual authored28 FollowerDocumentRf3Tests cases cover held SDK/official MCP/Q1,
credential/grant/field-policy changes, lag/cancel/no-quorum and full restoration;
all runtime gates remain pending. This point-document stage does not close the
broader KL021 model/read-mode inventory. No offline authorization, silent
fallback, remote owner substitution, latency or production claim is made.


## TASK-KL021-NATIVE-CAPABILITY-OBSERVATION-004

REQ/AC-MTOKEN-ISOLATION-003 retains exact single-capability admission, every native network/name/image/user/source/incarnation guard and the original failure/cleanup order. Current f80/run37861333290 normal and scalar former-leader originals first reject Capability after network-mode equality to the attached native network ID passed. Their schema2 row does not retain the capability literal; it cannot establish which alternative spelling was supplied. Docker 28.0.4 daemon validation discards the normalized capability return, stores the supplied HostConfig and returns that HostConfig in inspect. Do not infer a returned spelling merely from NormalizeLegacyCapabilities.

Ordered additive evidence contract: emit schema3 with the existing closed mismatch/network booleans and only three capability booleans: exact single value, sole exact NET_ADMIN, sole exact CAP_NET_ADMIN. Never emit a native literal, identifier, address, image, payload or credential. Neither capability predicate changes admission: the sole exact NET_ADMIN remains required. No alias fallback, deadline change, policy change, privilege or topology change. The complete Kl021ReachableFormerLeaderRejectsMinimumTokenWhileOtherVotersAcknowledgeNewerTerm native SDK/official MCP/Q1 refusal/restoration flow remains required on current Linux; diagnostics and supporting controls cannot qualify it.

Ownership: IntegrationTests ClusterReplication Diagnostics owns the bounded row; Cases owns a supporting complete strict-admission rejection→healthy control, with independently literal full diagnostic rows. Root joins/compiles and binds its ordinary classification to actual native metadata. ADR-017 remains the owning namespace/session contract. Verify wrong, prefixed, absent and multiple capabilities remain rejected, then the unchanged exact sole capability and exact network are admitted. Preserve all original failed reports; fresh authentic Linux originals must identify the actual producer before any producer/schema correction.


## TASK-KL021-NATIVE-CAPABILITY-SCHEMA-005

REQ/AC-MTOKEN-ISOLATION-003: owner-approved native schema correction separates the configured additional capability `NET_ADMIN` from the exact native Docker inspect representation `CAP_NET_ADMIN`. The actual source023/run37872325740/attempt1 normal and scalar schema3 originals establish one capability, soleCapabilityMatchesNetAdmin=false and soleCapabilityMatchesPrefixedNetAdmin=true, with exact attached network ID equality. Admission stops at Capability before evaluating RuntimeUser; these failures do not prove the service uid, and that unchanged exact non-root service-user guard remains mandatory. Preserve both original failed reports and their archive/source/image bindings.

This correction supersedes only the unprefixed inspect-literal admission expectation in TASK-KL021-NATIVE-CAPABILITY-OBSERVATION-004. Aspire's existing --cap-add=NET_ADMIN producer remains unchanged. Inspect admission requires ONE exact CAP_NET_ADMIN string, with ordinal equality; unprefixed NET_ADMIN, different case, whitespace, ALL, other capabilities, absent values and multiple capabilities fail closed. There is no OR alias, decoder normalization or fallback. Every original running/privilege/PID/network/name/image/user/source/incarnation predicate and ordering remains unchanged. Diagnostic schema3 continues to report the two separate literal predicates without exporting native identities.

Stages/ownership: IntegrationTests ClusterReplication Serialization declares the native canonical inspect constant separately from the configured diagnostic value; Validation compares only that native constant. Cases retains the existing ordinary supporting case identity and complete independently literal refusal/admission diagnostics, including a mismatched runtime user followed by exact healthy admission. Root alone joins, formats/builds, binds current source/PDB/native case metadata and executes native controls plus fresh genuine Linux former-leader SDK/official MCP/Q1 minimum-token refusal/restoration and cleanup. Controls are ordinary, not functional coverage or RF3 qualification. No database migration or product capability change; rollback restores the prior predicate and retains all originals. Current runtime qualification remains OPEN.


## KL035 genuinely empty follower installation contract

TASK-KL035-EMPTY-FOLLOWER-001 implements architecture KL035 and REQ-REP-004/005, AC-REP-004/005. The retained-directory scenario remains unchanged. An additive EmptyReplicaSnapshotRf3Tests operation uses the existing inspected Docker SIGKILL/settled readers and same Aspire restart owner. Before erasure it exclusively opens actual stopped node ownership and canonical/replica owner/WAL/metadata files and rejects links/foreign roots. Only database, replica, snapshots and the eagerly opened disposable search-indexes/text-projections roots within that fixture-owned follower bind mount are erased; actual bootstrap membership, root, profile, immutable configured incarnation and voter cohort stay owned by the same fixture.

AC-KL035-EMPTY-001: the two surviving voters produce forty independently literal documents and a published native snapshot, followed by one actual later acknowledged tail command. The receiving node must apply at least that original token position and have an installed read generation. A genuinely empty ZoneTree canonical store receives its newly created native NodeId under ZoneTreeIdentityFile.Open, rather than copying another node's identity; configured incarnation stays exact and the new local NodeId must survive the later cold restart.

AC-KL035-EMPTY-002: after public SDK/official MCP verification, kill that exact follower again and reopen only its native stopped replica store. Require non-null complete ReplicaHardState snapshot, positive index strictly before the original tail position, matching incarnation and checksum/length of its actual immutable image, committed/last positions covering the tail, and the actual retained tail ReplicaEntry with the original command ID and compatible term. Successful forwarded reads or snapshot-file existence alone cannot pass this criterion.

AC-KL035-EMPTY-003: real SDK and official MCP assert every complete literal document, original full commit/subscription-processing/projection result and receipt, actual source event and change records, subscription checkpoint, outbox/consumer state and inbox deduplication. Fresh cut positions must be monotone; server-created current cursors are consumed through actual continuation to the complete empty tail and are never replaced with old/fabricated cursor bytes. A principal/key genuinely persisted before erase with an unrelated resource grant is denied without value/disclosure; an administrator then reads the complete healthy document. A subsequent real update uses the same token scope, increases position without a +1 assumption, and retains original command replay after that later commit.

Implementation order: retain shared original producer state/internal methods; additive stopped storage owner and native inspection; complete SDK/MCP assertions; new real RF3 case. Exact slice ownership is tests/KeyLoad.IntegrationTests/Features/ClusterReplication/{Cases,Helpers,Assertions}, existing ClientApi official SDK helper borrowed; no shared fixture or production changes. Dependencies are ADR035/036/017, original persisted-policy/ZoneTree/replica APIs and native ADR117 entry. Rollback removes the additive case/helpers and restores internal helper visibility only; no persisted format or public contract changes. Root alone joins/builds/discovers/qualifies the exact new source in Linux. Existing interrupted/corrupt install, checksum, atomic cut, ordered-tail and recovery-path/GC tests remain mandatory; no deadline expansion, new retry, generated snapshot/proof/raw insert, single-node replacement or power-loss/endurance/performance claim. Source-authored assertions are not runtime qualification.


## TASK-KL021-ORIGINAL-GENERATION-CANCEL-001 — actual store fence and initiating cancellation

REQ/AC-SESSIONREAD-002..004, REQ/AC-MTOKEN-ISOLATION-003/005 and the canonical DocumentStorage REQ/AC-FOLLOWERREAD-001..005 retain all original criteria. Original source55f/run37885964187/attempt1 task normal112/113 and scalar111/113 remain FAILED; no current-source qualification is inferred. The complete113-case task inventory and every existing selector/parameter/oracle remain unchanged.

Actual native `StoreIdentity.ReadGeneration` initializes at0; `NodeAdministration.StatusAsync` exposes that same persisted owner fence. Only actual native tree replacement advances it. A newer consensus term or successful quorum read does not imply a positive generation. Capture complete authenticated native statuses for all three distinct physical owners before namespace isolation. Every original direct SDK/official MCP status check binds exact captured NodeId/incarnation/generation, including0, while preserving role, current term, exact voters/durability/routing and actual process checks. After original minimum refusal and real majority newer-term ACK, restored full literal SDK/MCP/Q1 reads and original full receipt replays are followed by exact captured owner/generation checks on all three endpoints. The isolated minority cannot provide a successful authenticated status when its native quorum/persisted-authority barrier denies it; no fallback/status bypass or generation guess is introduced. Its original actual namespace/process verification and null/original/new minimum no-value refusals are bracketed by the exact native pre/post owner fences. NET_ADMIN/CAP_NET_ADMIN/nonroot/exact network-cohort admission remains unchanged.

The scalar OfficialMcp lost-quorum original contains `TaskCanceledException` only at the final failure observer; existing task settlement intentionally creates a canceled-task exception without the initiating frame. Its causal operation is unproven. The test now records a closed owning stage immediately before its existing startup/held-read/change/original/restart/readiness/discovery/MCP reconnect/healthy operations. Catch the actual exception inside RunOwned and inside the existing resumed-owner observer before cancellation becomes a settled canceled Task, emit only stage/caller/change enums, parent/wave/caller canceled booleans, actual exception type and at most16 original source-stack frames, then bare rethrow to the same lifecycle owner and cleanup. No exception Message, request/node/credential identity or user document is exported. Diagnostic output failure combines original+output exception rather than masking the primary. This is bounded original-failure observation, not a guessed product fix, timeout increase, native retry or changed error admission.

Ordered stages: frozen source/evidence contract; Integration ClusterReplication FlowAssertions/Flow/StatusIdentity owner-fence correction; DocumentStorage feature-local diagnostic enum/helper, state and existing Scenario/QuorumFlow stage annotations; root guarded source join/build/native exact113 metadata rebind; actual normal/scalar task lanes with unchanged20 slots/deadlines/source/PDB/images/native reports/cleanup. No production API, generated alias/Id, dispatcher, provider, storage replacement or fixture topology changes. Rollback removes the additive test observation while retaining native fence semantics. Required full-suite gates and broader unsupported follower models/SQL remain open. No task closure until the fresh exact-source originals satisfy all113 outcomes in both profiles.


## TASK-KL035-NATIVE-TASK-ACCEPTANCE-001: complete original snapshot installation

Original architecture KL-035 requires committed snapshot cut/index/term/config identity, checksummed bounded transfer and atomic installation, genuinely empty follower catch-up, interrupted installation recovery and a surviving snapshot+tail recovery path after log GC. This additive execution contract freezes the complete existing operations under REQ/AC-REP-004/005, AC-KL035-EMPTY-001/002/003 and REQ/AC-REP-GC-001/002/003; no original criterion is removed.

| Original criterion | Exact existing operations |
|---|---|
| Empty RF3 follower installs actual snapshot and ordered tail | EmptyReplicaSnapshotRf3Tests.ErasedFollowerInstallsNativeSnapshotAndOrderedTailBeforeCompletePublicReplayAndHealthyContinuation uses same Aspire-owned stopped voter, exact native owner locks/closed no-link roots and genuine database/replica/snapshot and disposable text-root erase. Remaining actual quorum publishes full snapshot and later acknowledged tail. New genuine local NodeId and unchanged configured incarnation/voter count, actual read generation/applied token are required. |
| Complete SDK/official MCP state and native installed cut | EmptyReplicaSnapshotScenario/Storage/Assertions/HistoryAssertions/PolicyAssertions compare all literal documents, original full command/subscription-processing/projection replies and receipts, events/change pages/checkpoints/outbox/consumer/inbox, persisted denial/no disclosure then healthy admin read. Re-kill exact receiving node and inspect its actual complete hardstate, snapshot index/term/incarnation/image length/SHA and original retained tail command; later same-root cold restart/revision2 update keeps original outcome replay. Forwarded reads or file existence alone cannot satisfy this. |
| Retained follower | Existing ClusterTests.RetainedReplicaCatchesUpThroughNativeSnapshotAndKeepsCommandOutcomes remains independent and unchanged; its preserved directories cannot substitute for the genuinely empty follower. |
| Bounded checksum/config/cut/atomic installation | ReplicaNativeSnapshotTests real native unsupported descriptor then resumed healthy install; ReplicaPersistenceTests complete/chunk/replay/corrupt/path/verified-install pointer/stale-cut/private-prefix flows; ReplicaSnapshotMetadataRecoveryTests foreign incarnation/truncated manifest unchanged cut then healthy; ReplicaTermMetadataSnapshotTests exact snapshot/replacement/compaction/warm-cut checks; ReplicaCheckpointProtocolGateTests actual planning gate after native image flush. |
| Interrupted/corrupt installation | ReplicaTransferProcessRecoveryTests actual killed transfer begin/chunk acknowledgement/rejection and publication+cold tail; ReplicaProcessRecoveryTests acknowledged-prefix kill, empty target full snapshot+cold tail, verified/install process cuts and corrupt/missing published image preserving newer canonical state. Every child/reader/file owner is joined. |
| GC retains recovery path | ReplicaPrefixGcProcessRecoveryTests actual eligible prefix deletion/native replica rewrite cuts, exact image/hardstate/term/suffix/original receipts and fresh native target full image+tail. Original cancellation/missing/corrupt image refuse GC with full native cut unchanged; restore actual image then healthy GC/cold reopen. REQ/AC-REP-GC-001..003. |

Four actual root R863 native censuses bind1 Unit case,32 Recovery cases and two genuine RF3 cases (one erased and one retained):35 per profile. Exact canonical type, actual constructor-bearing native report type, method, typed parameters, parameterized display and source paths enter the existing closed task contract. Original native UIDs/locations/process/source-image receipts remain evidence; source Arguments are not native case proof. This is metadata binding, not a test outcome or Linux qualification.

Reuse existing native REQ/AC-TUNIT-ENTRY-010 under ADR117. Root owns docs/contract/closed selector/workflow joins/build/format/Git and authenticates exact delivered-source normal/scalar original artifacts before task closure; security owner maps actual cases and harvests dedicated originals. Scalar RF3 is caller-only. Preserve prior eight tasks including KL033/KL036, all mandatory complete suites, native20 and unchanged native/job bounds/cleanup/admission. ADR035/036/017/041/061 own actual storage/replica/lifecycle boundaries; no data/API/provider/topology migration. Rollback removes only additive lane, retaining originals and prior tasks. Process-kill proof is not power-loss/endurance/performance or production readiness; dependency/full-product gates remain independent.


## TASK-KL035-COMPLETE-SNAPSHOT-ADMISSION-002

REQ/AC-REP-004 and REQ/AC-TUNIT-ENTRY-010 preserve every original KL035 criterion and all35 prior native case objects. Actual root R883 metadata (native0,14 cases,5742 source inputs/437 image inputs, zero drift; stdout SHA02891a853fc6bda7bf31d7d500de9b8b38587360689e895268de85d3c9066fd2) identifies two distinct omitted classes: ReplicaSnapshotRecoveryTests in ReplicaPersistenceTests.cs and ReplicaSnapshotProcessRecoveryTests in ReplicaProcessRecoveryTests.cs. Their14 exact typed native identities are added to the existing Recovery selection. The complete lane is49/profile:1 Unit+46 Recovery+2 actual Aspire Docker RF3. This is metadata admission, never an outcome claim. Existing historical35 binding and every other task/full suite remain unchanged. The neighboring class names in the earlier table did not admit these separate classes.

AC-KL035-PUBLISHED-REPAIR-001 strengthens the SAME four PublishedImageDamageFailsClosedAndPreservesNewerCanonicalState native process rows (SnapshotVerified/Installed × missing/corrupt): retain the genuine published immutable image within the stopped trial, preserve exact original missing/corrupt failure and newer native canonical cut/NodeId/read generation/full receipt. After the original node is disposed, restore only that exact image, cold recover original snapshot4+tail5 and stable original receipts, submit an independently literal new revision5/cut6 native command through the real log/materializer, require its full original receipt and exact retry without another store position, then cold reopen and verify healthy result/old outcomes again. No fabricated receipt, replacement snapshot, recovery fallback, token/deadline change or product behavior. The helper owns assertions for this actual case, not a standalone test.

AC-KL035-PUBLISHED-REPAIR-001 maps to those four existing cases and feature-local ReplicaSnapshotDamageRecovery. Existing node/materializer observed-owner helpers join primary and cleanup failures before trial files are removed. Fixture exact-image repair is not production repair from missing/corrupt bytes. Root must compile, refresh changed-source native metadata/PDB binding, execute actual operations and authenticate delivered-source Linux normal/scalar exact49 union/source-image/TRX/cleanup before acceptance. Original full-suite556 rows and R883 metadata are retained independently; no current PASS, power-loss, endurance, coverage or whole-product promotion.


## TASK-KL035-INSTALL-BOUNDARY-003: actual descriptor limit and original cancelled admission

REQ/AC-REP-004 and original architecture KL035 atomic snapshot installation require an explicit install boundary, independent of existing GC cancellation/log read limits. ReplicaSnapshotInstallBoundaryTests.ActualSnapshotCeilingOrOriginalCancelledInstallPreservesWholeCutThenSameTransferAndColdTailAreHealthy uses the existing real CrashHost SnapshotChunkAcknowledged producer, actual killed/readers-joined native source/target and original partial descriptor. After the same genuine image is fully copied, one source argument validates an exact image-length-minus-one receiver ceiling with valid smaller chunk bounds; the other calls real materializer InstallCheckpointAsync with its original already-cancelled caller token. Exact Validation or original OCE/token must retain full canonical+replica records/positions/hardstate/native local identity/read generation and private image/manifest lengths+SHA. The SAME valid descriptor then installs snapshot4, applies retained tail5, preserves complete ordered original receipts, joins actual materializer/node/source and cold reopens full literal healthy state. This cancellation is before apply ownership; no in-flight IO rollback is claimed. No fixture/threshold/product limit/deadline/retry/provider/format/authority change.

The existing49 task objects remain unchanged. Root must compile and obtain genuine native typed argument expansion/UID/PDB source-image records before adding this class to the Recovery selector; two source Arguments are not native census or passing outcomes. Existing full-suite/fault/coverage/Linux RF3 gates remain mandatory, and process recovery cannot qualify power-loss/endurance. Shared heavyweight fixture ownership remains untouched.


## TASK-KL035-EMPTY-NATIVE-PROJECTION-001: consistent stopped empty-follower cohort

Original exact-source 9c5fe578/run37920436876 normal job113786795759 and scalar job113786795953 both contain a native node1 startup failure in NativeTextRootFiles.VerifyReceipt, before the outer canceled-operation/cleanup aggregate. Production rejects a retained text projection owner receipt whose SourceNodeId differs from the newly created empty canonical store identity. The fixture erased database/replica/snapshots but retained both eagerly opened text projection roots.

Implementation contract: after the original actual Aspire kill joins and while the existing node.owner.lock is held exclusively, validate every existing mandatory erase root with the same no-link regular-tree/file-exclusive checks BEFORE deleting any root. Add only search-indexes and text-projections, the two actual eagerly initialized disposable native text roots bound to the erased canonical NodeId. Canonical and replica retain their explicit owner/WAL/metadata lock checks; text roots use existing recursive exclusive checks for every file. Preserve node root, ownership file, bootstrap/profile/incarnation/voters, all other native directories, survivor storage and production owner receipt checks. No migration/reader fallback.

The SAME existing genuine erased-follower operation is the regression: restart must now complete real native root construction, receive actual quorum snapshot plus ordered tail under its unchanged original three-minute token, show a genuinely new NodeId with unchanged incarnation, complete literal SDK/official MCP state/receipt/persisted-policy denial then healthy read, stop/inspect exact snapshot SHA/hardstate/tail, cold reopen retaining that NodeId and execute the original healthy update/replay. Do not substitute advisory readiness. No new cases, Arguments, UID/count or execution claims; original failed logs remain immutable. Root owns fresh build/discovery and exact-source Linux normal/scalar execution; task49/full recovery/GC/cleanup gates remain mandatory.

Ordered stages: immutable original evidence and this contract; guarded fixture/docs change; root review/join/build/census; original full RF3 case normal/scalar with unchanged deadlines; authenticate task/full-suite originals. Rollback restores prior fixture source, retains original logs and leaves production state/formats unchanged. This is a test-fixture cohort reset, not production recovery or power-loss proof.


## TASK-KL035-INSTALLED-CUT-TAIL-001: observe the installed cut before the later tail

REQ/AC-REP-004 and AC-KL035-EMPTY-001/002/003 preserve the original actual missing-tail failure from source745f4e37/run37951115844. That failure occurred before native hardstate escaped the stopped reader; snapshot coverage versus another missing-entry cause remains unclassified. The fixture now restarts the erased owner through its original final forty-document token, stops the same actual owner, and reads its real complete installed snapshot and verifies its immutable image. It cold restarts that same root and requires the genuine new NodeId to persist before submitting the original fresh later tail command. The final inspection still requires snapshot.Index < original tail position and the actual retained journal entry with exact command identity and compatible term. No forwarded read or manufactured snapshot passes this flow.

Native cadence contract: ReplicaMaintenance schedules a checkpoint at its unchanged configured threshold16 OR an actual retained checkpoint prefix. ReplicaCheckpointReclamation captures the current applied cut before reclaiming that prefix; ReplicaFollowerSender transfers the already published native descriptor. Therefore the stopped pre-tail observation must have no real checkpoint prefix and enough actual last-index headroom for all nine native submissions before final inspection below threshold16. The nine are the new tail, two original final-command replays, two original tail-command replays, two subscription-processing replays and two projection replays. Native ReplicaLeader appends each admitted replay even though the complete original durable outcome is retained. Post-tail actual Applied additionally reserves the eight remaining replay submissions, exposing any original election retry consuming this headroom. The healthy revision2 write remains after final inspection. This is a fail-closed observed prerequisite, without sleeps, retries, fake timestamps, forced maintenance, configuration changes or weakened final predicate. It does not guarantee a qualifying cadence cut or claim runtime success. Any actual unsuitable cut requires original native evidence before further repair.

Ordered ownership: docs/Features/ClusterReplication.md and canonical docs/ADR/ADR-017-ownership-session-tokens.md freeze this contract; IntegrationTests ClusterReplication Helpers/EmptyReplicaSnapshotStorage owns stopped native state/prefix/image reads and bounded failure-only metadata; Helpers/EmptyReplicaSnapshotScenario owns actual Aspire stop/restart order. Missing-tail metadata contains only snapshot/commit/last/tail integer positions and hasEntry false; original refusal is rethrown unchanged, without payload, identity, credential or exception text in this record. Original full receipt/history/processing/persisted policy denial and healthy/cold continuation assertions remain mandatory. Cleanup retains all original primary and shutdown failures and original deadlines. No production, persistence, scheduling, dependency, topology or transport change. Root alone joins, compiles, refreshes actual native UID/source/PDB/image census, and executes Linux normal/scalar49/profile plus required complete recovery/RF3 suites. Source review is not qualification. Rollback removes only the additive phase/read/diagnostic changes; no durable migration.

AC-KL035-INSTALLED-CUT-TAIL-001 additionally requires complete independently literal DocumentResult parity through original SDK/official MCP and real Q1 SDK/official MCP CALL for all forty documents, projection and original tail, plus the final cold healthy revision2 document. Existing full original SDK/MCP receipt/history/processing/policy outcomes remain unchanged. The added Q1 document operations are reads and do not consume replica commit headroom. Four-route write/replay expansion before strict inspection is not claimed; seventeen such submissions would exceed the unchanged sixteen-entry checkpoint cadence.

AC-KL035-INSTALLED-CUT-TAIL-002 freezes the later continuation separately: only after the strict actual snapshot+original tail entry observation succeeds and the same new native owner cold restarts, reconnect the real official SDK caller and replay original final/tail receipts plus original subscription/projection effects through SDK, official MCP, Q1 SDK and Q1 official MCP. Every response compares the complete original typed outcome, and the full forty-document/projection/tail literal and history/processing state is checked again. Then the original fresh revision2 command executes and its complete new receipt and original receipt replay also run through all four routes, followed by the complete literal revision2 document. Later native checkpoint/GC is legitimate and does not erase the already retained strict pre-continuation observation. No source-derived/native outcome PASS is inferred. Helpers/EmptyReplicaSnapshotAssertions owns the explicit after-cold allRoutes flag on these genuine executions; false before the strict inspection preserves its nine native submissions. No extra standalone cases or test-only DTO/getter assertions.


### TASK-KL035-NATIVE-BOUNDED-FLOW-002 — cohesive post-install tail phase

REQ/AC-REP-001 and REQ/AC-REP-004 retain the entire approved KL035 installed-snapshot→same-owner cold restart→fresh ordered tail→strict native journal inspection→full public receipt/processing/cold continuation flow. Native codefix attempts for CA1068 and KLD0032 returned NotFound. Extract only the actual sequential post-install tail preparation into private EmptyReplicaSnapshotScenario.CreatePostInstallTailAsync; return the original command, acknowledged receipt and complete baseline tuple. Preserve its exact RequireApplied/current-owner/generation checks, actual fresh tail commit and original RetryDuringElection, native baseline capture and unchanged16-cadence headroom assertion in the same order. RunAsync retains every Kill/Restart, running/stopped flag, actual MCP pointer/closing and original failure/cleanup join. No lifecycle owner, deadline, operation, native cut or assertion is removed or reordered. Private ProcessingAsync places required allRoutes before final CancellationToken and changes its exact actual callsite; optional public verification arguments and every route assertion remain unchanged.

Ordered ownership: this feature/ADR contract; private Integration ClusterReplication Helpers/EmptyReplicaSnapshotScenario.cs and Assertions/EmptyReplicaSnapshotAssertions.cs current-source guarded successor; root native preview/join/diagnostics; exact current Linux normal/scalar original49 discovery/source/image/TRX and complete actual flows with joined native resources. No new public/trust/persistence/topology/dependency seam or synthetic case is introduced. Original745 failure and strict Snapshot.Index<tail/actual entry/index/term remain immutable requirements. Root alone owns live/Git/build/CI, author only immutable private preimage/postimage/patch. Rollback this cohesive extraction/signature delta only; no stored-format or production change. Native compilation and qualification remain open, and no runtime PASS or missing-tail cause is inferred.


### TASK-KL035-OBSERVED-TAIL-BEFORE-REPLAY-003 — retain the native tail before complete replay

REQ/AC-REP-004/005 and AC-KL035-INSTALLED-CUT-TAIL-001/002 preserve all original empty-follower, complete public-state, persisted-policy, original receipt, same-owner cold restart and healthy revision2 criteria. The authentic aeb source/run37981021139/job113991243820 failed before tail creation: the installed native suffix was11 and the fixture reserved9 future submissions, giving20 against unchanged threshold16. This is an unsuitable fixture phase forecast; it does not prove a snapshot installation defect. ReplicaLeader.SubmitAsync appends each admitted replay before resolving its original outcome. ReplicaMaintenance selects a current applied checkpoint when the actual configured16-entry threshold or retained prefix requires it; neither mechanism promises an installed suffix below7.

Before implementation, place the strict native inspection immediately after the genuine later tail command, before all eight existing SDK/official-MCP receipt and processing replays. The stopped installed cut still requires the complete actual compatible snapshot, checksum and reclaimed prefix; its headroom reserves only the one command actually remaining before inspection. After genuine tail acknowledgement and current-owner catch-up, its actual applied suffix must remain strictly below16. Kill/join that exact Aspire follower, retain the actual Snapshot.Index < original tail position, exact native tail entry/command/term and complete image, then cold restart the same root and require the same native owner. Only after this observation run BOTH unchanged complete verification phases (original SDK/MCP and expanded SDK/MCP/Q1 routes), persisted denial/no-disclosure then healthy admin read, and fresh revision2/full original replay. All original literal documents, history/checkpoint/outbox/inbox/projection outcomes, original receipts, two official SDK sessions and joined cleanup remain; later legitimate replay-triggered checkpoint/GC cannot erase the retained earlier observation.

Ownership: Integration ClusterReplication Helpers/EmptyReplicaSnapshotScenario.cs only for operation order and actual remaining-command accounting, this feature and ADR017 for the precise refinement. Production snapshot selection, threshold16, limits, budgets, membership, authorization, storage format, test identities and required full suites stay unchanged. No maintenance forcing, retries, waits, fabricated positions, provider substitution or runtime claim. Rollback restores this fixture operation ordering only. Root alone joins/builds/censuses/executes exact current-source Linux normal/scalar49 and mandatory recovery/RF3 gates; source preview is not qualification.

### TASK-KL021-NATIVE-BODYLESS-STATUS-001

REQ/AC-REP-003/005 preserves the complete isolated-former-leader and restored-owner SDK/official MCP flow. AdminStatus has the existing closed no-body input schema: use McpOfficialClient.CallWithoutBodyAsync at both actual status callsites; CallAsync<T> instead adds a request property which the native decoder correctly refuses. Preserve the same tool, persisted credential, original token, two actual official callers, complete NodeStatus identity/term/incarnation/generation checks, minority read denial, healed read and joined cleanup. This caller correction changes no server schema, permission, result or test identity. ADR-017 already defines these boundaries; no new ADR is needed. Exact-source Linux execution remains required.


## TASK-REP-COHORT-TERMINAL-DISCRIMINATOR-001

REQ-REP-004 / AC-REP-004; REQ/AC-TEST-007; TASK-REP-COHORT-TERMINAL-DISCRIMINATOR-001; ADR036. This is bounded diagnostic evidence, never readiness, membership or authorization authority.

Original b392 17 final prerequisite observations pass transport, configured non-null leader, positive term/commit, materialized coverage and stop at false cohort. They cannot distinguish local protocol/current-reader/transport, fresh peer incompatibility, or insufficient fresh ready observations. Preserve the exact original ordered short circuit and majority predicate.

Introduce native-local readonly ReplicaCohortAdmissionStatus with one closed enum and six integer counts (required majority, evaluated remote, fresh remote, missing remote, expired remote, ready including local). Codes: NotObserved, GrainFactoryUnavailable, HostUnavailable, DiscoveryStopping, LocalProtocolMismatch, LocalReaderMismatch, LocalTransportUnavailable, PeerProtocolMismatch, PeerReaderMismatch, InsufficientFreshReady, Compatible. No identities, addresses, strings, payload, roles, credentials, exceptions or timestamps. Missing and expired derive from the exact existing cache lookup/TTL/removal, not another read.

Old public bool property retains its signature and result and delegates the same evaluation, discarding a value-type status. Add only native diagnostic method on existing discovery client, no inter-grain/publicHTTP/MCP/persistence formats, serializer aliases/Ids or trusted inputs. Discovery stopping and node unavailable are closed codes. Server captures exact status in existing prerequisite observation at the same gate; final existing generated Warning event1016 adds code/counts. No extra event, log loop, discovery request, timer, deadline, allocation of observer state or option. Default status values are native structs; only existing startup observation instance remains.

Original cache checks, native auth/signature/current contract/current incarnation, fixed voter mapping, placement, majority, startup task and shutdown stay unchanged. Both valid and invalid current-layout admission remain strict; separate sealed042 directory repair is not Linux-cause proof.

Map original89 cases and existing complete public/cohort negative→healthy/cold flows to fresh Linux RF3 evidence. Root runs original six owners and all deadlines and authenticates new reason plus complete operation/cleanup outcomes. Unit getter assertions are not qualification and no synthetic provider/discovery test is introduced. Original b392 remains failure. Join docs first, then existing guarded sources and exact new diagnostic files. Rollback only observation additions, no stored-format migration.


### TASK-NATIVE-COHORT-WHOLE-CONTROLS-001 — genuine signed admission/refusal controls

REQ/AC-REP-001 and REQ/AC-REP-004 and REQ/AC-TEST-007 map to RequestCqrsCohortObservationWholeFlowTests: SignedReadinessRefusalPreservesOriginalIdentityThenFreshMajorityAdmits, SignedFirstInvalidPeerOverridesReadyMajorityThenExactRecordsRepairAdmission, and OriginalSignedCacheNaturallyExpiresThenUnavailableRefusalAndFreshHealthyAdmission. The existing three-silo Orleans fixture and original three Kestrel peers supply actual nonce-bound native bytes signed by ReplicaEnvelopeAuthenticator. Production discovery verification/cache/admission evaluates every operation; tests never populate its cache.

Preserve the same configured voter/cluster/incarnation/silo identities, 1.5-second lower-election TTL, original RPC limits and 20-second whole-case deadline. Compare all closed branch counts with absence of observation-triggered HTTP work, retain actual OwnershipLost plus exact original NoLeader/IncompatibleCohort/InvalidDiscovery safe details, then restore exact original signed records and require fresh native address resolution and compatible admission. Natural expiry removes the original cache entries before fresh healthy resolution. Supporting TestCluster/signed HTTP flows are distinct from mandatory exact-source Docker RF3 startup and KL042/KL079 SDK/official MCP/Linux gates; no source/test declaration is a PASS. No schema/alias/field-ID/authority/provider change.


### TASK-KL079-COLD-NATIVE-COHORT-ACQUISITION-001

REQ/AC-FEED-005, REQ/AC-REP-004 and REQ/AC-TEST-007: catalog prerequisite must actively obtain missing/expired genuine signed native cohort observations rather than waiting for unrelated outbound replica work. Catalog startup currently precedes RuntimeJournalStartup acquisition; incoming replica RPC does not fill the private discovery cache. A current cold follower may therefore materialize leader heartbeats while its own cache is empty. This is a source-proven liveness counterexample; original b392 seventeen RoleFollower/cohortFalse observations do not identify the historical branch and remain failed/unknown.

At the same original cohort short circuit, after all existing native consensus guards, await the existing owned signed EnsureCompatibleCohortAsync with the original readiness token/RPC deadline/current-contract/MAC/fixed-voter/TTL checks. Only its exact existing OwnershipLost/NoLeader maps to an insufficient-ready value for the unchanged heartbeat poll; all other original errors and cancellation propagate. After actual acquisition/work settlement, evaluate the same closed predicate/status, which remains mandatory before admin auth/catalog bootstrap/admission. No cache injection, extra loop/retry, new provider/endpoint/option, relaxed majority, changed clock/deadline or persisted/public schema.

RequestCqrsColdStartupAcquisitionTests.ColdFollowerAcquiresItsOwnSignedCurrentCohortWithoutUnrelatedOutboundWork and OriginalUnavailableAndIncompatibleRefusalsAndCallerCancellationPrecedeHealthyAdmission execute genuine existing3-silo/Kestrel nonce/native-MAC operations with complete original identity/address and refusal→healthy assertions, zero-request cancellation, all counts and joined client/endpoint/signer cleanup. Supporting native tests do not replace original exact-source Linux KL079 full SDK/official MCP/Q1 snapshot/tail/privacy/reconnect/revocation/cold operation or KL042 whole restore gates. Selectors/old b392 TRX identities are retained separately; fresh current native UID/image/runtime outcomes are required. Join discriminator13 → signed-cohort5 → this guarded successor; no source declaration is qualification.

The cold acquisition owner supersedes its cache-only OrleansNode.CohortAdmission projection. Native semantic reference search proved that projection had no remaining callers; remove it and its exclusively owned OrleansNodeCohortObservation helper under the immediate unused-code policy. The live startup still uses the actual signed acquisition and its closed status, with unchanged admission and error contracts. This source cleanup does not qualify runtime acceptance.

# TASK-KL035-EMPTY-ONLINE-ROOT-001

REQ/AC-REP-004/005 and AC-KL035-EMPTY-001/002/003 require the genuine erased follower to acquire a new canonical NodeId, install the actual RF3 snapshot and ordered tail, and complete the existing full SDK/official MCP/Q1/cold receipt and healthy continuation.

The authentic own-main source8c3a/run38017420672 scalar job114110692708 log SHA256 c66f4c41de76c2d94e5df20d820469ce402ba1de4b307aed47687de592d764ea records restarted node1 failing NativeTextRootFiles.VerifyReceipt before cancellation. Current native NativeTextHostResourceRoots.Register eagerly registers search-indexes, text-projections and native-text-online. Every receipt is bound to database.Store.Identity.NodeId. EmptyReplicaSnapshotStorage erased canonical storage and only the first two derived roots; retaining the third across new canonical identity violates the intended complete disposable-projection erase. The production foreign-owner refusal is correct and remains unchanged.

Implementation ownership: only Integration ClusterReplication Helpers/EmptyReplicaSnapshotStorage plus append-only ClusterReplication/ADR017 traceability. Add native-text-online to the existing closed erase inventory. Acquire the existing actual stopped node.owner.lock, validate ALL six existing directory trees and all regular files/no links/exclusive handles BEFORE any deletion, then delete all six and assert absence. Preserve node root/owner lock, configured incarnation/voters/profile, survivor roots, all canonical/replica lock checks, native snapshot/tail/receipt values and original three-minute cancellation/cleanup contracts. No production adoption, root migration, new observer, default or format change.

Automated regression is the SAME ErasedFollowerInstallsNativeSnapshotAndOrderedTailBeforeCompletePublicReplayAndHealthyContinuation operation. Its actual erase absence assertions now include online projection; subsequent new NodeId, real installed snapshot SHA/cut/tail and same-owner cold/native complete public records/receipts/index/policy/healthy continuation remain mandatory. No added case/Args/UID or qualification claim. Existing foreign-root ownership negative operations remain unchanged. Rollback removes only the fixture inventory addition, restoring the authentic failure. Root must build/discover and execute exact-source Linux normal+scalar original case and complete KL035 recovery/RF3 suites; source preview is not acceptance evidence.


## TASK-KL035-CURRENT-NATIVE-TAIL-HEADROOM-002

REQ/AC-REP-004 and REQ/AC-FEED-002/005 preserve the original genuine erased-follower snapshot installation, checksummed native image, same-owner restart, fresh subsequent tail command and complete SDK/official MCP/Q1/cold/feed41 oracles. Original df75 normal/scalar UID95c7915c-a20f-4fe4-f8b9-c1ef5d58034f failures report old-baseline differences21/22 and remain failures; they do not expose the final native snapshot/entry state or identify background commit causes.

Actual ReplicaMaintenance.ScheduleCheckpoint uses current canonical LastApplied minus current Snapshots.Current.Index. Native CaptureAndReclaim legitimately captures/replaces that checkpoint at a later exact applied cut; a subsequent restart does not freeze the previously observed snapshot. Therefore later headroom must be measured against the SAME current stopped native snapshot used by the existing strict tail-entry/image oracle, rather than an old pre-restart snapshot. Keep initial installed-prefix settlement, original full image and LastIndex-installedSnapshot+one-tail<16 admission unchanged. After the original tail has genuinely applied and the owner is actually stopped, require current snapshot.Index>=original installed snapshot.Index; current snapshot.Index<the exact tail receipt position; current native LastIndex-current snapshot.Index<16; committed/last cuts cover that original tail; retained native tail index/commandID/term match. Full current snapshot checksum is still mandatory. Legitimate newer checkpoint is admitted only by these exact predicates; a snapshot covering the tail, missing entry, altered image, older checkpoint or suffix exceeding16 still refuses.

Ordered ownership: this appendix precedes private source; only EmptyReplicaSnapshotScenario passes the original snapshot cut to its existing final stopped-state assertion and removes the stale online status comparison. Native node readiness/status/replication/storage/checkpoint scheduling remain unchanged. Existing63-line flow capture extraction, original case declarations/Args, all full receipts/models/processing/privacy/cursor41, faults and initiating/cleanup ledgers remain unchanged. No polling/retry, snapshot cadence/configuration/limit/deadline/topology change or fabricated index. Root runs authentic current Linux normal/scalar SDK+official MCP+Q1 RF3 and native recovery/source/image/census gates. Source correction is not runtime PASS or historical root-cause closure. Rollback removes only this oracle-origin correction and appendix coherently.


### TASK-KL035-CURRENT-NATIVE-TAIL-FAILURE-FACTS-003

The final stopped-owner refusal retains bounded original installed snapshot/current snapshot/committed/last/tail integer facts and actual hasEntry, including absent native-entry and strict ancestry/headroom/image/assertion failures. No payload, command/owner identity, exception message or credentials are exported. The initiating native/assertion failure remains first and keeps its original identity/stack; synchronous diagnostic failures join the same existing failure ledger. Successful calls emit nothing. This supplements the exact current-snapshot oracle without changing any predicates, original whole case, deadline, topology or runtime qualification requirement. Rollback removes only this failure-observation appendix and its two owning helper edits; original snapshot/tail proof remains unchanged.


### TASK-KL021-RESTORED-FOUR-ROUTE-REPLAY-004

REQ/AC-SESSIONREAD-004 and REQ/AC-MTOKEN-ISOLATION-005 preserve the SAME former-leader case and its two original acknowledged commands/tokens/complete receipts. Original healthy token reads already exercise SDK, official MCP and both Q1 CALL routes on every restored voter; original command replay only exercised SDK. This additive whole-flow edge replays BOTH original commands through all four actual routes using a fresh official administrator caller with the original persisted bootstrap credential. Compare every complete typed receipt to its original native serialized bytes and bracket all four submissions with full original OutboxStatus equality. The unchanged final independently literal revision2 token read across all four routes proves no second producer effect or image regression. No new command, case/declaration/UID, trusted role, clock, deadline, retry, topology or product/persisted/public contract. Existing caller disposal remains lexical and joined.

Original df75/run38031746813/attempt1 failures remain distinct and unqualified: normal same UID2b76bf70-6325-b0bd-7507-fb199ad8284b fails official Create/discover in VerifyNodeAsync after RestoreAsync with503 OwnershipLost; scalar fails original majority replacement CommitAsync before refusal/restoration with UnknownWriteOutcome. Initial old-reader/admin official connections occur before isolation. Scalar lost-quorum UID6ee0fae7-4bec-586a-4994-df8bbfb92e53 has native explicit2026-07-28 initialize refusal; that client message does not prove unsupported server protocol or the initiating server cause. No common bootstrap-sequencing fix, protocol fallback or Unknown-as-success is inferred. This source extension is not a causal repair of those failures.

Ordered implementation and ownership: append this contract to ClusterReplication and ADR017; extend only existing ReplicaIsolationFlowAssertions.ReplayedAsync and its two real restored-node callsites in ReplicaIsolationFlow. Native typed official SuccessAsync and Q1 SqlRf3Protocol reuse actual command IDs/payloads; no alternative dispatcher or decoder. Root alone joins, compiles/discovers, binds original source/DLL/PDB/image/UIDs and executes complete KL021 Linux normal/scalar Unit/process/Aspire RF3 gates. Focused selector remains /*/*/DocumentSessionIsolationRf3Tests/*. All original refusal/privacy/minimum cut/isolation/restore/status/full literal and failure-evidence cleanup paths remain. Rollback removes these additive route/no-effect checks only. Source preview is not runtime PASS or task closure.


## KL035 native naturally published cut readiness, existing original flow (2026-10-10)

# KL035 naturally published snapshot readiness — exact missing boundary

REQ/AC ClusterReplication snapshot installation/ordered-tail/receipt recovery and ADR-017. This is readiness-only test infrastructure. Original scalar e92 stopped oracle17<16 failed; normal passed. No historical causal integer is inferred beyond the authenticated original assertion.

The native filesystem seam is insufficient: ReplicaSnapshotFiles.ReadManifest is incoming-transfer state, discarded by ClearIncoming. Published images contain bounded canonical checkpoints but cannot prove the currently selected ReplicaHardState.Snapshot or LastIndex. DurableReplicaLog.Save commits current hardstate in its exclusively owned ZoneTree StateStorageKey. No live secondary store open, newest-directory authority, force checkpoint, extra write, torn header, inferred LastIndex or stopped-assert retry qualifies.

Construction: preserve original ClassDataSource<ClusterFixture>, SharedType.Keyed, same constructor parameter/method and all original test declarations. Change only this case's fixture sharing key to a closed KL035 snapshot-readiness key. ClusterFixture implements native pinned TUnit.Core.Interfaces.IKeyedDataSource; TUnit assigns its Key before original IAsyncInitializer starts (official native1.73.19 XML lines7600–7622). Only that exact key selects one existing RequestCqrsProbeFixture with fresh bounded session/per-voter roots before AppHost Build. Add existing profile arguments before resource construction and retain controls until stream/producer, actual app and volume cleanup join. All ordinary keys/default fixture behavior remain uninstrumented. No new MethodDataSource, changed parameter, second topology or post-start arm injection. Independently compare actual compiled case identity before execution.

Covered admission: current ClusterResources explicitly refuses RequestProbe with NativeCoverageRf3ClusterImage. The scoped new ReadCovered profile validates actual already-verified covered image/root plus the same existing ephemeral RF3/no-benchmark/private-owner path. It must not manufacture ProtocolCohort or Github metadata. Existing coverage source/DLL/PDB/image/selection/root gates run unchanged before profile creation. Local profile continues native ReadLocal validation. This is an explicit additional original covered-fixture diagnostic mount, not a broad admission bypass.

Capture: append one internal Server probe phase ReplicaSnapshotReadiness after all existing values; no public DTO/operation/persisted IDs. Arm only Hold, GrainReadKind.NodeStatus (public AdminStatus), empty CommandId, actual persisted administrator, target voter and original session/arm/request. At actual NodeAdministration.StatusAsync after the existing ReplicaConsensus.StateAsync returns its consistent state, use the active claim of that SAME independently verified signed request and exact current context RequestId. Existing OrleansNode runtime observer borrow returns the current original IGrainRequestPhaseObserver, never a new activation/observer. RequestCqrsProbeObserver may capture only if claim identity/principal/kind/target/phase all match and original expiry/cancellation/current persisted admin checks passed. No Store.Read gate remains held over callback await.

Closed private evidence JSON: Version=current existing probe protocol; Kind=replica-snapshot-readiness; SessionId/ArmId/RequestId bind exact original owned records; SnapshotIndex/MaterializedPosition/CommittedIndex/LastIndex are actual native long integers. No term, role, principal/token/credential/endpoint/user payload, unknown keys or caller-supplied integer. Use the existing strict source-generated JSON/files owner and its original maximum record bytes/file count/aggregate bytes. Sidecar filename derives original arm, is committed before same-phase Observed marker, included in strict known-file inventory and retained through release and ProducerDisposed. Charges remain bounded under original quotas; no unbounded evidence accumulation. Fixture retires each settled arm using existing owner validation; original immutable records and arm history remain charged/retained under original32-arm/400-file/aggregate limits. No arm-history reset or evidence deletion is inferred from retirement.

Readiness: original whole3m token and existing bounded Eventually owner perform genuine signed AdminStatus on erased restarted follower. Arm→actual capture/hold marker→read exact sidecar→release→await original status producer→ProducerDisposed→retire. Return only when SnapshotIndex>0, MaterializedPosition>=final receipt, CommittedIndex>=final receipt, LastIndex>=CommittedIndex, and checked(LastIndex-SnapshotIndex+original one tail reservation)<16. Otherwise no stopped assertion is attempted yet; natural background checkpoint may publish while no read/hold/snapshot gate is retained. Every failure releases/cancels/joins same original tasks and preserves initiating plus evidence/cleanup errors.

Then unchanged actual Kill/join and independent stopped ReadInstalledState/immutable checksum/prefix/image/headroom oracle must pass. Continue original restart/fresh tail command/term/exact journal/current snapshot ancestry/strict snapshot<tail plus full four-route replay/feed/privacy/history/cold/healthy. Readiness observation never replaces stopped authority or proof of successful operation.

Required genuine controls: exact original compiled case discovery before execution; complete original normal/scalar RF3 flow under canonical closed local selector; covered Linux original native source/image/UID/cleanup; negative wrong identity/private root/schema/cancellation settlement uses existing meaningful operation controls. No source-only PASS or power-loss claim.


### KL035 single original readiness caller refinement
The readiness phase uses exactly ONE original signed NodeStatus request and ONE original bounded arm. Before the existing Observed/Hold marker, the same callback reads naturally published consistent ReplicaNodeState under its original consensus gate, releasing that gate before each await. It uses the existing validated RequestProbeExecutionOptions.PollInterval and original caller/host cancellation; no new polling, timeout, quota or checkpoint configuration. The predicate is SnapshotIndex>0, MaterializedPosition>=CommittedIndex, LastIndex>=CommittedIndex and checked(LastIndex-SnapshotIndex+one original tail command)<the original configured SnapshotThreshold16. The fixture already proved the final receipt applied and then independently requires this observed cut's applied/committed positions to cover that receipt. Actual published snapshots are monotonic; the stopped independent oracle remains mandatory.
Only after one qualifying actual cut is atomically written under the original file/record ledger does the same claim publish Observed and enter the original Hold. The fixture reads exact sidecar/session/arm/request/voter marker, releases and joins that same original producer and ProducerDisposed, and retires only the original control arm. Original immutable history/byte/file/32-arm charges remain retained. Readiness never retries the stopped assertion.
The original NodeStatus response uses the actual returned observed native state. Before returning any instrumented response, the owning ConnectionReadCapabilities repeats the same signed scope/identity/token check and freshly reloads persisted administrator authority. The fixture requires real successful completion before using the sidecar. An already-revoked/expired caller cannot turn an Observed marker into qualifying authority.
This exact keyed fixture provisions a real c1-probe administrator and real credential through root-authorized public ConfigurePrincipal/ConfigureApiKey before original captured feed/cut. c1 grammar stays unchanged; no root arm exception or trusted role. Ordinary fixture keys/profiles/defaults remain unchanged. Coverage-image admission reuses its already validated native image/source/output root and exact ephemeral RF3 profile, not a manufactured protocol cohort. Shutdown joins original producer/gate and AppHost resources before original private control cleanup and owned-root deletion; uncertain cleanup retains evidence/root and all initiating/cleanup failures.


Fixture argument composition remains in the existing ClusterFixtureComposition owner: move the original coverage/local/namespace/snapshot argument block intact into its bounded PrepareArguments method, return the actual selected root/arguments/original readiness owner, then assign those same values before AppHost creation. This responsibility extraction preserves all original default/profile conflict checks while keeping ClusterFixture class and initializer within existing bounds; it creates no additional lifetime or topology.

#### Validated observer composition refinement

The snapshot-readiness observer borrows the original centrally validated `IOptions<ReplicaConfiguration>` and original request-probe execution options. `OrleansNode` exposes only its existing silo-owned observer reference; `NodeAdministration.StatusAsync` invokes that same observer without new ownership or a default observer. Both original stopping tokens remain final constructor parameters and participate in the same linked cancellation. The threshold, identity and publication predicate remain unchanged.

The existing `OrleansNode.Connection.cs` owner contains the two-line borrowed observer accessor alongside its actual connection execution entry; no additional partial-type header is introduced. This preserves the original aggregate type ceiling and the same silo-owned instance.

The original scenario groups its existing applied-prefix status/new-NodeId assertions and the same readiness join in `RequireInstalledReadinessAsync`, returning that actual installed status to unchanged subsequent tail/cold assertions. This is a bounded same-case phase extraction; it introduces no case, state owner, public schema or independent acceptance claim.

Before actual silo start or after it has been cleared, the borrowed accessor returns no observer without invoking the existing throwing `RuntimeServices` accessor. With a running silo it borrows only that silo’s optional existing observer. Therefore ordinary readiness/status retains the original no-observer behavior; no diagnostic instance, startup dependency or authority is manufactured.


### TASK-KL035-NATIVE-READINESS-CLOSED-TARGET-004

REQ-REP-004/005, AC-REP-004/005 and AC-KL035-EMPTY-001/002/003 preserve the same original erased-follower operation and all exact snapshot/tail/SDK/official MCP/Q1/cold/healthy oracles. Private R36 normal actual original UID95c7915c-a20f-4fe4-f8b9-c1ef5d58034f failed at44.041075s in RequestCqrsProbeJson.ReadArm called by WriteArm, before its signed readiness status producer. This is a demonstrated test-observer schema omission, not a native snapshot or historical e92 timing diagnosis. All initiating/cleanup results and the original source/image/discovery receipts remain immutable.

The existing RequestCqrsCanonicalArmValidation owner adds only the closed ReplicaSnapshotReadiness shape: NodeStatus, Hold, empty CommandId, null Partition/SourceRequestId/SourceArmId and a target accepted by the existing exact three-native-origin RequestCqrsProbeActivationTargetValidation. Null/unknown target, another phase/kind/action, a command or adjunct identity refuses. Existing canonical-journal/BeforeSubmit/sample-pair validation remains byte-preserved. Existing ReadArm still validates c1 principal grammar, current protocol, session/arm, enum and native record/UTF8 bounds; the callback still compares this target with the actual independently signed claim and local ReplicaNodeState voter, and terminal persisted administrator validation remains mandatory. No wildcard target, root exception, caller role, new credential or authority is admitted.

Ordered join: this append-only contract follows the actually joined Natural23 docs; add the finite branch in the existing validator only. No public/persisted IDs, aliases, quota, timeout, snapshot threshold, retry or stopped oracle changes. Reconstruct against exact current main guards. Required proof is the SAME original complete case with unchanged compiled/native UID, normal then scalar under fixture-owned heavy1 after actual fixed-source build/discovery and cleanup; existing closed negative arm controls remain mandatory. Source preview/reconstruction is not runtime or Linux PASS. Rollback removes only this branch and appendix; immutable original failure is retained.


### KL-035 original readiness marker publication closure (2026-10-10)

REQ-REP-004/005, AC-REP-004/005 and AC-KL035-EMPTY-001..003 retain the original erased-follower whole operation and UID. The actual private R37 normal case failed after the authenticated native readiness sidecar had been durably published and before its Observed marker: the retained exact request/arm/session matches the original safe CapabilityExecution rejection and ProducerDisposed marker. Its actual cut was SnapshotIndex 81, MaterializedPosition 86, CommittedIndex 86, LastIndex 86; no original e92 stopped-state integer is inferred from this later observation. The existing closed RequestCqrsProbeRecords.ValidateMarker phase admission omitted ReplicaSnapshotReadiness, so its next WriteMarker rejected the already implemented internal phase.

Ordered implementation: preserve the original R37 initiating OwnershipLost and cancellation cleanup evidence; append only ReplicaSnapshotReadiness to the existing marker phase admission; retain all original marker version/kind/session/voter/request, canonical entry, exact active-arm/scope, duplicate, immutable byte and marker-count checks; retain sidecar-before-Observed publication and independent fixture qualification. No public/persisted ID, protocol, trusted role, threshold16, deadline, quota, operation effect or stopped oracle changes. All existing marker phases and refusals remain mandatory.

Ownership is the existing Server ClusterRouting Execution/RequestCqrsProbeRecords.cs. Dependencies are the actually joined Natural23 readiness observer/sidecar and closed-target successor3. Join these current guarded documents before that one native source correction; rollback restores its exact preimage and does not reinterpret any observed evidence. The same original EmptyReplicaSnapshotRf3Tests case must run normal then scalar against an unchanged discovered source/DLL/PDB/image, through genuine Kill/join, strict installed image/ordered tail, SDK/official MCP/Q1 replay, cold and healthy continuation. Native marker source/preview or a qualifying running cut does not close the original stopped or Linux acceptance gates.


## TASK-KL035-POISONED-INSTALL-COLD-001

REQ-REP-004 / AC-REP-004: preserve both original ReplicaTermMetadataFailureTests InstallPrepared/JournalSwapped cases, original stage callback, actual RecoveryRequired poison, rejected-read accounting and exact original repeated-disposal assertions. Their missing finite continuation is same-directory native recovery after all original target handles have retired. Freeze this implementation before source: retain the genuine complete four-record original replica image, hard state, position and identity before the fault; after original close, open only the same configured directory/incarnation with existing validated execution options and no fault observer. Require byte-exact complete original image and hard state, unchanged position/NodeId/incarnation and the actual installation's one read-generation increment. Then append the independently specified real entry2/term2, commit2 and applied2, verify complete expected hard state/entry and five-record image, close every owner, reopen again and require the complete same healthy image/state/cut.

Use existing native ZoneTreeStore/DurableReplicaLog and ReplicaMaterializerLifecycleErrors for initiating+cleanup identity-preserving joins; no new fault API, provider, authority, schema, migration, clock, deadline, budget, case/Args/UID or selector. The unchanged original process-kill/snapshot/public RF3 cases remain separate mandatory gates. Private normal/scalar owning full cases are development evidence only; delivered Linux process/RF3 and whole KL035 acceptance remain OPEN. Rollback removes only this supporting test continuation and its helper. Root owns guarded integration; private source/image checks and independent reconstruction precede sealing.

## KL-035 recorded-source snapshot acceptance

All three original KL-035 criteria passed in the authenticated49-case Linux lane at `b1f57eb8d487270983b428d835c922701dd14ed7`: empty-disk follower install plus ordered tail through actual SDK/official MCP, interrupted installation recovery, and retention of the last snapshot/tail recovery path during log reclamation. [Exact task/job/artifact/REQ/AC evidence](../implementation/qualification-stage57-kl035-kl079.md) distinguishes this recorded-source acceptance from later joins and the broader cluster, endurance and power-loss gates. Historical failures are retained.


## TASK-KL009-NATIVE-ORLEANS-MOVEMENT-004 — owner correction2026-10-11

The owner now requires actual Orleans startup for movement tests and rejects custom network listeners, fabricated silo generations, loopback aliases and test networking substitutes. This direction supersedes the earlier private lane constraint to preserve its manual listener topology. The original KL009 source8 and actual macOS Socket.Bind failure remain immutable historical evidence, not acceptance. Current main base: f0d869c08f1a9efe30e93911c4631ad26eb87fb0.

REQ-REP-NATIVE-MOVEMENT-001 / AC-REP-NATIVE-MOVEMENT-001: the retained native writer regression starts existing TwoRf3MembershipWave, whose TUnit-owned AppHost starts/readies all six real Orleans nodes as two genuineRF3 groups. Real SDK and official MCP calls execute the existing complete linked-model seed and public parent movement. Require actual terminal receipt and placement, original committed model/output assertions, actual stopped original roots with every owner lock released, changed outerGUID refusal through native DurableReplicaLog without writes, then exact original same-index append, joined same-directory cold observations and real restart plus public original result/model replay. Preserve original deadlines, bounds, signed per-operation identity, connection-owner execution and cleanup; no skip, retry, increased retention, fake authority or topology fallback. Failure to retain the needed actual entry is a native failure, not permission to synthesize one.

REQ-REP-NATIVE-MOVEMENT-002 / AC-REP-NATIVE-MOVEMENT-002: clustering and inter-node database coordination use native Orleans grain/grain-service APIs. Existing PartitionReplicaGrainService/ReplicaGrainServiceClient demonstrate the selected native per-silo endpoint and SiloAddress discovery shape. PartitionMovementClient currently owns separate HTTP effect/outcome/receiver-issue/retire-cancellation/transfer-data transport, and membership authority uses HTTP too; replacing a listener alone cannot qualify this criterion. Native movement transport must borrow the same production PartitionMovementReceiver, PartitionMovementPeerAdmission and bounded PartitionMovementRuntime, preserve exact-byte authentication, purpose/freshness/correlation, persisted authorization and native connection execution, and use actual runtime-generation discovery. Root owns this prerequisite product contract and registration; a test-only dispatcher, embedded HttpContext, parallel command implementation or sidecarTestCluster does not satisfy it.

Slice map: ClusterReplication docs; Core InternalSerialization existing typed comparison; IntegrationTests Features/ClusterReplication Cases/Helpers/Assertions/Contracts/Models own the native writer AppHost regression; existing movement fixtures retain their ClusterRouting ownership and are reused through their actual contracts. Existing manual movement listeners/direct journal fixtures must be retired through the complete mapped caller closure once native counterparts preserve their whole-flow oracles. No partial source-only fixture addition claims that retirement complete. Backend/public formats, stable aliases/Ids, native journals and atomic ownership remain unchanged.

```mermaid
flowchart LR
  T[TUnit] --> A[Existing Aspire two RF3 AppHost]
  A --> O[Six native Orleans nodes and PartitionHosts]
  T --> C[Real SDK and official MCP]
  C --> G[Connection owner and movement grains]
  G --> O
  O --> S[Joined stopped native roots]
  S --> N[Actual retained native append and complete images]
  N --> R[Native restart and public replay]
```

Source/API proof is recorded in the private native-Orleans packet. Compilation, authentic discovery, operation execution, original control, Linux/RF3, coverage and complete custom-network retirement remain OPEN until original exact-source evidence exists.


## Native discovery admission and resource-owner closure

# TASK-REP-NATIVE-DISCOVERY-CLOSURE-001

REQ-REP-NATIVE-DISCOVERY-CLOSURE-001 / AC-REP-NATIVE-DISCOVERY-CLOSURE-001: preserve native membership alias/Ids and exact authenticated caller/destination full generation, payload, nonce and expiry verification. Auth consumes the SAME centrally bound validated IOptions<ReplicaConfiguration> supplied by its genuine authenticator owner, not a new configuration reader or ConfigurationBinding exception. The one original caller argument must pass configurationOptions; root composes that adjacent one-line hunk separately.

REQ-REP-NATIVE-DISCOVERY-CLOSURE-002 / AC-REP-NATIVE-DISCOVERY-CLOSURE-002: native discovery admission remains zero-time, configured MaximumAdmissions, no queue or timer. Use native WaitAsync(ImmediateAdmissionMilliseconds, original token). The synchronous membership metadata owner consumes only the immediately completed zero-time bool task; command execution awaits it. Read uses original lifetime shutdown token, Execute uses original caller token. Every admitted finally releases before operation lease exits.

REQ-REP-NATIVE-DISCOVERY-CLOSURE-003 / AC-REP-NATIVE-DISCOVERY-CLOSURE-003: validate all constructor arguments before allocating owned resources. Close admission, cancel original callbacks, join actual original leases, then idempotently dispose admission semaphore and owned stopping source through the SAME lifetime callback. Explicit DisposeAsync path invokes the same guarded semaphore cleanup after original lifetime settlement for analyzer ownership visibility; no early disposal or borrowed authentication disposal. Keep original error mapping and cleanup exception retention.

Existing genuine native tests, not duplicated getters: PeerDiscoveryOwnershipTests all3 original methods; PeerDiscoveryReplayTests and PeerDiscoverySecurityTests complete original declarations/Args; original native fixture obtains actual ISiloStatusOracle/TestCluster, never a mocked membership table. Root coherent compiler/analyzers, original native50/SIMD operation execution, exact source/image Linux and Aspire RF3 gates remain OPEN. No limit/default/schema/public identity/provider, retry, trusted role, timeout or global analyzer change.

```mermaid
flowchart LR
 Authenticated --> Admitted --> Callback --> Released --> Drained --> Disposed
```


## TASK-PEER-DISCOVERY-LIFETIME-001 — native recovery owner closure (2026-10-12)

REQ-PEER-DISCOVERY-LIFETIME-001: The original recovery discovery fixture owns its acquired authenticators/runtime; the native cluster owns its TestCluster and nodes; each node owns its materializer and term-metadata lifetime. Partial construction must release every acquired owner and original private directory while preserving the initiating and cleanup exceptions. Borrowed cluster references remain borrowed. Successful construction transfers ownership once; the original memoized disposal task joins runtime/consensus before storage and directory disposal. No deadline, capacity, secret, serialization or transport contract changes.

AC-PEER-DISCOVERY-LIFETIME-001: Preserve every original discovery security/ownership/replay operation and declaration. The lexical fixture RunAsync captures operation errors before implicit disposal and records acquisition/disposal errors afterward; no exception is lost. Direct materializer/journal/TestCluster disposal stays in the same original ordered shutdown. Required canonical recovery execution and native analyzer recognition remain OPEN until the source is compiled/executed; source review does not qualify process/power-loss or Linux gates.

Source map: RecoveryTests/Features/ClusterReplication/Fixtures/PeerDiscoveryFixture.cs, PeerDiscoveryNativeCluster.cs, PeerDiscoveryNativeNode.cs. Original actual cohort, signed caller authentication, real silo registration and original startup/shutdown limits remain unchanged.


## TASK-CQRS-COHORT-LIFETIME-001 — native detached-owner join (2026-10-12)

REQ-CQRS-COHORT-LIFETIME-001: RequestCqrsCohortRuntimeFixture disposal returns the same original shutdown task to every caller, including concurrent/repeated callers. It joins original runtime/consensus/cluster/node operations before releasing fixture registration and clearing the owned secret. During genuine voter restart, the actual detached node is lexically owned: restart failure remains first, disposal failure follows, and both settle before any fresh membership observation. An unexpected detached owner is rejected; both detached and original node owners are joined, never silently lost. A failed removal leaves the original dictionary-owned node for fixture cleanup.

AC-CQRS-COHORT-LIFETIME-001: Preserve existing RequestCqrsCohort whole-flow declarations, exact native voter/incarnation/address, signed calls, generation-change oracle, startup/shutdown bounds and actual healthy/rejected/replay operations. Direct cluster disposal stays in the original ordered shutdown and every fatal/nonfatal error is retained. Required native Unit50 SIMD execution, compiled discovery and analyzer recognition are OPEN. This source-only correction does not prove causes for historical Linux cancellation or introduce a retry, timeout, authority, topology or synthetic-success seam.

Source map: UnitTests/Features/ClusterReplication/Fixtures/RequestCqrsCohortRuntimeFixture.cs. Existing original scenario/native node owners remain unchanged.


## Authenticated native caller cancellation and healthy continuation

# TASK-REP-NATIVE-CALLER-CANCELLATION-001

REQ-REP-NATIVE-CALLER-CANCELLATION-001 / AC-REP-NATIVE-CALLER-CANCELLATION-001: the existing native callback executes only after genuine group MAC, configured voter/full generation/current local membership and replay admission. A bounded one-shot observer retains original native bytes and SAME cancellation; its Entered task causally marks that authenticated cut. Cancel the real original caller CTS, retain exact signed OwnershipLost/empty payload/error-only shape, independently verify the sender's real MAC/nonce.

REQ-REP-NATIVE-CALLER-CANCELLATION-002 / AC-REP-NATIVE-CALLER-CANCELLATION-002: cancellation releases the one configured original admission slot before a fresh request under SAME receiver owner. Preserve real membership view/full actual metadata, fresh healthy original generated bytes, and exact duplicate-nonce refusal. Close the SAME original Stop task, join callbacks before resource disposal; repeated Dispose/Stop keeps the original task and borrowed authenticator usable on a genuine native GrainService call.

Implementation ownership is one appended PeerDiscoveryOwnershipTests declaration plus two feature-local helpers. Existing3 declarations and every original assertion unchanged. Real existing PeerDiscoveryNativeCluster/ISiloStatusOracle, generated serialization, original Replay nonce and signer; no fake oracle, fabricated payload/MAC, provider/dispatcher, production hook, retry, new timer/deadline or budget widening. Independently configured test owner MaximumAdmissions1 and ReplayNonceCapacity2, existing centrally validated bounds. Runtime/code after R2 coherent predecessor, compiler/native UID/actual native50 SIMD operation/RF3/Linux OPEN. This native owner support does not substitute for public Aspire RF3 or current-format cold qualification.

```mermaid
flowchart LR
 MAC --> CurrentGeneration --> Entered --> CallerCancel --> SignedRefusal --> Released --> Healthy --> ReplayDenied --> JoinedStop
```

### Native current-generation refusal and original nonce continuation

REQ-REPL-NATIVE-GENERATION-001 and AC-REPL-NATIVE-GENERATION-001 require the existing AuthenticatedNativeCapacityDoesNotHideReplayOrForgedInput operation to prove genuine signed caller-generation refusal before nonce admission, unchanged complete native membership, and same-original-nonce full native result success. Original forgery/replay/capacity controls and bounds remain. This native TestCluster scope does not qualify process cold or public RF3; runtime and Linux gates remain open.


### TASK-REP-NATIVE-DISCOVERY-COHERENT-UNION-001

The current native RequireCurrent authenticates configured voter and complete caller/destination SiloAddress generation before replay admission. A genuine re-signed changed caller generation returns Unauthenticated; the unchanged original generation with SAME nonce must then succeed. OwnershipLost remains the separate original canceled admitted callback result. Both typed errors retain exact signed nonce/empty payload/empty discovery signature. All original case declarations, capacity/forgery/replay controls and all eight generated discovery fields remain required. Cancellation closes/releases the original one-slot owner, joins the actual original task and SAME memoized Stop before direct disposal in finally; fatal/nonfatal original disposal errors remain ordered and retained. The receiver authenticator is borrowed and must remain usable for the subsequent genuine native GrainService call. No fake membership, generation adoption, timeout/capacity change, retry, UID inference or runtime PASS. Current compiler/native50/LINUX/public RF3 remain OPEN.

REQ-REP-SUBMIT-001..004 and AC-REP-SUBMIT-001..004 refine the existing native failure contract in [SubmissionOutcome](ClusterReplication/SubmissionOutcome.md): actual log Commit boundary, strict shared canonical outcome observation, exact persisted terminal proof, genuine operation regressions and separate native RF3 fault qualification. Private source packets do not close the OPEN compiler/runtime/coverage/fault gates.


## KL-035 retained installed-owner full public cold continuation

REQ-REP-004/005 / AC-REP-004/005 / AC-KL035-RETAINED-COLD-001: retain the original retained-follower method/UID,40 acknowledged document commands, genuine native snapshot installation and generation advancement, subscription/projection effects and every existing assertion. Following its original first restart, reuse that same actual follower/root for TWO additional Kill/join→Restart/readiness cuts under the ORIGINAL3-minute caller token. This is one-node cold within the still-genuine RF3 fixture; no shared fixture-wide restart/new topology.

Before either cut and after each, real SDK and official MCP independently replay the unchanged final command, original subscription-processing command and original projection command with their original IDs. Compare each complete public outcome to the retained original acknowledgement, not only Token/AlreadyProcessed. Read all40 independently literal doc-0..39 records (revision1, exact numeric JSON/reference/deletion/fields) and the literal projection record, retaining exact41-change history and subscription/outbox checkpoint checks. After second cold, a fresh command must return a genuine later same-authority RF3 receipt, full literal mutation result and exact same-ID SDK/MCP replay; old original receipt remains byte-exact.

The existing outer retained-follower failure owner tracks each actual stop/restart, joins original official MCP scopes and propagates initiating plus disposal failures; a failed cold cut reuses original failed-replica restore path and its unchanged cleanup deadline. No clocks, limits, receipt fields, caller roles, aliases, schema, provider, selector or TUnit declaration/UID change. No strict stopped store opener or READY115 dependency is introduced. This strengthens an existing complete public snapshot case and does not duplicate its process-install/GC controls. Source/preview is not compiler, runtime, Linux, endurance or power-loss qualification. Historical49-case receipts remain source-specific.


## TASK-KL021-NATIVE-DISCOVERY-PROFILE-001 — original namespace fixture admission

REQ-KL021-DISCOVERY-001: The explicitly selected native namespace-isolation fixture owns one existing bounded RequestCqrsProbeFixture before Aspire model construction. Reuse ClusterFixtureSnapshotReadiness.Create/Apply/JoinAfterStop; no arm or principal provisioning is needed for the original persisted administrator signed discovery. Default and unrelated fixture profiles remain uninstrumented. The diagnostic endpoint remains fixture-only; health/readiness is independent of signed discovery authentication.

AC-KL021-DISCOVERY-001: The original Kl021ReachableFormerLeaderRejectsMinimumTokenWhileOtherVotersAcknowledgeNewerTerm reaches VerifyStartedAsync with authenticated native discovery for every actual owned voter before namespace mutation, then retains its complete original SDK/official MCP authority, refusal, restoration and healthy assertions. Current owner/generation, nonce, cluster/incarnation and original MAC checks remain unchanged. A non-200 response is not a signature failure. Original run38223377791/job114724826120 failed before scenario entry; its actual response status/body was not retained. Current source predicts the absent route when both enable settings are false, independently of readiness200; a future exact-source runtime must verify this causal repair.

REQ-KL021-DISCOVERY-002: Existing owning cleanup joins the actual Aspire application before deleting the probe owner tree; failed stop or owner cleanup retains evidence under the existing ClusterFixture rules. No duplicate lifecycle, timeout increase, public endpoint widening or diagnostic authority substitute is permitted.

AC-KL021-DISCOVERY-002: Original namespace resources/image rules and every initiating/cleanup failure remain preserved. Successful cleanup removes the actual owned probe tree after app stop; a failed join retains it. Compiler, Linux whole-flow and original cold/replay gates remain OPEN until Main executes them.

Canonical slice map: IntegrationTests Features/ClusterReplication/Helpers/ClusterFixtureComposition.cs selects the explicit profile; Helpers/ClusterFixtureSnapshotReadiness.cs creates/applies the existing owner; ClusterFixture.cs owns unchanged startup/JoinAfterStop; ClusterRouting/Helpers/RequestCqrsRf3SignedDiscovery.cs verifies original signed observations; Server ClusterReplication/Diagnostics/NativeSiloDiscoveryDiagnosticEndpoints.cs retains unchanged fixture-only mapping and fresh persisted administrator/current native generation admission.

```mermaid
flowchart TD
    A[Explicit namespace isolation profile] --> B[Create actual bounded probe owner]
    B --> C[Apply existing profile before Aspire build]
    C --> D[Join real RF3 resources and verify signed native discovery]
    D --> E[Original namespace fault and SDK MCP assertions]
    E --> F[Stop and join original Aspire resources]
    F --> G{Cleanup succeeded}
    G -->|yes| H[Delete exact owned probe tree]
    G -->|no| I[Retain original evidence and failures]
```


## Native discovery contract before transport and after owner settlement

REQ-REP-NATIVE-DISCOVERY-CONTRACT-001: Outbound discovery accepts only the configured voter and a bounded canonical full Orleans SiloAddress, including its generation. Parse with the pinned native Orleans TryParse and require exact ToParsableString roundtrip before constructing or signing the request or entering transport. Blank, malformed, oversized and noncanonical caller addresses produce Validation/InvalidPeer; valid native authentication bytes remain unchanged. This does not relax inbound membership, current generation or replay admission.

AC-REP-NATIVE-DISCOVERY-CONTRACT-001: PeerDiscoverySecurityTests.OutboundNativeContractErrorsAreTypedBeforeTransport retains its original voter refusals, exercises every bad-address class, and then completes the genuine healthy exchange with the same native owners. Transport must not receive invalid requests. Compiler and exact-source Linux runtime evidence remain required.

REQ-REP-NATIVE-DISCOVERY-CONTRACT-002: A settled/disposed native discovery runtime rejects its original asynchronous operation with ObjectDisposedException before borrowed authentication or membership work. Observe the original returned faulted Task asynchronously; preserve each synchronous signer/verifier disposal check and joined owner cleanup.

AC-REP-NATIVE-DISCOVERY-CONTRACT-002: NativeDiscoveryDisposedOwnerTests.ActualDisposedNativeSignerAndVerifierRefuseEveryDiscoveryEntry observes that exact asynchronous exception and retains its entire healthy/disposal flow. PeerDiscoveryOwnershipTests.ClosedNativeRuntimeRejectsBeforeBorrowedAuthenticationOrMembership remains the independent original ownership control. No test declaration, UID, production lifetime or disposal contract is removed.

Existing native movement peer-refusal diagnostics now distinguish only closed shape stages null, body, payload and outer-signature-length, followed by the original nonce, MAC, error and success validation. This attribution carries no caller content or credentials and changes no wire/authentication contract. The original R21 bad64 failures reached shape before prepare/cold recovery; their issuer cause and full recovery acceptance remain open.

```mermaid
flowchart LR
    A[Configured voter and full native address] --> B[Bounded native parse and exact roundtrip]
    B --> C[Unchanged native signature and transport]
    C --> D[Genuine authenticated discovery result]
    D --> E[Join actual original owner]
    E --> F[Original disposed Task refuses before borrowed work]
```


## Native cold acquisition owner and refusal repair, 2026-10-12

REQ-REP-COLD-ACQUISITION-001: A fresh original native cohort scenario owns a fresh observation interval. The shared actual native endpoint resets its nonce-enrolled request counter with its gates and selected metadata only at the existing scenario boundaries, after the preceding original discovery client has joined disposal. These counters measure enrolled native discovery callbacks, not database or consensus outbound work. Preserve the literal cold zero and acquisition one assertions; do not subtract an observed baseline or rewrite expected counts.

REQ-REP-COLD-ACQUISITION-002: Aggregate unavailable-cohort trials stop both original remote native discovery owners through their existing StopListeningAsync ownership path. They must obtain the original aggregate OwnershipLost/NoLeader refusal and missing-peer observations. A signed individual InvalidDiscovery error remains an individual refusal and must not be normalized to NoLeader. Repair restarts both actual remote silos through the existing RestartVoterAsync lifecycle, joins old owners and native membership stabilization, requires both real generations to change and rebinds fresh signed discovery records from those actual generations. No synthetic generation arithmetic, product catch, retry, deadline increase or authority change is introduced.

AC-REP-COLD-ACQUISITION-001: Preserve RequestCqrsColdStartupAcquisitionTests.ColdFollowerAcquiresItsOwnSignedCurrentCohortWithoutUnrelatedOutboundWork and OriginalUnavailableAndIncompatibleRefusalsAndCallerCancellationPrecedeHealthyAdmission declarations and native UIDs. The same original20s case bound covers actual unavailable refusal, real restart, current signed majority, incompatible third-peer refusal, exact repair, original enumerator/caller cancellation without extra discovery work and fresh healthy admission. Retain every literal error detail, cohort count, request counter and joined cleanup failure. Existing signed invalid identity, tampered bytes, natural cache expiry and stopped-owner transitions remain independent controls.

The coherent R26 additional lane recorded these two original failures with stable source/images and joined processes: the second fresh scenario retained a previous endpoint count1, and the unavailable trial injected a signed InvalidDiscovery receiver error while asserting aggregate NoLeader. The individual product diagnostic remains correct under the frozen aggregate-versus-individual contract. Native source preview and implementation are not runtime or Linux qualification; the original failures stay retained and Root owns compiler, focused native execution and mandatory suites.

```mermaid
flowchart LR
  Cold[Fresh joined observation interval] --> Stop[Stop two actual discovery owners]
  Stop --> Refuse[Original NoLeader and missing peers]
  Refuse --> Restart[Restart both actual silos and join]
  Restart --> Generation[Rebind genuine new generations]
  Generation --> Controls[Original incompatibility and cancellation controls]
  Controls --> Healthy[Fresh signed healthy cohort]
```

The actual repair is phased: restart the first remote owner and verify the original two-ready majority while the second owner remains genuinely stopped, then restart the second owner before publishing its original incompatible-third-peer control. Both new generations are independently required to differ from their actual stopped owners; no simultaneous restart may erase the original two-ready assertion.
