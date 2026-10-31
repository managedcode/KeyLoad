# Memory and read-work requirements

This page lists the memory and read-work paths that must stay bounded and cheap. The
complete scope is [ResourceExecution](../Features/ResourceExecution.md), with the
decision and acceptance criteria in [ADR-035](../ADR/ADR-035-memory-performance.md)
(REQ-MP-001..006, AC-MP-001..012). It states what each path has to do. It records
no measured improvement, and no entry here is closed by a build or a review.

A repair counts only when an operation test on a real store shows the same outcome,
the same read cut and the same errors, and the bounded work is asserted (examined
entries, copied bytes, allocations). A speed or memory claim additionally needs
matched Linux GitHub measurements for the same workload, topology and durability.
Owned storage copies stay wherever a caller may keep or change an array.

## Requirements by area

| Area (owning slice) | Required result |
|---|---|
| Storage reads (StorageRecovery) | Charge borrowed point and range work before copying; merge staged and baseline data in order; bound the examined entries |
| Read budget (ResourceExecution) | No whole-result byte arrays; reject an excess read before an owned copy is made; count bytes through a counting stream |
| Queries (QueryExecution) | Charge index bytes plus referenced document bytes; never materialize full raw or eligible pages; parsing and lowering observe the operation budget, deadline and cancellation ([ADR-130](../ADR/ADR-130-microsoft-scriptdom-tsql.md)) |
| Ordering (QueryExecution) | Prepare JSON paths and order keys once and keep only the requested ordered prefix |
| Live queries and cancellation (QueryExecution, ClientApi) | Propagate cancellation through SQL, AST and live work and through HTTP callers; keep cursor and read-cut authority |
| Search (Search) | Bound text and vector branch ranking and hybrid fusion where a semantics-preserving bounded design exists (no full score array or union map); validate and prepare the query vector once and compute only the selected metric with the same accumulation order and exact ranks |
| Admission (QueryExecution) | Bound simultaneous SQL, AST, live and search work through one engine-owned, atomic, fail-fast allowance |
| Events (EventStreams) | Bound head, range and projected output and the page metadata before retaining excess events; borrowed source records share one authority and head cut with an operation-wide budget and complete result-envelope counting |
| Time series (TimeSeries) | Seek the inclusive UTC range end, never read or decode later samples, bound the projected output |
| Graph traversal (GraphTraversal) | Reuse visibility decisions inside one read cut including hidden vertices, count every examined edge, never expand a hidden vertex |
| Atomic mutations (DocumentStorage) | One prior-image lookup and a directly built final image instead of repeated reads and decodes; an exact-capacity immutable receipt builder |
| Messaging (Messaging) | Reuse counters, stored body lengths and validated leases; a receive never copies a provider page larger than the requested message count; duplicate-publication checks avoid decoding solely for the sequence. FIFO, expired-item skipping, lease, quota and atomic failure are unchanged |
| Projection outbox (ChangeFeeds) | Carry the observed raw length instead of serializing again to size a batch; stop a purge at the requested cut; prepare partition identity once. Contiguous progress, checksum, corruption, rollback and retention authority are unchanged |
| JSON (ResourceExecution) | Stream exact canonical JSON into the fingerprint hash, share pooled input serialization and avoid validation clones; keep a bounded configured UTF-8 pool and reuse cached-receive requests; replacement rules, strict errors and fingerprints stay exact |
| Checkpoints (StorageRecovery) | Remove redundant full snapshot passes only after the file identity, lifetime and recovery cut are established; checksum, format and replay guarantees are unchanged |
| Client transport (ClientApi) | Stream successful JSON after the headers and cap the size of problem bodies |
| Benchmark harness (BenchmarkComparisons) | Bound aggregate corpus bytes, targets, attempts and input before allocation; write reports progressively; keep operation-sized samples bounded |
| Resource measurement (BenchmarkComparisons) | Measure database-node CPU, allocations, GC and working set in addition to the load generator, and label working set honestly; export logical ZoneTree counters and label physical I/O only when it is measured |
| Public operations (ClientApi) | Exercise every affected flow through the official MCP client and the .NET SDK on the Aspire-owned replicated topology |

## Qualification boundary

Integration closes each row with the exact source, run, job, test and resource
artifact, then updates the acceptance criteria, the feature documents and the ADR.

The native MVCC store configures mutable BTree leaves to 32 records through
the public ZoneTree factory. Point lookups copy a mutable leaf in the pinned
engine, so the smaller leaf reduces that copied workspace. This changes neither
key ordering nor snapshot visibility; the canonical journal still flushes before
acknowledgement. Local Debug SDK diagnostics and allocation captures are
development evidence only. Exact-source Linux comparisons and full cluster
qualification remain open.

Coverage, complexity, endurance and fault gates stay mandatory. Process-kill tests
do not support a power-loss claim, and nothing here is a production-readiness or
performance-leadership claim.
