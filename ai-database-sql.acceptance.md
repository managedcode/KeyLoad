# AI database: SQL and typed relational rows

Goal: one KeyLoad server exposes its implemented document, table, graph,
text/vector search, event, queue, time-series and file/blob operations through a
central versioned SQL entry point. Owner clarified that "one file" means one
server/database with linked models, not a new live file format.

## Scope and contracts

In scope: optional typed relational schema on canonical Collection resources;
string primary-key column equals the canonical entity ID; mandatory/type/closed
row checks; existing native partition-local unique indexes; final-image PUT/PATCH
validation in the same atomic gate; unchanged document reads, graph/vector links,
outbox and batch atomicity. Repair the initial Orleans RPC transport error
classification discovered in the exact baseline, preserving genuine recovery
errors, cancellation and stable write outcomes. New unified SQL envelope version 1 supports existing
Q1 SELECT plus `CALL exact_public_operation(@arguments)`, where the object
parameter is the exact existing SDK/MCP argument envelope. All currently
catalogued public operations compile to their existing typed operation. This is
explicit SQL procedure syntax, not full PostgreSQL compatibility or general
cross-model SELECT/JOIN syntax. Correct reversed equality access-path selection.

Out of this first delivery stage: arbitrary SQL joins, FK/check/default/cascade
constraints, cross-partition/global snapshots, SQL wire compatibility, fused
graph/vector operators, ANN acceleration, physical-file changes, and new
dependencies. These remain required product stages in ADR-054/055; none is
advertised as implemented. Blob commands retain their existing separate lifecycle
and are not silently folded into document/event/queue mutation batches.

Actors: persisted administrators configure schemas; authorized callers mutate and
read through real .NET/MCP clients; SQL is an adapter and cannot assert roles.
Boundary: versioned SQL request -> existing canonical decoder -> signed Orleans
gateway -> one request actor -> node-local authorized operation/ordered apply.
Schema metadata is additive/null-omitted; changing a configured schema remains an
explicit migration job. Deploy source before opting new resources into schemas;
rollback to an older writer is unsafe for typed resources and must fence them.

## Criteria and test matrix

| Criterion | Pass/fail and required flows | Automated proof / command |
|---|---|---|
| AC-AISQL-001 | Capability coverage matrix and ADRs identify present/absent relational and SQL behavior for every model; no unsupported feature/performance/readiness claims | Governance static inventory plus lead source/doc review; documentation has a review-evidence exception |
| AC-AISQL-002 | Valid new schema persists; unique column names, defined scalar types, required nonnullable string PK, bounded columns, Collection/Document authority only; malformed/default schemas rejected before commit | New real-ZoneTree RelationalStorage TUnit schema cases; GitHub unit suite |
| AC-AISQL-003 | PUT/PATCH final images reject unknown/duplicate/missing/wrong-type fields, invalid PK or PK/id mismatch; int64/decimal are bounded exact numeric types, timestamp explicitly UTC; nullable columns accept absent/null, required reject both | Real-store RelationalStorage positive/negative/edge rows and patch tests; GitHub unit suite |
| AC-AISQL-004 | Native UNIQUE and revision/security semantics hold; failed mixed table/document/event/queue batch leaves no effects; table rows can be read/query/graph/vector entities without a second store | Real-store transactional and linkage regressions; Docker/Aspire RF3 .NET/official MCP schema/row scenarios |
| AC-AISQL-005 | SQL version 1 SELECT preserves Q1 pages and validates its grammar once in the authorized canonical Q1 actor before any scan or effect; CALL resolves exact names and a single object argument envelope, empty argument envelope works for no-body operations; unsupported version, invalid CALL grammar, trailing CALL statement, unknown/recursive target or missing/extra CALL parameters are rejected before gateway | Strict parser/binder unit cases and RF3 SELECT/CALL differential cases; existing Q1 rejection tests retain the single-parser contract |
| AC-AISQL-006 | SQL CALL returns the original operation result and errors, preserves current persisted rights, stable command IDs/retry/fences and one-grain-per-request; no generic UPDATE of internal queue/event state | RF3 SQL/.NET/MCP document/event/queue/search/graph/series/blob equivalence and stable-retry/denial cases |
| AC-AISQL-007 | SQL wrapper is bounded before deserialize, reserves conservative heavy-read DATA lane for HTTP and MCP, respects cancellation and exact output bound; generic MCP hint is not read-only/idempotent and is destructive; direct control routes retain reserved progress | Real admission/parser bounds and official MCP metadata checks; GitHub units/RF3. SQL control CALL can reject under saturated DATA lane: explicit initial-stage limitation, direct control API remains available |
| AC-AISQL-008 | Reversed literal/parameter equality selects same native point/index path and exact ordered rows as ordinary equality; protected use, residual contradictory/null predicates and shared read budgets remain | First-authored QueryExecution real-store access-path/authorization/budget TUnit cases; GitHub unit suite and scalar-disabled unit pass |
| AC-AISQL-009 | Exact delivered SHA receives complete solution build/format/analyzer/governance, TUnit units, process recovery and real Docker RF3 SDK/MCP qualification; failures tracked individually and artifacts retained | ci.yml verify/analyzer-rules/docker-rf3 jobs, exact run/job/SHA/artifacts |
| AC-AISQL-010 | Performance priority remains explicit: native indexed access, one final-image schema check, no whole-dataset copies/new engine; comparative measurements retain latency/throughput/allocation/memory/contention/backlog and topology/guarantees | Existing GitHub comparison profiles plus future dedicated table/CALL comparisons. No new numerical speed claim until comparable raw exact-SHA evidence; coverage collector remains an explicit incomplete mandatory gate |
| AC-AISQL-011 | Initial native Orleans RPC/timeout failures return OwnershipLost for reads and UnknownWriteOutcome for possibly dispatched writes, using only server-derived intent and fixed safe details. Preserve typed KeyLoadException errors (including genuine RecoveryRequired), caller cancellation, stable IDs and exactly one dispatch with no retry | Real native Orleans exception unit classification cases, existing genuine RF3 stopped-replica catch-up/replay, SDK/MCP regressions; source review verifies both command gateway and authentication read callers |
| AC-AISQL-012 | All ten typed blob SDK methods preserve source call syntax, native routes/results/IDs and one transport; null client/request are rejected before any HTTP effect. Feature-owned extension class keeps aggregate type and nesting limits enforced without exceptions/suppression | New pure public null-argument TUnit cases, existing genuine RF3 blob SQL/SDK/MCP lifecycle/range/retry cases; exact-SHA analyzers/formatter |

TUnit/Microsoft.Testing.Platform only. No local test/recovery/load execution,
mocks, stubs or service doubles. Unit cases use real ZoneTree; integration uses
existing RF3 Docker/Aspire fixture and real .NET/official MCP clients. New tests
precede implementations; execution and baseline happen in GitHub Actions. Source
coverage thresholds remain mandatory and unqualified while collector is absent.

Schema-name clarification before source join: Q1 metadata names `revision`, `*` and names starting `@` are reserved; `id` is allowed only as the primary key. AC-AISQL-002/003 includes explicit reserved-name rejection and valid id-primary SQL parity tests. PATCH uses raw typed SET scalar preflight before one final-image check; byte/depth caps apply before parsing.

Pre-delivery public enum spelling is WholeNumber for signed Int64 and FixedPoint
for exact decimal, with unchanged ordinal/range/scale contracts. The initial
candidate's CA1720 failures are tracked in the plan and repaired without suppression.
