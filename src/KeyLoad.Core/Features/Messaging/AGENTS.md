# Core Messaging feature

## Purpose and entry points
- Owns canonical queue, event, recurring schedule, saga, transfer and subscription behavior in the KeyLoad.Core assembly.
- Due-work discovery entry point: `Queries/DueWorkDiscovery.cs`; record interpretation stays in feature-local query helpers.

## Boundaries
- Discovery is read-only and uses the existing native ZoneTree records and prefixes. Do not add a durable index, new storage owner, public SDK operation, or alternate queue writer here.
- Return only bounded owned metadata. Never expose retained schedule payload/header JSON, saga state, or a borrowed store view.
- Apply remains in the existing atomic `DatabaseEngine` command path; one operation preserves the full atomic partition and current persisted authorization.

## Verification and risks
- Author focused TUnit cases with the existing real ZoneTree Messaging fixture. Execute only through the Aspire `unit` suite; the solution integrator owns build and runtime gates.
- Fixed-tail sweep bounds prevent later keys from extending a page. A cursor is disposable and must reset when store incarnation or read generation changes; it is not a snapshot across pages.
- Preserve the native range byte ceiling, admitted-value cap, cancellation, deadline and safe corrupt-record reporting.

## Owner-approved KL092 deadline extension
- TASK-KL092-QUEUE-DEADLINE-NATIVE-001 adds one authorized direct Batch mutation through existing ordered DatabaseEngine apply. This explicit owner-approved additive public operation supersedes only the earlier prohibition on a new SDK operation for this exact mutation. Discovery remains read-only with no durable index or alternate writer.


## Finite native Accept capacity coordination, TASK-KL094-ACCEPT-CAPACITY-ATTEMPT-002
- ADR-088/094/125 owns the optional signed failure witness and separately authorized source CAS. Discovery remains read-only; it cannot advance attempts or grant authority. Every effect remains the original native signed Batch on the stable ConnectionGrain call-local owner, with fresh persisted subject permissions, original cancellation/deadline and joined CQRS tasks. No per-operation activation, fallback principal, new dispatcher or DurableJobs retry authority.
- Nullable source ceiling/history and target original failure stamp are charged under actual native count/byte/serialization limits. Unknown or unrelated dependency changes do not advance; known source failure requires explicit existing operator repair. This finite capacity-only boundary does not close universal AC-XFER-005.
