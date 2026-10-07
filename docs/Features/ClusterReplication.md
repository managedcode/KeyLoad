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
