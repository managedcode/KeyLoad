# DueCoordination within Messaging

Accepted S2 contract for KL-100, REQ/AC-JOBS-005 and
[ADR-094](../../ADR/ADR-094-orleans-due-coordination.md). S1 canonical records,
UTC due arithmetic, caller/creator checks, occurrence identities, queue limits
and atomic transitions remain the authority. No separate durable due index or
storage-format transition is introduced.

TASK-DUE-RF3-AUTH-ORACLES preserves REQ/AC-DUE-002/003/004 and
REQ/AC-JOBS-004/005 after original run37242346547 at5fa61f2. Luna lifecycle_wave
owns a private overlay of DueFaultRf3SeedWriter, DueNoQuorumRf3Run,
RecurringSagaRf3Support, RecurringSchedulePolicyRf3Tests and
DueRecurringRf3Assertions, plus a feature-local pure creator model and identity
writer if required. Root owns these docs, source joins and all gates.
Configure the leader/cold-wave schedule and saga using a real persisted scoped
principal/API key created by the private profile administrator. The administrator
may configure resources and identity; it must not substitute for the retained
schedule/saga creator or receive an authorization bypass. Bind the exact three
queue scopes, required capabilities and actual payload/header policies and
field grants; preserve every canonical identity, deadline, replay and cold/fault
assertion. Keep credentials only in the private owned fixture, never a receipt.
For the no-quorum case, keep the restricted creator for all database work and
use the existing profile administrator only for the restored/survivor Status
topology assertions. Preserve exact leader, voter, caught-up and single-effect
checks. Protected schedule redaction paths are JSON pointers: the independent
oracle is payload:/secret and headers:/secret, with unchanged projected bodies,
ordered paths, cross-client parity and no-disclosure checks. No product change,
new fault, clock, role supplied by a client, corpus/timing reduction or weakened
assertion is authorized. Existing ADR-092/094 remain sufficient; no new wire,
storage, trust boundary or topology is introduced. Implement setup authority,
restricted-work/admin-status separation, then exact projection oracles; review
all base/post hashes before root joins and runs serialized Aspire RF3. Source
and compiler success alone do not close these criteria or the full118-case gate.

TASK-DUE-RF3-C adds the genuine no-quorum public recurrence gate for AC-DUE-003.
TASK-DUE-RF3-BUILD preserves AC-DUE-002/003/004 and ADR-094 while making the
existing S1 and S2 Messaging RF3 fixtures compile under the unchanged repository
analyzers. Luna lifecycle_wave owns a private overlay of Messaging test files
only, using the exact diagnostics from build56. Resolve actual contract members,
unnecessary imports and nullable checks; extract executable units without
changing their assertions, corpus, timing, faults or client paths. Each actual
wave and official MCP client must have explicit ownership and unconditional
joined disposal on every path, retaining both primary and cleanup failures.
Use try/finally and the existing failure observer rather than swallowing a broad
primary exception. Never replace cleanup with detached work or suppress a
diagnostic. Root reviews the complete diff, joins after the current build ends,
and owns full solution build, formatter and actual Aspire RF3 execution. A
compiler fix is source evidence only and cannot close the runtime gate.

TASK-DUE-ACK-DISCOVERY maps AC-DUE-003 to the remaining actual lost-ACK gate.
Luna lifecycle_wave reads the native request phase observers and existing scoped
RF3 fault fixtures and writes only a private source-bound proposal. Identify the
exact real phase between committed autonomous due effects and receipt delivery,
the original task/stream cleanup join, persisted creator and stable command
identity, and independently observed SDK/MCP outcomes after restart/retry. No
new test hook, public endpoint, trusted caller role or write-capable change is
approved by discovery. Root freezes the exact fault/ownership contract before
implementation; a delayed or rejected uncommitted attempt is not lost-ACK proof.

Luna lifecycle_wave owns only new Messaging Cases/Helpers/Assertions/Models files
in a private current-source overlay; root owns the shared wave fault join and
gates. Start an isolated all-current Aspire RF3 wave with its immutable image
proof and persisted profile. Configure a unique future UTC recurring schedule
through the actual SDK, next interval one day, protected persisted creator and
an independently derived expected occurrence ID. Require exact SDK/MCP ordinal0
and no occurrence before the due instant. Capture actual leader/voter identities,
kill two scoped owned voters before the deadline (including the actual leader),
and retain their genuine failure receipts; never launch independent Docker nodes.

Keep only one voter through the real due instant plus a five-second observation
window. Its readiness must be503 and SDK plus official MCP authorized database
reads must reject with the actual closed unavailable-quorum contract; MCP initial
503 is acceptable only before a session exists. Do not infer a readable zero
watermark from a failed read or claim to have inspected uncommitted storage.
Restore exactly one original killed voter using the owned AppHost resource, prove
its discovery address changes while the continuously running survivor's does
not, and require autonomous ordinal1 with the exact ID/due instant/definition,
full projected cross-client parity and no ordinal2. Restore the last voter and
verify caught-up applied state and unchanged one-occurrence outcomes on all3.
All resources, actual tasks and native handles must settle on every path; retain
primary and cleanup failures. No manual Emit, fake clock, service-disable switch,
direct Apply or trusted caller roles. This proves the public quorum boundary and
resumed single effect; exact offline no-quorum-state and lost-ACK injection remain
separate mandatory evidence and cannot be claimed from this case.

| Requirement | Measurable acceptance and automated mapping |
|---|---|
| REQ-DUE-001: finite fair discovery | AC-DUE-001: real ZoneTree schedules/sagas spanning more than a page, canceled/not-due/terminal records, mixed full partitions and concurrent lower/higher-key insertions preserve a fixed sweep upper bound; every readable preexisting key is visited, page count is at most32, actual examined bytes include lookahead and differ explicitly from admitted bytes, cancellation rejects executable work, no payload or open view escapes. Unreadable provider frames fail explicitly. `DueJobDiscoveryTests` |
| REQ-DUE-002: native isolated Orleans execution | AC-DUE-002: per-silo grain service wakes the current leader only; a partition-keyed coordinator grain creates a fresh request GUID and signs the existing typed Batch, consumes native ManagedCode CQRS to settlement, scopes the creator ID through ManagedCode identity/RequestContext, and reloads persisted authority. Production DI/Graph joins and actual Aspire RF3 autonomous emissions pass. `DueCoordinationRf3Tests` |
| REQ-DUE-003: canonical retries and lifecycle | AC-DUE-003: duplicate hints/timers, lost ACK, cancel/completion, creator revocation, restart and leader loss preserve one occurrence identity or one saga timeout; no-leader does not change canonical state; stop cancels and joins all started work before storage shutdown. Native process and RF3 tests |
| REQ-DUE-004: bounded admission and visible failures | AC-DUE-004: one page and one dispatched job per service at a time; fixed tick and total job deadline, at most one retry with the same command ID for uncertain transport, failed jobs cannot monopolize the page, safe structured failure diagnostics contain no templates/credentials/IDs; healthy subsequent jobs still run. Unit and actual RF3 failure/cancellation cases |

Discovery is a trusted node-local internal API, never an SDK/MCP operation. It
reads unchanged `recurring-schedule` and `saga-state` native prefixes. Each prefix
has its own disposable owned cursor: incarnation, ReadGeneration, fixed encoded
upper key and last visited key. Capture the tail with bounded reverse VisitRange;
scan forward up to that captured tail, then begin a new sweep. Alternate prefixes
after completing each page. New keys behind the cursor are found on the next
sweep; new keys beyond the captured tail cannot indefinitely delay older keys.
Reset cursors after incarnation/ReadGeneration change and restart. They are hints
and never claim a cross-page snapshot or authoritative due ordering.

Use borrowed VisitRange bytes, charge examined bytes (including lookahead) before
native decoding, cap a page at32 records and admitted/decoded value bytes at
DatabaseLimits.MaxBatchBytes, and
check a100ms discovery deadline before/after work. A single corrupt or oversized
record within the readable provider range yields a safe rejected-record marker and advances its bounded key rather
than executing it or indefinitely starving the following key. Actual examined
bytes include the borrowed lookahead/rejected row and remain bounded by the
native ZoneTree64MiB range-work ceiling; they must never be reported as admitted
bytes. If a valid next value would cross the admitted page budget, stop before
decoding/copying it and retain the preceding key so the value is revisited. A
single value above MaxBatchBytes is not decoded/copied: reject it, advance only
its canonical bounded key and stop that page. A physically unreadable frame or
range exceeding the native ceiling fails explicitly; no unknown key is invented.
The cursor key ceiling is4,096 bytes. A raw key above that ceiling, including
captured-tail discovery, fails the page/prefix with Corruption before copying;
it cannot be skipped using an incomplete key or a lossy cursor. The service
defers this prefix and permits the alternate prefix to progress on later ticks.
Such rejection
must remain observable; it is not a successful complete index. Decode and check
the entire canonical key, full lane, identity, revision/generation/ordinal,
supported UTC definition and checked due arithmetic. Return only owned metadata
(kind, full lane, ID, creator ID, generation/revision/ordinal and due instant),
never templates/state JSON or retained views. Local time only selects wake hints;
leader-logged EvaluatedAt is the sole due decision at apply.

`RecurringDueGrainService` runs on each silo; after native runtime readiness it
uses the borrowed consensus leader check and quorum read barrier before one
page. It keeps at most32 owned hints and drains them before advancing the scan.
Only one job is in flight. Every page cycle waits1second through TimeProvider. Routing
uses `IRecurringDueCoordinatorGrain` keyed by complete AtomicPartitionId; moving
this activation cannot move native storage handles. The service is not a new
write authority and does not call DatabaseEngine.Apply or SubmitNativeAsync.

The coordinator creates one Batch containing EmitRecurringOccurrences(max1) or
ExpireSaga(expected revision), preserving full identity and current creator.
Its command identity is SHA256 over the existing canonical KeyCodec encoding of
`keyload-due-command-v1`, kind as Int64 (schedule0, saga1), full tenant/database/
transaction-domain/partition/queue, item GUID in N format, revision, generation
and ordinal. The GUID uses the first16 hash bytes; if empty, use the last16.
Saga hints pin generation0 and ordinal0. This identity remains independent of
request GUIDs; a public retry with the same creator, mutation and command ID
converges with a service attempt under the existing canonical receipt contract.
For each attempt it creates a fresh IRequestGrain GUID, sets the existing bounded
native request state and subject-only claims, signs via GrainRequestCodec, and
drains the existing native CQRS stream. One5second dispatch deadline includes
both attempts; an uncertain result may retry once using the SAME command ID and
payload but a new request GUID. After a definite failure or exhausted uncertainty,
release the job and continue the page. Creator reload, hint validation and handled
transport failures are isolated per job; a rejected creator cannot repeatedly
abort the page before healthy following jobs. The service deadline includes
routing and activation as well as native stream settlement. Stop cancellation
still escapes the per-job error boundary and joins all started work. Future
discovery rechecks canonical state;
restart does not require retaining the command ID because S1 atomic watermarks
and Waiting CAS already prevent duplicate occurrence/timeout effects. Definite
NotDue caused by clock skew is deferred, never treated as an emitted occurrence.
Fresh command IDs on later scans cannot weaken canonical generation/cancel/CAS.

The existing identity scope/admission helpers move to ClusterRouting/Identity
in the Orleans assembly and are shared by Server and this coordinator. Preserve
exact native limits, claims, context restoration and request/command correlation;
do not copy a second weaker implementation. The native grain-service marker is
`IRecurringDueGrainService`, alias `keyload.orleans.messaging.due-service.v1`,
and derives from the actual Orleans IGrainService system-target contract. Native Graph permits service entry
into this coordinator and coordinator-to-IRequestGrain only; the existing request
to database-grain edges remain unchanged. Core grants internal visibility only
to Orleans/Server for this trusted node-local discovery join.

Ordered tasks: root freezes requirements/ADR and shared visibility/identity/DI/
Graph joins; Luna cluster_wave prepares new Core Messaging Queries/Models/
Validation, Orleans Messaging GrainServices/Grains/Contracts/Execution and new
Messaging UnitTests in a private patch while root tests a stable compilation.
Root reviews and integrates; an independent worker creates actual autonomous
RF3 SDK/MCP cases after the runtime is frozen. Root owns Aspire build/unit/
recovery/RF3 and exact-source Linux receipts and stage commits. No worker runs
gates, edits shared composition or changes public/persisted S1 contracts.

TASK-DUE-RF3-A refines AC-DUE-002/003/004 with independent public observations.
Luna cluster_wave owns only new Messaging Cases/Helpers/Assertions/Models/
Contracts files in a private patch; root owns all shared fixtures, service/Graph
registration, gates and integration. Reuse the existing genuine Aspire
ClusterFixture and real SDK/official MCP clients, a unique canonical partition,
persisted resource policies and persisted scheduler identities. Never call
EmitRecurringOccurrences or ExpireSaga, invoke internal grains directly, alter
clock/storage bytes, disable the service or synthesize a successful response.

Use future UTC definitions with a long next interval so each schedule has
exactly one due ordinal during the finite test. Before the due instant, revoke
the protected creator's field-use grant while retaining scheduler/write grants,
and cancel a separate schedule through the real API. A healthy eligible schedule
in that same partition must progress autonomously while the revoked schedule
keeps its exact revision/generation/watermark and the canceled one emits
nothing. Restore persisted creator authority and require exactly one occurrence
with the independently derived full identity and exact NotBefore; repeated
SDK/MCP inspection cannot create a second ordinal. Assert exact projected copy
and complete cross-client value parity, not only nonnull responses.

A separate healthy future Waiting saga with a configured timeout must reach
TimedOut revision2 without an explicit expiry caller, enqueue exactly one
independently named timeout at its stored deadline, and remain unchanged under
later public inspection. Use the real destination consume/ack flow to prove
that no second timeout remains. All native SDK/MCP errors, cancellation and
test cleanup stay observable. This first autonomous public stage does not
qualify actual leader loss, RF3 cold restart or lost-ACK phase injection; those
remain mandatory follow-on cases and may not be closed by these source tests.

TASK-DUE-RF3-B refines AC-DUE-003 for actual leadership and cold restart. Luna
lifecycle_wave owns only new IntegrationTests Messaging files named
`DueFaultRf3*` under Cases/Helpers/Assertions/Models/Contracts, prepared privately.
Reuse RequestCqrsRf3Wave's owned current-image AppHost and the exact verified
current image/profile contract; no old executable, migration, alternative
Docker launch or shared-fixture fault is needed. Seed one future schedule with
a long interval and one future Waiting saga through SDK/official MCP. Capture
authoritative physical node statuses and signed current discovery, identify the
actual leader from those statuses, and stop that owned voter before either due
instant. Prove setup completed before due rather than racing the clock.

Through the two real surviving endpoints, observe a compatible replacement
leader and autonomous one-occurrence/one-timeout outcomes with independent IDs,
exact stored due times, watermark1 and TimedOut revision2. Never issue emit or
expiry commands or call internal coordinators. Restart the same physical voter
through the owned AppHost, require changed signed runtime generation and caught
up state, and verify the same SDK/MCP values without another canonical effect.
Fully settle the wave, then cold start homogeneous current RF3 on those same
owned targets/profile. Use new real client sessions, require changed generation
on all three voters, preserved occurrence/timeout/state and real destination
consume/ACK/no-second-timeout evidence. Retain roots on primary or cleanup
failure; delete success roots only after every owned process and disposal joins.
Root owns all production/shared joins and execution. This source stage does not
qualify lost-ACK interruption, no-quorum fault or performance/endurance gates.

The sole shared B fault join is root-owned
`RequestCqrsRf3Wave.KillLeaderAsync(string, CancellationToken)`, delegating to
the existing scoped ContainerRuntimeControl with the exact scenario
`messaging-due-leader-loss`. Preserve the existing KillAsync follower-loss
scenario and all native resource/ownership checks. Do not record a leader stop
as a follower-loss receipt.

Frontend N/A: scheduling is exposed through existing Batch SDK/MCP operations
and current inspection. New SDK/MCP syntax N/A; existing S1 commands are used.
Rollback stops the coordinator under a capable epoch7 reader and retains all
canonical data. No compatibility fallback, automatic migration, power-loss or
production qualification claim follows from local development evidence.

```mermaid
flowchart LR
  Leader[Native leader and read barrier] --> Scan[Bounded node-local due hints]
  Scan --> Coordinator[Partition coordinator grain]
  Coordinator --> Request[Fresh signed request grain and native CQRS]
  Request --> Apply[Current authorization and logged-time RF3 apply]
  Apply --> Canonical[Atomic watermark or Waiting CAS plus enqueue]
```
