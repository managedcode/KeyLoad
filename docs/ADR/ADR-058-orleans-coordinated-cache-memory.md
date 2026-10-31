# ADR-058: Orleans-coordinated disposable cache memory

Status: Accepted; local cache source is partially implemented, while Orleans cache control, complete qualification and measured benefit remain open.
Related: REQ-CACHE-001..007 / AC-CACHE-001..011 and REQ-MP-002/005 / AC-MP-002/011/012 in [ResourceExecution](../Features/ResourceExecution.md); ADR-035, ADR-041, ADR-081, ADR-113.

## Decision and boundaries

A node-local, bounded RAM pool may accelerate eligible reads from canonical ZoneTree state. ZoneTree and its WAL and atomic journals remain authoritative. Cache content is disposable across restart and activation migration. Orleans coordinates policy through ordinary bounded grain calls; storage handles and authority never move with an activation. Cache admission never replaces persisted authorization, a committed read cut, a required-copy acknowledgement barrier or an acknowledged write.

The local provider cache starts opt-in and cold. A shared node-owned reservation pool accounts for retained bytes and entries across opted-in cache families. Keys and values are privately owned; exact-key mutation invalidation and snapshot-generation fencing occur under the existing storage gate. Pins, in-flight fills, index reservation, eviction and release remain bounded and charged until actual ownership ends. Pressure or stale/uncertain coordination bypasses acceleration and uses the original path.

```mermaid
flowchart LR
    Request[Authorized connection grain] --> Barrier[Fresh authorization and read barrier]
    Barrier --> Store[Node-local ZoneTree storage gate]
    Store --> Cache[Disposable coherent cache]
    Store --> Tree[Authoritative ZoneTree and journals]
    Coordinator[Orleans grain-call policy] --> Cache
```

## Acceptance and source of truth

The ResourceExecution feature is the normative criterion matrix and source map:

- AC-CACHE-001/002 bound shared reservation accounting and lifetime.
- AC-CACHE-003..006 cover exact-key coherence, store-generation/recovery fencing, owned results, pin/fill cleanup, bounded eviction and lock order.
- AC-CACHE-007 covers bounded Orleans policy coordination through ordinary grain calls and the local permit/binding; there is no custom wire, peer-authentication or owner-lease layer.
- AC-CACHE-008 preserves current authorization, revocation and tenant/field policy.
- AC-CACHE-009 requires private low-cardinality diagnostics and repeated matched cache-on/off measurements.
- AC-CACHE-010 requires complete delivered-source build, analyzers, governance, unit/scalar/recovery/cluster and collected coverage.
- AC-CACHE-011 binds the exact local provider under this decision.

The acceptance source specifies the exact option bounds and provider lifecycle. Policy coordination uses ordinary generated Orleans grain-call contracts. This ADR does not authorize a custom wire format, public cache administration API, data migration, cache authority, or a performance/RSS claim.

## Ownership and verification

Storage.ZoneTree owns provider integration and the existing StoreGate/cache path. Core owns the bounded reservation and permit primitives. Orleans owns the grain integration. ResourceExecution TUnit tests use real ZoneTree files, callbacks and finite joined tasks; no fake provider, test-only production hook or reflection substitutes for those cases.

Ordered implementation: complete current provider source and normal/scalar regressions; review and join the Orleans grain integration; run actual replicated-cluster authorization, revocation, activation migration, restart, recovery and healthy-follow-up flows; collect cache-on/off resource measurements on the exact delivered Linux source. Until those gates pass, keep replicated-cluster caches disabled and report numeric benefits as unavailable. Rollback disables acceleration after readers/fills drain; data and journals remain untouched.
