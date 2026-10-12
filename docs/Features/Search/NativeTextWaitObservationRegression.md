# Native text wait observation regressions

Future source proposal only; depends on the complete typed FTS capture/publish packet and Query27R4. Existing NativeTextWaitForIndexTests cases remain unchanged.

REQ-FTS-WAIT-OBS-001 / AC-FTS-WAIT-OBS-001: the actual ReplicaAppliedPositionWaitFixture, native applied entry, ZoneTree.FullTextSearch projection and SAME optional observer yield exact local owner/read generation/physical position, independently matched original request digest, current principal/schema/policy and full applied token. Applied horizon remains distinct from physical storage position. Callback reads the genuine complete canonical store only after original native owner view and provider lease release; complete image and literal Ukrainian/English ranked pages remain unchanged.

REQ-FTS-WAIT-OBS-002 / AC-FTS-WAIT-OBS-002: original wrong-incarnation/future-minimum refusals and current persisted protected-field PermissionDenied happen BEFORE observation. Compare complete original image and position after every failure; then actual healthy wait invokes exactly one typed observation and preserves native token and literal page parity. Persisted grants are genuine ConfigurePrincipal operations, not trusted caller roles. All original provider/fixture cleanup is joined under primary-plus-cleanup aggregation.

Maps existing REQ-SEARCH-WAIT-001/003 and AC-ZT-003. New focused native primitive cases are not election, process-recovery, Aspire RF3 or public SDK/MCP proof; those gates and compiler/Linux execution remain OPEN.

```mermaid
sequenceDiagram
    participant Test
    participant NativeWait
    participant CanonicalView
    participant Observer
    Test->>NativeWait: original authorized wait
    NativeWait->>CanonicalView: current policy + native applied horizon + projection
    CanonicalView-->>NativeWait: complete result after lease release
    NativeWait->>Observer: exact typed captured metadata after view exit
    Observer->>CanonicalView: genuine complete readonly image
    NativeWait-->>Test: original complete result
```
