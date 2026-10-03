# Isolated intensive TimeSeries comparisons

TS009J raw-data boundary: default serialization omits internal ACK and cleanup
facts. Use an explicit bounded JSON writer over the original settled run result.
Encode all50000 planned slots; distinguish NotStarted from observed attempts and
retain actual ACK/native primary/cleanup independently of outcome. A failed slot
may retain an observed count/ACK and stays failed. Native host, source/job receipts,
telemetry, copy proof and publication remain separate root-owned joins.

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

TS009 plan continuation: freeze an additive small executable JSON family contract
and strict internal C# reader/typed6/30 plan before raw/native host integration.
This can be prepared independently of the shared Node repair. Keep the existing
profile constants as guarded runtime parameters; the external family contract
must match every frozen value, never provide an alternate configurable workload.
Reject unknown/duplicate/missing/null/drifted fields and array order/content.
Plan cells carry no measurement or native-qualified status. Temporary complete
source candidates allow root review without placing partial code in the shared
checkout while its other owner prepares a full checkpoint. Root owns JSON,
embedding, integration and all workflow/raw/physical-copy decisions.

Physical-copy alternatives were reviewed separately: routed SDK reads cannot
prove every node's local data, and Restore invalidates original identity/cut.
Stopped original stores can provide genuine ordered oracle reads, but safe
metadata guards, stop/start ownership and seed/final host handshakes must be
approved before that native inspector is implemented. The plan-only continuation
does not claim to resolve or qualify those boundaries.
