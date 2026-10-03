# Isolated intensive TimeSeries comparisons

The owner requires meaningful Linux1/2/3-node comparisons with one database and
scenario per independent runner. ADR-056 freezes270 document/specialized cells;
TimeSeries remains an explicitly open part of the same BenchmarkComparisons
slice. This continuation must not relabel the existing48-sample regression as
intensive performance, put an in-memory library into a native-node matrix, or
combine databases in one worker.

Use a separate30-cell family: KeyLoad and TimescaleDB, each at1/2/3 actual
members, for Append, RawRangeRead, Latest, Aggregate and Windows. Six independent
topology preflights precede measurements. The existing public SDK/MCP contracts
and PostgreSQL physical streaming replication provide the boundaries; production
KeyLoad remains RF3. Common input and operation plans, correctness oracles,
acknowledgement facts, actual membership, source/image/provider identity and
byte-preserved complete evidence are required before publishing any value.

The chosen profile uses4096 seed samples,10000 operations per repetition,
256 warmups, five repetitions and16 clients. Deterministic UTC ties and
out-of-order seed timestamps exercise the read contracts. Concurrent appended
samples use unique increasing timestamps; their commit sequence is observed,
not invented from submission order. Seed chunks of256 samples remain outside
timing and below native command budgets. SQL on Timescale must implement real
latest/full aggregate/dense-window queries; current time_bucket SUM is incomplete.

Timescale's1/2/3-node topology is one primary with zero/one/two physical standbys.
All nodes use the existing digest-pinned PG18 image. Synchronous commit requires
one standby at node count2 and ANY1 of two at count3. Verify native roles,
streaming membership, acknowledgement settings, replay and seeded/readback
copies. This topology has no automatic failover claim. Existing PG18 bootstrap
can be shared after making only the selected primary name explicit; do not
duplicate replication machinery or assume an Alpine su-exec entrypoint.

Risks: genuine image readiness/replication needs GitHub proof; sequence/ties,
half-open aggregates and clamped dense windows differ from naive SQL; native
output/memory/disk must remain bounded; ManagedCode library version belongs to
its published owning repair; strict family evidence must not silently weaken the
270-cell collector or make the website accept an incomplete TimeSeries family.

Alternative of adding TimeSeries to the frozen main scenario enum would change
the canonical270 cohort and old schema3 meanings. A distinct closed family is
chosen. Preserve the old48-sample semantic/library/foreign-schema regressions as
untimed qualification, with actual public error calls in the new path. Native
coverage is a separate CodeQuality stage and cannot instrument measured cells.

Next: freeze typed operation/workload/report/provider and task ownership in
acceptance and ADR-059 before delegated implementation. All runtime tests and
qualification occur in GitHub; no local containers, load or test runs.
