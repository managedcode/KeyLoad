# Memory and performance acceptance

Goal: repair all evidenced memory, performance and excessive-read defects in the
current KeyLoad implementation. Review covers storage, every core operation,
query/search, replication and snapshots, Orleans routing, HTTP/SDK transport and
the qualification/report harness. Completion requires this full inventory closed,
real public correctness and measured resource behavior, not just one optimized path.

Chosen direction: [brainstorm](memory-performance.brainstorm.md). Cross-cutting
resource contract: [ResourceExecution](docs/Features/ResourceExecution.md).
Decision and execution ownership: [ADR-035](docs/ADR/ADR-035-memory-performance.md).

## Scope, actors and boundaries

Developers and authenticated SDK/MCP callers operate the same durable RF3 database.
Node-local storage hosts own committed read cuts, journals, locks and apply gates.
Optimizations must preserve public results, exact ranking, authorization, atomic
outcomes, replay/recovery, signing and cancellation. The owner authorizes this
review and its repairs; independent ManagedCode defects use the owning-repository
repair/release policy. No such dependency defect has yet been evidenced.

All actual reviewed defects and verification prerequisites are in scope, including
existing dirty bounded-read work. Broader product features with no implementation
are recorded as gaps rather than manufactured performance wins. Paid infrastructure,
fake stores, analyzer/test weakening, arbitrary cache consistency changes, claimed
power-loss durability and unrelated checkout changes are outside scope.

Current evidence: strict build/format are failing. The current server source starts
a node-local `PartitionHost`, then the Orleans silo; request grains dispatch to
database grains, and Aspire defines three Docker nodes with separate storage
mounts. Distributed directory and activation repartitioning are configured.
Source wiring does not qualify RF3 behavior: no current integration scenario
forces activation migration and then verifies node-local storage ownership and a
durable caller-visible outcome. The successful existing-main CI run predates this
dirty source. Copies protecting mutable storage arrays remain required unless a
scoped borrowing contract explicitly prevents escape/mutation.

## Criteria and pass/fail conditions

- AC-MP-001 / REQ-MP-001: a located inventory covers each current operation family,
  with cause, correction, owner and test/measurement. Every evidenced issue is
  fixed and reviewed; no unresolved item may silently vanish or count as complete.
- AC-MP-002 / REQ-MP-002: range reads charge/check deadlines, cancellation, records
  and bytes before materializing a whole page; stop on bounds/end keys. Transaction
  overlays merge matching changes only without global staged-change overfetch.
  Views/borrowed values remain inside the read/commit gate; no mutable arrays leak.
- AC-MP-003 / REQ-MP-002: SQL/index/full-scan work charges keys and values, obeys
  one cancellation/deadline, and parses/encodes sort values once per candidate.
  SQL lexing checks the same deadline/cancellation at bounded character
  intervals, including inside one long token; a SQL string already longer than
  the byte cap is rejected before a full length scan. Page selection retains
  only necessary bounded state while preserving filter, order/ties, signed
  cursor, access path, cut and oversized-scan rejection.
  Automated proof: a deterministic operation clock expires midway through an
  unterminated long literal, and the public QueryEngine returns BudgetExceeded
  before the trailing syntax error; an oversized SQL string is rejected before
  tokenization. Existing SQL grammar/error-precedence and query suites stay green.
- AC-MP-004 / REQ-MP-002: exact text/vector/hybrid ranking preserves Unicode,
  repeated terms, visible-corpus statistics, RRF summation/ties and policies while
  removing redundant full-document collections and query-vector validation/norm.
  Selected returned-document projection stays bounded; hidden/stale data still
  consumes work and cannot bypass budgets. Shared hybrid accounting remains exact.
  SQL, AST, live-query and search engines sharing one DatabaseEngine also share
  its positive MaxConcurrentQueries ceiling. Admission is fail-fast before parsing,
  ranking or entering the store gate; cancellation and every error release exactly
  once. Creating another engine cannot multiply that node-local allowance.
- AC-MP-005 / REQ-MP-002: event/sample/graph reads bound complete response bytes,
  cumulative work and cancellation. Time-range scans stop at the end bound; graph
  repeated destinations reuse gate-scoped visibility without revealing hidden
  vertices. Positive, empty, limit-edge and excessive-work cases preserve semantics.
  Unified topic/stream source reads include principal/catalog/head/event work in
  one raw-byte/deadline/cancellation budget, validate the catalog once, preserve
  signed cursor authority and include the complete cursor envelope in output size.
  SourceRecord decodes borrowed values for read and subscription/publication callers;
  it rejects missing or wrongly scoped/positioned persisted records explicitly.
- AC-MP-006 / REQ-MP-002: mutation/change-feed, queue and outbox paths remove
  duplicate reads/serialization while preserving before/after images, quotas,
  ordering, FIFO, leases, checksums, replay, policy and atomic durable outcomes.
  Ready queue receive stops its borrowed-index visit once the requested count of
  deliverable messages is reached while preserving expired-message skipping and
  the 256-entry scan ceiling.
  Canonical JSON validation keeps the same ordinal property order, duplicate-name
  rejection, decimal normalization and UTF-8 output. Fingerprinting preserves raw
  numeric spelling and exact lowercase SHA-256, including null and Unicode, while
  removing complete intermediate byte-array copies. Golden fixtures, real-store
  replay, invalid inputs and warmed allocation-growth cases provide the proof.
- AC-MP-007 / REQ-MP-003: replica log/snapshot storage is reclaimed crash-safely
  after durable publication, preserving committed prefix, current snapshot and
  in-flight transfers. Restart/partial cleanup cannot lose the authoritative cut.
  Entry validation/apply/transport removes redundant full-size serialization and
  byte/string copies without changing payload identity or conflict detection.
- AC-MP-008 / REQ-MP-003: peer transfer admission bounds aggregate concurrent
  memory/disk use and cancellation cleanup, preserving a reserved control plane
  for votes/heartbeats. Snapshot file work leaves ordered apply gates when an exact
  immutable cut is established; concurrency/recovery tests prove it.
- AC-MP-009 / REQ-MP-004: SDK consumes successful HTTP bodies as streams after
  headers, with request/response disposal and cancellation throughout; error-body
  parsing is bounded. Typed results and unknown-write/error mapping remain intact.
  Real server tests exercise delayed/chunked/large bodies, cancellation, malformed
  errors and a following request; no fake HTTP handler or stream is proof.
- AC-MP-010 / REQ-MP-004: supported workload sizes cannot cause report-time sample
  duplication or unbounded report strings. JSON/CSV stream to files, permitted
  sample totals are validated before allocations and exact report schema/results
  survive. Oversized operator profile reads are bounded without changing valid
  generated profiles or configuration trust boundaries.
- AC-MP-011 / REQ-MP-005: CI evidence distinguishes server/node resources from
  load-generator resources; measures logical reads/bytes/scans, managed allocations,
  GC, working set and latency with exact workloads/source SHA. Physical I/O is
  labeled only when genuinely measured. Bounded-memory and reduced-read assertions
  use deterministic operation counters and configured budgets; timing comparisons
  use repeated same-topology baseline/candidate runs, never fabricated speedups.
- AC-MP-012 / REQ-MP-005: development build, format/governance and all required
  TUnit/recovery/Docker-Aspire RF3 SDK/MCP gates pass for the delivered source.
  Coverage/complexity requirements receive real compatible collection/gates before
  a passing claim. Final docs/status/README and requirement/task/test evidence are
  honest, all required worker joins reviewed, unrelated work preserved.
  ADR-041 AC-ROC-001..006 governs the read-only collection/owned-read naming
  prerequisite: explicit CLR migration, exact JSON/hash and ownership/default/SIMD
  regressions, no shim/suppression and combined caller proof.

## Criterion-to-test matrix

| AC | Tests / methodology | Assertions and negative/error flows | Verification |
|---|---|---|---|
| 001 | Operation inventory + independent joined source review | Every family and finding has owner/evidence; missing means open | Located implementation inventory, lead review |
| 002 | Real ZoneTree StorageRecovery TUnit and recovery | Prefix/end/after-key/overlay equivalence; cancellation/byte cap stops before excess copies; no borrowed escape | CI unit/recovery, logical counters |
| 003 | QueryExecution real-store and RF3 SDK | Sorted/filter/cursor equivalence, index bytes, cancellation, tight budgets, single parse/key preparation | CI unit/integration, reads/allocations |
| 004 | Search real-store and RF3 SDK | Exact reference ranks for Unicode/hidden/stale/repeated terms and vector spaces; hybrid shared caps | CI unit/integration and resource profiles |
| 005 | EventStreams/TimeSeries/GraphTraversal real stores | Complete response budget, narrow range work, cycles/repeated/hidden vertices, cancellation | CI unit/RF3 plus work counters |
| 006 | DocumentStorage/Messaging/ChangeFeeds real-store/recovery and ResourceExecution canonical-JSON cases | Before/after, leases/quota/FIFO, checksums/replay, atomic failure; no redundant point read; exact canonical/fingerprint fixtures, real replay and bounded incremental hash buffers | CI unit/recovery/RF3, warmed current-thread allocation artifacts |
| 007 | ClusterReplication real persistence/process kill/RF3 | Current image/tail survives publish/cleanup/reopen, conflicts/large Unicode payloads, no leaked old files | CI recovery/integration, entry allocations |
| 008 | ClusterReplication concurrent real peer/snapshot flows | Data saturation leaves quorum traffic ready; canceled temp state reclaimed; exact concurrent snapshot cut | CI real recovery/RF3 resources |
| 009 | ClientApi TUnit with real Kestrel plus RF3 SDK | Chunked/delayed success, bounded malformed/error body, mid-body cancellation, reuse/disposal | CI TUnit/integration only |
| 010 | BenchmarkComparisons report validation, ClientApi profile boundary | Correct raw JSON/CSV under large allowed settings, oversized rejection before allocation, interrupted output | CI comparison/TUnit; actual artifacts |
| 011 | Instrumented real workload profiles | Scope-labeled counters, allocation/GC/RSS and repeated latency; bounded resource assertions | GitHub JSON/artifacts with SHA/options/topology |
| 012 | Full configured qualification and static gates | No skipped/weakened tests or fabricated result; actual exact-SHA pass and preserved index | CI all suites, format, governance, diff review |

Manual review exceptions: architecture/ownership, full operation inventory and
policy preservation require lead plus independent source review. They supplement,
not replace, runtime/counter proof. Missing MCP/coverage/complexity instrumentation
is an open qualification task. Existing old-main CI does not qualify this checkout.

Migration/rollback: no persisted-format change is planned for streaming/copy
repairs. Any actual log retention/transport/contract change must extend ADR-035
before coding, with ordered migration/recovery/rollback. Owner-directed revert is
safe only where data/serialization contract allows; never restore obsolete data
ownership as a performance shortcut.
