# ADR-143: Independent Orleans silo and physical replica topology

Status: owner-selected 2026-10-17; implementation under local verification; Linux qualification pending.

## Decision

The owner requires independent silo and replica counts, including 1, 2, 3 and 5.
The initial RF3 default remains three replica-bearing silos; it is no longer a
restriction on every ordinary deployment. `KeyLoad:SiloCount` selects native
Orleans participants. `KeyLoad:ReplicaCount` selects the initial physical
partition's fixed voting group, independently and no larger than the silo count.
Both are positive bounded startup options. Benchmark and functional qualification
selectors retain their existing explicit topology inventory.

The first replica-count resources own node-local ZoneTree, journals, locks
and ordered commit/apply. Remaining resources use the same server image in an
explicit coordination-only role: native membership, distributed directory and
activation repartitioning, without database grain implementations, storage
services, SDK endpoints or MCP endpoints. Native heterogeneous grain placement
keeps database activations on their capable silos. Coordination readiness checks
that its local silo is Active and the selected total membership is present.
No alternate membership provider or replication implementation is introduced.

Replica voting and durable acknowledgement use the original majority rule.
Even groups are allowed: RF2 requires both voters and provides no write
availability during a voter outage; RF1 has no replica fault tolerance.
Extra coordination silos cannot vote or acknowledge database writes. They are
cluster infrastructure participants, not additional database copies or public
operation servers. This stage does not deliver stateless database execution on
storage-free silos or multiple independently placed physical groups.

Ordinary local AppHost runs publish the canonical server project using the native
.NET SDK PublishContainer target, owned as an Aspire executable prerequisite for
all dependent containers, when no pinned server image is selected. Product server
and comparison images use centrally pinned Directory.Build.props without a
Dockerfile or root .dockerignore. Specialized fault/coverage instrumentation
images retain their separate qualified build context. Explicit image, benchmark, protocol and
coverage modes preserve their original identities and admission gates.

## Boundaries and verification

Topology selection is startup-only. This does not implement joint consensus,
live group resizing, automatic data movement or a persisted-data migration.
Public HTTP/MCP JSON, signed authorization, Orleans aliases/Ids and atomic
partition identity are unchanged. Process recovery, actual SDK/MCP operations,
heterogeneous membership, availability under faults and joined shutdown need
real Aspire execution. A build or model test cannot qualify these guarantees.
Required exact-source Linux, recovery, RF3 and endurance gates remain open until
their original evidence exists. No local startup implies production readiness.
