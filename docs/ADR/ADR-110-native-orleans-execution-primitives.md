# ADR-110: native Orleans execution and scheduling primitives

Status: Accepted for per-method selection and audit; runtime/provider adoption
pending. Date: 2026-10-05. Integration owner: KeyLoad lead.
Requirements and acceptance: REQ/AC-ORL-001..013 in
[ClusterRouting ExecutionPrimitives](../Features/ClusterRouting/ExecutionPrimitives.md).

## Context and decision

The owner asks KeyLoad to use Orleans' StatelessWorker, OneWay, durable jobs and
reentrancy/interleaving facilities where their guarantees fit. The current unique
request/read grains already allow independent requests; partition commands and
due coordination carry stronger ordered-effect and reliable-outcome contracts.
Increasing grain concurrency without auditing awaits, identity, storage lifetime
and admission can violate those contracts while multiplying retained work.

The owner's follow-up explicitly includes local Orleans services and a broad
documentation review, including messaging/pub-sub and transactions. Extend the
selection inventory before choosing new providers or contracts; preserve the
existing native replica/due GrainServices and silo-local storage/lifecycle owners.
The canonical reviewed capability inventory and proposed priorities are in
[CapabilityReview](../Features/ClusterRouting/CapabilityReview.md).

Adopt a per-grain/method selection contract rather than enabling attributes
globally. The owning feature contains the canonical source audit, candidate
matrix, bounds, REQ/AC, planned tests and execution joins. Its decisions preserve
ADR-036's separate request grain, ADR-082's native CQRS lifetime/identity, ADR-003's
RF3 receipt and ADR-094's canonical recurring/saga due state.

Use bounded StatelessWorker pools for proven interchangeable computation;
loss-tolerant OneWay for advisory hints with an independent recovery path; narrow
AlwaysInterleave/MayInterleave control when waits prevent useful progress; and
ReadOnly only for an actually pure compatible read method. Whole-grain Reentrant
requires all-method invariant review and measured justification. None of these
attributes grants CPU parallelism within one Orleans turn or moves file ownership.

Evaluate native Durable Jobs for one-time persistent wake-ups with idempotent
canonical effects. The repository pins Orleans 10.4.0, whose matching Durable
Jobs package remains `10.4.0-alpha.1`. The owner implementation approval now selects the bounded RF3-backed native
journal provider frozen in [RuntimeJournal](../Features/ClusterRouting/RuntimeJournal.md),
including private bootstrap, owner/content fencing, quotas, and already-due saga
handoff. Runtime qualification remains pending. Canonical ZoneTree schedule/saga
state and creator-authorized effects remain authoritative.

Retain native GrainServices for per-silo/partitioned runtime support, borrowed
silo-local DI owners and precise silo/activation lifecycle hooks. New loops
require explicit ownership, readiness/admission and joined shutdown; registration
alone never establishes a cluster singleton. Evaluate native Streams as a bounded
post-commit delivery path with canonical outbox/catch-up, and native transactions
through a dedicated ZoneTree/RF3 transactional storage/visibility contract.
Provider selection, distributed transaction guarantees and new runtime settings
remain separate implementation joins; the review does not enable them.

## Alternatives and consequences

Blanket Reentrant/AlwaysInterleave would admit turns beside mutable operations
and weaken the existing partition/due serialization. ReadOnly on the generic
read dispatcher would include heterogeneous backup/admin lifecycle work. Marking
request/read grains StatelessWorker would multiply identities/pools and change
the unique signed request boundary without a demonstrated benefit. Those options
are rejected for the inspected methods.

Converting command/due RPCs to OneWay loses callee errors and terminal receipts.
Using a timer as persistent work loses the schedule on activation failure;
reminders persist recurring definitions but do not retain every missed tick.
Neither supplies the selected one-time at-least-once job semantics. Existing
canonical due sweeps remain the recovery authority until a real native adapter
passes its provider/fault gates.

The selected approach can improve independent computation and responsive
operation control while retaining ordered effects. It adds explicit resource and
fault tests and may add native prerelease/provider maintenance. Performance,
durability and production readiness remain unproven by this decision.

```mermaid
flowchart TD
    Method[Actual grain method and invariant] --> Audit[Identity state awaits effects completion]
    Audit --> Pure[Interchangeable bounded computation]
    Pure --> Workers[StatelessWorker pool]
    Audit --> Loss[Recoverable advisory signal]
    Loss --> OneWay[OneWay hint]
    Audit --> Wait[Bounded operation control during awaits]
    Wait --> Selective[Audited selective interleaving]
    Audit --> Future[Persistent one-time invocation]
    Future --> Provider[Durable Jobs provider and idempotency gate]
    Audit --> Ordered[Ordered effect or durable receipt]
    Ordered --> Serial[Serial reliable canonical path]
```

Owner implementation approval, 2026-10-06: [RuntimeAdoption](../Features/ClusterRouting/RuntimeAdoption.md)
freezes the first concrete native-service and telemetry stages, REQ/AC-ORL-011/012. Reuse the
coalesced node-local post-apply notification with a bounded clock fallback;
retain known-target awaited due RPC. Root owns shared contracts/gates and Luna
owns the disjoint runtime/test files; native provider/transaction stages remain
subject to their exact storage and trust joins.

## Implementation contract

1. TASK-ORL-AUDIT joins disjoint read-only native API and current grain reviews;
   root owns shared contracts, configuration, docs and integration. The reviews
   make no source/dependency/test changes.
2. TASK-ORL-CONTRACT records the owner rule first, freezes the feature's method
   matrix, maps each REQ to AC/test/evidence and adds architecture/catalog/status
   navigation. Static governance, changed-link review and Mermaid render prove
   this documentation stage only; AC-ORL-001 has an explicit review exception.
   TASK-ORL-CAPABILITY-REVIEW joins disjoint read-only state/time, messaging and
   runtime/lifecycle reviews against the complete official documentation families.
   Root owns the integrated inventory and REQ/AC-ORL-007..010; research alone
   neither registers a provider nor grants a new database consistency guarantee.
3. TASK-ORL-WORKERS, CONTROL and HINTS start only when the owning feature's
   concrete operation, quotas, exact methods and immutable/lifetime/trust
   contracts are resolved. Writes are disjoint feature-local role folders in
   `src/KeyLoad.Orleans/Features/ClusterRouting/` and the actual affected slice;
   tests mirror those slices. Root alone joins generated contracts and call graph.
4. TASK-ORL-JOBS resolves the native API and persistent provider/reconciliation
   gate first. Messaging owns `Features/Messaging/Grains/`, `Contracts/` and
   `Execution/`, matching Core canonical due owners and real process/RF3 tests.
   Server/AppHost composition and central package pins have one root owner.
   No added provider or experimental opt-in bypasses required architecture review.
5. TASK-ORL-VERIFY runs the candidate's meaningful operation tests, final canonical
   Release build/format/analyzers, Aspire unit/scalar/recovery/RF3 suites and
   required coverage/fault gates. Actual SDK/MCP caller evidence and comparable
   Linux GitHub measurements precede capability or acceleration claims.

The feature specification is the canonical task graph, dependency/start/join,
flow/test/bound and rollout source. No candidate task is complete merely because
an attribute compiles. Failed or incomplete proof does not unblock its dependants.
New public control APIs, provider formats or dependency defects need the owning
contract and, for ManagedCode defects, its mandatory published repair/release.

Rollout/rollback retain original request aliases, persisted authorization,
ZoneTree state, ordered apply and RF3 recovery journals. Stop admission and join
live work before disabling a candidate. Reconcile persistent job delivery against
canonical generations before provider removal; this documentation stage changes
no persisted format or data.

## Native sources checked against Orleans 10.4.0

- [Stateless worker grains](https://learn.microsoft.com/en-us/dotnet/orleans/grains/stateless-worker-grains): interchangeable activations and per-identity/per-silo limits.
- [Request scheduling](https://learn.microsoft.com/en-us/dotnet/orleans/grains/request-scheduling): non-reentrancy, selective interleaving and turns across awaits.
- [Concurrency attributes v10.4.0](https://github.com/dotnet/orleans/blob/v10.4.0/src/Orleans.Core.Abstractions/Concurrency/GrainAttributeConcurrency.cs): interface/class attribute placement and the native IInvokable MayInterleave predicate.
- [One-way requests](https://learn.microsoft.com/en-us/dotnet/orleans/grains/oneway): absence of callee completion/error acknowledgement.
- [Durable Jobs v10.4.0 README](https://github.com/dotnet/orleans/blob/v10.4.0/src/Orleans.DurableJobs/README.md): native one-time at-least-once manager/handler and provider requirements.
- [Durable Jobs v10.4.0 public API](https://github.com/dotnet/orleans/blob/v10.4.0/src/api/Orleans.DurableJobs/Orleans.DurableJobs.cs): native manager, handler, run context, registration and bounds.
- [Orleans v10.4.0 release](https://github.com/dotnet/orleans/releases/tag/v10.4.0): matching prerelease Durable Jobs status.

Native journal implementation stage, 2026-10-06: REQ/AC-ORL-013 in
[RuntimeJournal](../Features/ClusterRouting/RuntimeJournal.md) owns the exact Core,
Orleans and Server joins, ordered worker tasks, rollout/rollback, private identity
and real recovery/RF3 acceptance. Scoped ORLEANSEXP005 is owner authorized. The
native manager starts background work without awaiting its catalog at silo start;
provider async calls wait on a separately canceled readiness gate until signed
RF3 bootstrap and verification complete. Both physical stores must already satisfy
the current required reader contract before first journal admission; fresh stores
are created with that contract and unsupported capabilities fail closed.

The REQ/AC-ORL-013 call graph copies every existing default-deny edge and admits
the native RuntimeJournalClient's exact ReadCoreAsync and SendCommandAsync calls
to IRequestGrain.ExecuteStreamAsync. Native JournaledStateManager suppresses
ExecutionContext for its background work loop, so provider callbacks establish
their immediate caller identity with the supported RunWithCurrentCallerAsync
API around the complete native CQRS stream drain and restore it on every exit.
The coordinator's concrete ExecuteJobAsync edge remains scoped to its actual
saga effect; remove the unused manager wildcard and speculative concrete
ProcessDueAsync provider allowance. TASK-ORL-JOURNAL-GRAPH-014 freezes exact
Orleans client/Server registration ownership, ordered guarded private preparation,
root join and verification in the RuntimeJournal contract. Preserve signed
protected identity, fresh separately authorized request grains, backpressure and
RF3 authority. Actual native scheduling/journal callbacks, duplicate dispatch,
single saga/message effect, caller restoration, denied method/target and healthy
follow-up controls run through Aspire after build/format. No coordinator wildcard
or alternate database path is admitted; complete process, RF3 and Linux
qualification remains required.

Native streamed-request telemetry join, 2026-10-06: TASK-ORL-TELEMETRY-STREAM-002
in [RuntimeAdoption](../Features/ClusterRouting/RuntimeAdoption.md) freezes the
REQ/AC-ORL-012 client propagation and exact native stream-start classification
contract before code. Stage1 is a private Luna patch for ServiceDefaults
ClusterRouting Contracts/OrleansTelemetryPolicy.cs,
Diagnostics/OrleansTelemetryTagSanitizer.cs and the actual RequestCqrsFixture
client registration. Stage2 is root guard/source review and native format/build;
stage3 runs the unchanged real signed-operation, parentage, metrics and privacy
workflow through Aspire normal/scalar before Linux/RF3 qualification. No pins,
provider replacement, synthetic telemetry, raw metadata or bound changes apply.
Rollback and source-inventory constraints are in the owning feature contract.
The original228/230 local result leaves the label and later assertions open;
source reasoning is not runtime proof and this ADR is not fully implemented.
