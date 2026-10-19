# ADR-071: Canonical ZoneTree storage and full-text providers

Status: Accepted owner decision; complete integration and qualification pending.
Related: REQ/AC-ZT-001..003 in
[Search](../Features/Search.md) and [BenchmarkComparisons](../Features/BenchmarkComparisons.md).

## Decision

Use [ZoneTree](https://github.com/ZoneTree/ZoneTree) as the canonical storage for
all KeyLoad models and
[ZoneTree.FullTextSearch](https://github.com/ZoneTree/ZoneTree.FullTextSearch) as
the full-text derived-index provider. Package/source/license identities
are centrally pinned. byte serializers and synchronous WAL remain below
the ordered atomic and replication journals. Vector/ANN algorithm choices retain
their own correctness, resource and performance contracts.

Each silo-local ZoneTree owner (an Orleans Grain Service) owns every handle, journal, lock and apply gate of its node ([ADR-136](ADR-136-orleans-partition-ownership.md)).
Orleans connection and partition grains route authorized operations to that owner;
activation movement never moves its open storage handles. Canonical records and
persisted authorization remain authoritative. Full-text generations are disposable,
bounded derived state validated against an authorized committed read cut.

## Implementation contract

1. Provider identities, REQ/AC, slice/file owners, API/license/source review and caller-visible semantics are frozen before integration.
2. Canonical model owners use the actual ZoneTree APIs and retain complete write/retry/read/backup/reopen/snapshot/recovery flows. Comparison databases use their own stores and remain independent from KeyLoad provider selection.
3. Search freezes exact scoring/tokenization, protected-field exclusion, projection generation/cut, bounded rebuild/replay, cancellation, publication, restart and corruption contracts under [ADR-078](ADR-078-full-text-projection.md).
4. Provider and test contributors implement only the frozen Search/storage responsibilities. Central packages, host composition, SDK/MCP discovery and docs are joined once; dependency defects follow their owning repository's release policy.
5. Review every join, then execute the enabled full build/format/governance, real TUnit operations, process recovery and Aspire cluster tests with actual SDK and official MCP clients. Qualify exact Linux source and functional coverage. Comparable original GitHub measurements are required for any acceleration claim.

Frontend N/A for internal provider ownership. Caller capability documentation must
state delivered semantics and qualification separately. No current-format, signing,
authorization, acknowledgement, resource-bound or topology gate is waived.

## Rollout and rollback

Construct a bounded full-text generation from committed authorized records,
validate its complete cut and exact independent ranking oracle, then publish the
verified generation. A failed build leaves the previous verified current generation
or exact fallback search usable under the same authority; it never publishes partial
results. Rollback drains derived work and restores one coherent source/generation
checkpoint. Canonical ZoneTree data and acknowledged journals remain intact.

```mermaid
flowchart LR
    Owner[Silo-local ZoneTree and journals] --> Records[Canonical committed records]
    Records --> Projection[Bounded full text generation]
    Policy[Persisted authorization and scoped read cut] --> Projection
    Projection --> Verify[Exact rank privacy and recovery oracle]
    Verify --> Publish[Verified derived generation]
    Publish --> Proof[Aspire and exact source Linux qualification]
```
