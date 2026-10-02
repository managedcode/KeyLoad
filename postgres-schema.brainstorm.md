# PostgreSQL comparison schema ownership and constant SQL

Task framing: the real strict comparison-library build reports three CA2100
findings for interpolated setup/cleanup SQL and a graph SQL selection. The run
identifier is already parsed as a Guid and dimensions validated; no injection
exploit is established. The concrete lifecycle defect is unconditional cleanup
after failed CREATE SCHEMA: a second target with the same run ID can drop the
first target's schema. This stage belongs to BenchmarkComparisons, not a driver
dependency repair. Preserve exact workload tables, typmod, native commands,
durability/topology receipts, sampling boundaries and measured operations.

Options:
- A simple successful-create Boolean prevents collision deletion but leaks a
  successfully committed schema when the client loses the commit response.
- A private schema owner marker plus transaction-scoped advisory lock makes
  cleanup conditional on the creating target, including uncertain responses.
  Constant SQL with bound native Guid/integer parameters supplies temporary
  transaction-local PostgreSQL settings; a constant server DO block constructs
  identifiers through pg_catalog.format %I and a checked integer typmod.
- A SQL suppression or generic trusted-string wrapper conceals the diagnostic
  without proving lifecycle ownership and is rejected.

Recommended direction: parse the run Guid once in a private owned schema identity;
generate one private owner Guid per target; create and comment the exact schema
atomically, and drop only when the persisted marker matches under the same
transaction-scoped namespace lock. Use native parameters and constant client
CommandText. Keep ALTER SYSTEM replication setup outside the short DDL transaction.
No extra measured table/query, generic SQL trust bypass or configurable exemption.

Risks and open questions: verify PostgreSQL 18 comment lookup and identifier
formatting, transaction-local setting cleanup after rollback/cancellation, collision
serialization, and resources after a failed initialization. A comment is lifecycle
metadata, not an authorization boundary against a database owner who can alter it.
Test through the actual existing pinned pgvector Docker resource and public target.
Prefer sharing the already running real RF3/external-engine Aspire graph after
measurement, so no extra full cluster startup or benchmark instrumentation occurs.
Do not remove/replace RF3 resources or run local qualification.

Initial task graph, read-only discovery only before accepted contracts:

| Task | Owner/model | Permissions and exact responsibility | Dependencies / completion / join |
|---|---|---|---|
| TASK-MP-010AC-R | economical capable research worker | read-only existing ComparisonTests/AppHost and installed Aspire/Npgsql metadata; resolve the actual connection-string API and bounded real-fixture integration point | this framing; final source/API evidence and proposed exact files; no code, docs, config, build, test, install or local resources; lead reviews before implementation contract |
| TASK-MP-010AC-L | high-capability lead | PostgreSQL primary-source verification, acceptance/ADR/task graph; later shared call site and integrated builds/CI | research joins before write-capable test/production delegation; exact real GitHub evidence required |

Research can run independently of the analyzer source repairs. Subsequent test and
production workers receive disjoint ownership only after AC/ADR are explicit and
approved. Preserve the current dirty checkout and other active chat owners.
