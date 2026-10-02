# Orleans foundation acceptance

AC-REP-006: configured bounded anti-replay admission rejects replay/tampering and reserves critical vote/heartbeat/noop/membership capacity when Forward, ReadBarrier or data-Append pools saturate. Native cryptographic regressions use real Orleans runtime metadata and TimeProvider.System; actual client-load RF3 availability is required in GitHub Actions. Source-only checks do not establish that result.

Source of truth: [ClusterReplication](../Features/ClusterReplication.md), [ClusterRouting](../Features/ClusterRouting.md), [TestInfrastructure](../Features/TestInfrastructure.md), [ADR-036](../ADR/ADR-036-orleans-foundation.md).

- AC-REP-001 and AC-ROUTE-001: no DotNext; each SDK/MCP request has its own Orleans request grain. Prove with source dependency inventory and client-driven Docker RF3 tests.
- AC-REP-002: real process kills after durable vote/append/commit ACK reopen the acknowledged prefix; complete corruption fails closed. Recovery tests invoke CrashHost and real storage.
- AC-REP-003/005 and AC-ROUTE-003: any leader/voter loss preserves authorized acknowledged outcomes; minority reads/writes fail. Real client failover and denial flows, including revocation.
- AC-REP-003 election bounds: the strict-analysis prerequisite replaces insecure jitter with the system cryptographic RNG while retaining inclusive lower and exclusive upper tick bounds. Real RNG range cases cover a one-tick range, adjacent power-of-two widths and the full positive TimeSpan interval; these complement, and do not replace, actual RF3 failover.
- AC-REP-004 ownership cleanup: concurrent/repeated disposal shares one terminal result. Accepted protocol producers and detached peer/checkpoint work drain before synchronization resources or stores are released. An unexpected apply-worker failure must still wake waiters and close resources. Real-store durable-boundary regressions and active Docker shutdown cases are required.
- AC-REP-004: bounded snapshot transfer resumes/rejects interrupted or corrupt input before touching live state; an empty replica catches up documents, topics, projections and receipts. Recovery plus SDK/MCP RF3 rejoin tests.
- AC-REP-004 process-transfer boundaries: an actual process kill after durable transfer intent or acknowledged chunk retains only resumable private input and leaves the canonical cut unchanged; a kill after verification rejection reclaims that owned corrupt image without touching newer live state; a kill after publication reopens the installed cut. The frozen additional observer names are SnapshotTransferBegun, SnapshotChunkAcknowledged, SnapshotRejected and SnapshotPublished, appended without renumbering prior boundaries. Callback injection is complementary to these real process cases.
- AC-ROUTE-002: distributed directory and activation repartitioning enabled; routing migration preserves host identity and outcomes. Real Orleans configuration/migration evidence.
- AC-TEST-001: TUnit compiles/discovers/runs every relevant suite without lost assertions or alternate frameworks. Full CI suites plus migration diff review.
- AC-TEST-002: three independent Docker resources and both official clients are used. Aspire fixture/topology evidence and client cases.
- AC-TEST-003: generated package locks/local data/artifacts stay untracked and central pins remain. Tracked-file and ignore checks.

No acceptance item is satisfied by configuration alone. Baseline CI 36926803549 measured old commit 9c570f8c33a7a9667507a8e1c0ca68860de3be45. Current migration suites and measured coverage/complexity evidence remain pending.
