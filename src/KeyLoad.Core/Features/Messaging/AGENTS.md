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
