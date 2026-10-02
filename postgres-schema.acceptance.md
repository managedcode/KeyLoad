# PostgreSQL comparison schema acceptance

Goal: a target cleans up only its own transient schema, and setup/cleanup/graph
commands satisfy enabled SQL analysis without interpolated client CommandText.
REQ-BC-021 / ADR-045. Actors: comparison runner and CI fixture; entry points remain
public PostgresTarget constructor, InitializeAsync, OpenSessionAsync and DisposeAsync.
Existing database-owner credentials remain external; no new public API or IVT.

In scope: typed run identity, private target-owner marker, atomic setup and guarded
cleanup, constant SQL, real pinned PG18/pgvector regression checks in the existing
Aspire comparison test. Out of scope: replication redesign, durability claims,
measured query changes, driver replacements, other adapters, new test projects,
local resources/tests/benchmarks, packages, skill/tool installs and suppressions.

Accepted implementation contract:
- Parse the Guid once; retain exact bench_ + lowercase Guid N schema naming and
  SearchPath behavior. Generate one independent owner Guid per target instance.
- Use a deterministic 64-bit namespace lock key from the first eight bytes of the
  run Guid written in big-endian order, read as a big-endian signed Int64. A key
  collision may serialize unrelated runs but cannot grant schema ownership.
- All client CommandText is named constant SQL. Native Guid, Int32 and Int64
  parameters supply transaction-local pg_catalog.set_config context. Constant
  server DO blocks derive identifiers from typed Guid and format identifiers with
  %I, owner comment with %L, and typmod %s only from checked 2..1024 integer.
- Acquire pg_catalog.pg_advisory_xact_lock using the bound lock key before schema
  creation/ownership lookup. Setup and schema COMMENT form one short explicit
  transaction. Replication ALTER SYSTEM remains outside this transaction.
- Keep extension, four workload tables, index names, C collation, keys, JSONB,
  vector(dimensions), ready/default/lease fields, event revision CHECK and existing
  seed/request order exactly. Owner marker is a visible lifecycle nonce, not a
  secret or database authorization policy. Add no measured table or operation.
- Cleanup uses the same namespace lock, matches pg_catalog.obj_description for
  pg_namespace against this target's owner Guid, and drops only the matching schema.
  A missing schema or foreign/missing marker is a no-op. Do not rely solely on a
  successful-create Boolean: an uncertain commit response can leave owned state.
  A commit-attempt routing hint may be set immediately before CommitAsync after
  schema and marker are staged. Before that point transaction disposal rolls back
  and closes the source without reacquiring a still-blocked namespace lock; after
  that point cleanup must inspect the durable marker even if commit reports failure.
  The hint never authorizes deletion. Always dispose the owned source in finally,
  including cleanup failures. Cleanup failure propagation may mask the original
  setup failure; no exception contract redesign is part of this scoped repair.
- Select existing graph SQL via direct constant assignments in branches; retain
  exactly the recursive query text, parameters, row order and limits.
- Cancellation propagates through transactions/commands; rollback leaves no partial
  schema/context. Keep temporary context local to the transaction and default pool
  reset; no session-level settings or mutable global cache. Preserve SDK null guards.

Acceptance criteria and testing methodology:

| ID | Pass / fail and positive, negative, edge, error flows | Automated proof |
|---|---|---|
| AC-PG-001 | Ordinary setup retains all table/index/column/default/collation/key shapes and exact vector typmod; public read/vector/graph/event/queue/document flows work; boundary dimensions2/1024 work | New internal real-PG helper called from existing TUnit Aspire test, native metadata assertions plus public sessions; existing full comparisons prove all measured operations |
| AC-PG-002 | Sequential and concurrent duplicate run-ID setup yields exactly one owner; losing target fails with PostgreSQL duplicate-schema SQLSTATE; its Dispose cannot delete winner data; winner cleanup removes its schema | Independent two-target public-flow regression with real PG, queries bound by schema name, no doubles |
| AC-PG-003 | Setup SQL failure after CREATE SCHEMA rolls back the entire schema; subsequent cleanup preserves unrelated schema; independent subsequent run succeeds | Test-owned isolated database on the same real PG resource with pgvector installed in a non-public schema causing the expected vector-type lookup error inside setup; always close pools before dropping only test-owned database |
| AC-PG-004 | Cancellation during actual namespace-lock wait rolls back without schema or marker; no temporary context survives a committed/aborted transaction or pooled reuse; subsequent run succeeds | Real Npgsql transaction holds documented namespace advisory lock; query pg_locks/pg_stat_activity until the actual target waits, then cancel linked token; bounded real clock/poll and native context queries, no timing-only guess |
| AC-PG-005 | Default disposal before initialize performs no network; successful/failed initialization closes all target-owned sources; schema missing and externally altered marker do not permit destructive cleanup | Existing no-init public disposal fixture and real marker-mismatch/schema-missing cleanup checks; review source finally; test-owned data cleanup never prunes Docker/global state |
| AC-PG-006 | Enabled CA2100 has no finding in changed production code, numeric/style/XML/other imported rules remain enabled; report/measurement/topology wire data preserved | Ordinary enabled real Host and solution Release build, formatter, governance; exact-SHA GitHub analyzer/full comparison/functional suites and raw artifacts |

The one existing ComparisonTests AppHost supplies benchmark-postgres connection
through installed Aspire13.6 GetConnectionStringAsync(string,CancellationToken)
after the runner finishes successfully. Keep RF3 and every existing external
resource. Pass the linked eight-minute TUnit token. Helpers stay internal in
tests/KeyLoad.ComparisonTests/Features/BenchmarkComparisons and never publish
connection strings, parameters or owner nonces. Test-owned databases/targets are
disposed before the existing AppHost teardown. Checks run only in canonical GitHub
Actions; no second full graph startup, changed performance timers or coverage job.

Exceptions requiring explicit review/manual evidence: arbitrary transport loss at
the exact COMMIT-response boundary cannot be deterministically injected through the
current public fixture without new infrastructure. Source review must prove cleanup
queries the durable owner marker regardless of the create response, and a real
fault-run remains an open qualification limit; do not call it executed. Container
pool internals are not exposed; prove transaction-local reset through real reused
connections and source closure through public failure/disposal, retain raw CI logs.

Migration/rollback: benchmark schemas are transient per run; no product data or
public JSON migration. New targets conservatively leave unmarked old-run schemas
alone; they must never claim/drop a foreign schema. Rollback is a source revert
requiring owner direction before reviving unsafe cleanup or weakening enabled gates.
No production readiness or power-loss claim. Numeric coverage remains required
separate unfinished work in quality-gates.acceptance.md.
