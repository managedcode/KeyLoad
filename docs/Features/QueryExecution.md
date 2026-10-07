# QueryExecution

TASK-QUERY-FACTORY-WHOLE-OPERATION replaces the two shape-only
`KeyLoadQueryFactoryTests` cases under REQ/AC-ROC-005 and the existing bounded
query execution requirements. AC-QUERY-FACTORY-001 requires the public
`KeyLoadQuery.From<T>` builder, including predicate/projection/order/limit, to
execute against the actual TestDatabase/ZoneTree QueryEngine and return the
independently expected rows, canonical references, revisions and scoped read
cut. AC-QUERY-FACTORY-002 retains null partition/collection/expression rejection,
then requires an unchanged native store position and a successful query against
the same seeded database. Initial AST/getter assertions alone cannot qualify
these cases or contribute functional coverage. Use the current builder and
executor APIs, persisted operation state and existing bounded test options;
no duplicated translator, fake store or new product behavior is permitted.

ADR-041 owns the existing factory contract and ADR-033/117 own functional
test/coverage qualification; no new public or persisted contract is introduced.
Canonical ownership is UnitTests `Features/ClientApi/Cases/KeyLoadQueryFactoryTests.cs`
and cohesive same-slice helpers if required. Backend/client contracts remain
unchanged; frontend, new transport and RF3 fixture changes are N/A to this
unit-operation gap. Root freezes, reviews, joins and runs native owning
normal/scalar tests; a Luna worker prepares a private guarded source packet.
Source integration and exact-source Linux qualification remain pending. All
broader query/ClientApi gates stay mandatory.

## Event and queue SQL sources in the 104-task completion

[ADR-072](../ADR/ADR-072-authorized-sql-model-views.md) freezes the following
concrete implementation stage for KL-095 and SQL/AST/SDK equivalence. Existing
query/full-SQL requirements remain mandatory. Implementation precedes broad
stabilization; tests are authored with code and run after coherent self-review.

| Requirement | Acceptance / positive, negative, edge and error | Automated mapping |
|---|---|---|
| REQ-SQLVIEW-001: SQL source syntax lowers to one normalized typed AST | AC-SQLVIEW-001: EVENTS/QUEUE_MESSAGES literals, aliases, existing predicates/order/LIMIT/EXPLAIN and JSON AST return equal rows/errors; quoted source-like collection names stay collections; wrong arguments, source enum/generation, extra statements and model UPDATE reject before execution | SqlModelViewParserTests, SqlModelViewAstTests, real SDK/MCP RF3 |
| REQ-SQLVIEW-002: views are pure authorized reads in one cut | AC-SQLVIEW-002: current Query+EventsRead/QueueInspect are required, dead letters require their grant, SELECT leaves queue states/claims/stream head/store position unchanged, stale generation and corrupt identity/history fail closed | SqlModelViewReadTests, SqlModelViewAuthorityTests |
| REQ-SQLVIEW-003: protected input/output and delivery authority never leak | AC-SQLVIEW-003: payload/header field use is denied before row scans, unprivileged star/nested/alias output omits protected canaries, full grants expose expected values, no SQL row or Explain includes lease tokens/owner/fingerprint; revocation immediately denies the next request | SqlModelViewAuthorityTests plus actual persisted-policy RF3 SDK/MCP |
| REQ-SQLVIEW-004: every model operator has bounded work and output | AC-SQLVIEW-004: AllowFullScan is explicit; configured scan/raw/result/depth/token limits and cancellation/deadline reject whole requests, cursor is explicitly unsupported, empty/terminal rows and exact metadata bounds hold; a following authorized read works | SqlModelViewBudgetTests, real ZoneTree and bounded TUnit cancellation |
| REQ-SQLVIEW-005: deployment preserves canonical RF3 execution | AC-SQLVIEW-005: each SQL/AST/SDK/MCP request uses existing request/read actors and persisted rights; real Aspire RF3 append/enqueue/read and follower restart preserve rows and leave queue delivery state unchanged | root-owned SqlModelViewRf3 parity/authority/validation tests, exact-source CI; source review for unchanged transport/native fields |

Canonical slice map and task graph are in ADR-072. Root owns Abstractions and
transport/integration/docs/status; one Luna/high worker owns the specified
Query/Core read helpers and matching new tests. Search, movement, native sessions,
JOIN/DML and all foreign benchmark changes remain separate stages of the same
parent plan. Source-stage completion does not close KL-095 until its full mapped
acceptance and qualification pass. UI N/A; no dependency or persisted migration.

REQ-SQLC-006 / AC-SQLC-006A / TASK-SQLC-BETWEEN adds the bounded typed
[SqlBetween stage](QueryExecution/SqlBetween.md) under Accepted ADR065. Existing
scalar/null/eager-error semantics, expanded AST budgets, field authority and
cursors remain canonical. First-authored real ZoneTree and genuine RF3 SDK/MCP
tests qualify the new syntax at source7d1196 with44 original execution rows;
the receipt (report removed from repository) retains
full exact-source CI. Coverage, full SQL/native protocol and measured performance
remain unfinished.

REQ-SQLC-003 / AC-SQLC-003P adds independent full public/native value and
same-instance native writer tests under the Accepted
[ADR-065 oracle stage](../ADR/ADR-065-full-sql-client-compatibility.md).
Existing SQL-to-canonical byte parity, original IDs/kinds/input and disposal
ownership remain mandatory. Actual366 normal CI has47passed/1error among48
selected SQL/backup cases; the invalid trailing byte oracle requires correction
without hiding a serializer defect. Full source/scalar/RF3 remain unqualified.
Canonical test paths are UnitTests QueryExecution SQL compiler/comment tests and
ClientApi typed corpus/new McpNativePayload cases; production/frontend/data
changes are N/A because this stage corrects evidence only.

SQL exists because KeyLoad is one composable database for AI agents: documents,
typed tables, graphs, blobs, queues, events, vectors/search and time series can
reference and use one another. [DatabaseComposition](DatabaseComposition.md) /
[ADR-067](../ADR/ADR-067-composable-agent-database.md) specify queue -> linked
entities -> knowledge graph and graph -> queued actions in one authorized bounded
request. The first server-derived stage uses composing mutations through existing
canonical SQL CALL; full declarative model sources/JOIN/DML remain required under
ADR-065. One shared language must not stop at independent calls per model.

Owner clarification2026-10-03 makes full SQL syntax and a client connection
protocol required. [ADR-065](../ADR/ADR-065-full-sql-client-compatibility.md) owns
the ordered implementation contract; [conformance inventory](../implementation/sql-client-conformance.json)
distinguishes syntax, executed semantics and native interoperability. Current
Q1/SELECT/CALL is a stage; full SQL and native protocol remain pending.

| Requirement | Acceptance | Task/test/evidence |
|---|---|---|
| REQ-SQLC-001/006: full versioned typed SQL | AC-SQLC-001/006 | TASK-SQLC-R7/FULL; [183-command index](../implementation/sql-client-commands-postgresql18.json), explicit semantic stages and real differential fixtures pending |
| REQ-SQLC-002/003/004: bounded comments preserve canonical results/authority | AC-SQLC-002–004 | TASK-SQLC-I1/Q1/S1/I2; first-authored shared/parser/compiler real-store and RF3 SDK/official MCP comment cases; source/qualification pending |
| REQ-SQLC-005: conservative SQL transport outcomes | AC-SQLC-005 | TASK-SQLC-C1; exact commented/unknown-root write outcomes over genuine Kestrel, pending |
| REQ-SQLC-009/010: honest native performance and search choice | AC-SQLC-009/010 | TASK-SQLC-R4/FULL; pinned ZoneTree.FullTextSearch review then genuine correctness/fault/isolated workloads, pending |

The lexical stage uses one internal allocation-free scalar trivia reader and
existing configured byte/token/depth/cancellation budgets; quote readers retain
their exact contents. No second statement, DTO/data format, package or alternate
authority. Backend Query/Server/Abstractions, Client and matching tests use the
same QueryExecution slice; frontend N/A because callers use APIs. Root owns shared
contracts/docs/Git and actual final evidence; disjoint worker scopes and rollback
are fixed by ADR-065. TUnit/MTP runs only in GitHub; source checks are not passes.

Owner direction2026-10-02 requires SQL as the central language for one linked AI
database. [ADR-054](../ADR/ADR-054-central-sql.md) adds a versioned unified SQL
operation envelope: existing Q1 SELECT plus CALL into the sole canonical public
operation catalog. [RelationalStorage](RelationalStorage.md)/ADR-055 supplies typed
rows using those same document query/index paths. This is the first invocation
stage; declarative model sources, arbitrary JOIN and FK remain required pending
product stages. Exact source/qualification coverage is [central SQL evidence](../implementation/central-sql.md).

| Requirement | Acceptance | Task/test/evidence |
|---|---|---|
| REQ-AISQL-001: one server supports linked model operations with SQL central | AC-AISQL-001/005/006 | TASK-AISQL-004/006/008, strict compiler and real SDK/official MCP RF3 differential cases |
| REQ-AISQL-003: versioned single-statement SQL preserves canonical authority and resources | AC-AISQL-005–007/011 | TASK-AISQL-006/008/011, rejection/ID/retry/permission/cancellation/admission/metadata and initial-RPC native failure cases |
| REQ-AISQL-004: native indexed work and honest comparable performance | AC-AISQL-008–010 | TASK-AISQL-007/008, reversed equality real-store regressions and exact GitHub gates |

TASK-DBHP-012/AC-DBHP-012 adds a bounded genuine SDK observation to the existing
AC-AISQL-007 RF3 admission flow. The node and verified-scope control counts must
both reach exactly zero within five seconds after completed direct HTTP/MCP
control operations. Fifty-millisecond polls preserve caller cancellation and
every SDK failure. The native MCP server retains admission until its endpoint
drains, so client receipt alone is not the zero-count observation boundary.
[ADR-054](../ADR/ADR-054-central-sql.md) owns the unchanged lifetime contract and
ordered test-only repair. Original Tests37113337508 at9e053 RF3 is62/63; new
exact-source CI qualification is required before claiming this repair passes.

Status: Accepted repair contract; qualification pending. Requirements map to
[ResourceExecution](ResourceExecution.md), AC-MP-003/012 and
[ADR-035](../ADR/ADR-035-memory-performance.md).

| Requirement | Acceptance | Automated evidence |
|---|---|---|
| REQ-QUERY-001: one cancellation/deadline/raw key-value budget covers SQL parsing plus point, index and full-scan candidates | AC-MP-003 | Real-store SQL/AST index-plus-point accounting, full-scan rejection before page copies, cancellation during a long SQL token, and pre-tokenization rejection above the byte cap |
| REQ-QUERY-002: prepare JSON paths/order keys once and retain only the prefix needed for the requested page | AC-MP-003 | Real-store mixed scalar ordering, ties, filters, page concatenation and large-value allocation cases |
| REQ-QUERY-003: preserve authorization, redaction, explain, cursor cut/epoch and complete response limits | AC-MP-003/012 | Existing QueryAdapter/Security/LiveQuery cases plus new exact metadata/output and negative cases |
| REQ-QUERY-008: SQL/AST/live and Search share node-local analytical admission even across engine instances | AC-MP-003/004/011/012 | TASK-MP-006C real ZoneTree saturation, cancellation/error release and independent-database cases in Features/ResourceExecution |

Owning paths: Query/Features/QueryExecution/ helpers and matching UnitTests feature
tests. Existing QueryEngine, QueryAst evaluator and LiveQueries files are ADR-032
migration debt; repairs must not add layer folders. Abstractions query contracts
and Core shared JSON/storage budgets belong to the lead. Server cancellation
forwarding is serialized through the lead; no database schema or SQL/AST wire
format change. UI: N/A, typed caller operations are the entry point.

The preserving quality join is specified by AC-CQ-011/012 in
[quality-gates acceptance](CodeQuality.md) and ADR-033.
QueryEngine keeps the public SQL/AST/live facade; its private live owner is
`Features/ChangeFeeds/LiveQueryExecutor.cs`. Actual query suites cover the owning
operation results, authorization, cancellation and budget boundaries.
The numeric-enabled Query build is clean; exact-SHA test qualification is pending.

AC-CQ-014 accepts the preserving migration of the three legacy adapter/live/security
query test files to this canonical feature test slice. All original method/assertion
coverage remains, with internal cohesive classes, invariant inputs and explicit
deterministic200-document/50-query and100-mutation/12-identity flow assertions.
REQ-QUERY-003..006 map to this additional test-source acceptance and TASK-MP-010UQ;
the detailed matrix, exact worker ownership and required GitHub proof are in
the [CodeQuality](CodeQuality.md) acceptance and execution contract.
Existing SqlParserContract/QueryResource cases retain their error-precedence,
cancellation and budget boundary assertions where paired with whole operations.
ADR-033/032 suffice: only test ownership/input selection changes, no production
or public/data contract. Canonical paths/evidence are updated after the source join.

TASK-QUERY-BYTE-ASSERT preserves AC-QUERY-003/005 in QueryAdapterTestSupport,
LiveQueryTests and the RF3 AuthorizedQueryScenario. Canonical UTF8 arrays are
compared with explicit ordered SequenceEqual, rather than array identity or an
unordered byte comparison. The reported RF3 failure is run36988949282; UnitTests
were blocked by governance in that run. New exact-SHA UnitTests/RF3 evidence is
required before declaring the correction qualified. ADR: N/A, the canonical
query/replay contract and all production boundaries are unchanged.

```mermaid
flowchart LR
    Request[Bound SQL or AST request] --> Cut[Authorized consistent storage cut]
    Cut --> Source[Budgeted point index or full scan]
    Source --> Prepared[One parse and prepared scalar order keys]
    Prepared --> Selection[Bounded page prefix selection]
    Selection --> Page[Exact ordered projected page and signed cursor]
    Page --> Limit[Complete protocol byte limit]
```

TASK-MP-006 owns QueryEngine/QueryAst/LiveQueries and new QueryExecution helpers
and tests only; it cannot change parser/validation, public DTOs, core, server,
Search or central settings. New optional caller cancellation and an operation
clock do not alter the host clock. Tests use actual ZoneTree and real persisted
policies, without service doubles. Release build precedes GitHub TUnit/recovery/
RF3 checks. No performance or numeric-coverage result is inferred from source.

## Повний query contract

Актори: .NET/SQL/JSON caller і authorized reader. Entry points: [QueryEngine](../../src/KeyLoad.Query/Features/QueryExecution/Queries/QueryEngine.cs), [SqlParser](../../src/KeyLoad.Query/Features/QueryExecution/Execution/SqlParser.cs), [public AST](../../src/KeyLoad.Abstractions/Features/QueryExecution/Contracts/QueryAst.cs), [SDK query builder](../../src/KeyLoad.Client/Features/QueryExecution/Queries/KeyLoadQuery.cs), [HTTP query operations](../../src/KeyLoad.Server/ApiEndpoints.cs). Current Q1 — bounded read-only scalar dialect, а не PostgreSQL wire/full SQL compatibility.

| Вимога | Acceptance / flows | Test mapping |
|---|---|---|
| REQ-QUERY-004: versioned capability/grammar не виконують unsupported expressions | AC-QUERY-004: supported Q1/AST проходить validation; unknown version/operator, non-scalar parameter, malformed AST або unsupported CLR expression дає explicit rejection до scan чи execution application code | Existing `AstRejectsUnknownVersionOperatorsNonScalarParametersAndBudgetsBeforeScanning`, `MalformedPolymorphicAstIsRejectedByTheProtocolDeserializer`, `UnsupportedExpressionsNeverInvokeApplicationGettersOrDelegates` у [QueryAdapterTests](../../tests/KeyLoad.UnitTests/QueryAdapterTests.cs) |
| REQ-QUERY-005: SQL/JSON/C# є adapters одного authorized AST і typed scalar semantics | AC-QUERY-005: equivalent requests повертають той самий order/access path/PII omissions та interchangeable cursor; null/missing/IN/parameters визначені; builder branches не ділять mutable query context | Existing `SqlJsonAndCSharpAdaptersHaveTheSameSeededResultsAndAccessPath`, `CursorContinuesAcrossEquivalentSqlJsonAndCSharpForms`, `NullMissingAndInHaveExplicitCanonicalSemantics`, `BuilderBranchesHaveIndependentQueryContexts` у QueryAdapterTests |
| REQ-QUERY-006: planner/Explain і continuation зберігають cut, scope та policy authority | AC-QUERY-006: point/index/full-scan paths obey однакову authorization; tampered/wrong-scope/stale cursor відхилено; зміна source/policy invalidates відповідний cursor; protocol metadata входить у result bound | Existing `InvalidCursorIsRejectedBeforeACollectionScanUsesItsBudget`, `CatalogAndOtherCollectionWritesPreserveTheCursorButSourceWritesInvalidateIt`, `QueryResultByteBudgetIncludesIdentityAndRedactionMetadata`; [SecurityAndQueryTests](../../tests/KeyLoad.UnitTests/SecurityAndQueryTests.cs) revocation cases |
| REQ-QUERY-007: extended/distributed operators оголошуються тільки після versioned semantics і qualification | AC-QUERY-007: PLANNED capability fixtures та real RF3 tests доводять declared partition cuts/failure/cursor/budget semantics; unsupported joins/graph/search extensions залишаються explicit unsupported, без client-side silent evaluation | PLANNED suites для KL-037/055/095 та distributed/extended AST ADR contracts |

Q1 source-present: typed adapter validation, authorization, deterministic scalar order, bounded current-cut query/cursor та capability manifest. Global serializable snapshot, arbitrary joins/user delegates, cross-shard execution і всі proposed graph/event/search extensions не оголошені реалізованими. [Search](Search.md) і [ChangeFeeds](ChangeFeeds.md) мають власні behavior contracts; live scalar query використовує той самий safe query boundary.

Рішення: [ADR-004](../ADR/ADR-004-committed-read-views.md), [ADR-010](../ADR/ADR-010-query-budgets-security.md), [ADR-012 dialect](../ADR/ADR-012-sql-dialect.md), [ADR-013 AST](../ADR/ADR-013-authorized-query-ast.md), [ADR-020 contexts](../ADR/ADR-020-independent-query-contexts.md), [ADR-022 policy epoch](../ADR/ADR-022-policy-epoch-revocation.md). Один integration owner володіє shared AST/codec/security/cursor contracts; parser/planner/executor work scopes відділені після freeze. Qualification тільки exact GitHub TUnit/recovery/RF3 SDK/MCP; test source не є passing outcome.

## Unit test traceability crosswalk

These candidate references do not certify an AC or a native run. The R111 input was a failed, normal-only census and is used only for historical case identities.

| Existing case family | Candidate REQ/AC traceability | Functional contributor boundary |
|---|---|---|
| `PartitionQuery*` cases | REQ/AC-PQUERY-001..005 as listed per method in the current contributor registry and private per-case map | The 25 unique unit methods already in the registry are explicit candidates; contract-only assertions count only when paired in the same method with a real query outcome/state. REQ/AC-PQUERY-006 remains the separate RF3 gate. |
| `LiveQueryTests` | REQ/AC-FEED-003; cursor/policy subflows additionally REQ-QUERY-003 / AC-MP-003, AC-MP-012 where asserted | Snapshot/tail, replay, or persisted policy outcomes are actual operation candidates. Does not close all ChangeFeeds behavior. |
| `EqualityAccessPath*` | REQ-AISQL-004 / AC-AISQL-008; budget/security subflows also REQ-QUERY-001/003 / AC-MP-003 | Real indexed query outcomes are candidates; they do not establish comparative performance or full SQL. |
| `SqlBetween*` | REQ-SQLC-006 / AC-SQLC-006A; store, authority, boundary, and recovery methods use the exact subcriteria listed per case | Real ZoneTree SQL/AST results, null/missing query outputs and state-preserving retry are candidates. Parser/lowering-only grammar checks and parser rejection helpers remain contract evidence, not standalone operation contributors. Field authorization and cancellation cases are candidates only where the method asserts the healthy admin/retry result. |
| `SqlGraphPath*`, `SqlGraphSearch*` | REQ/AC-GRAPH-007..010 and REQ/AC-GSEARCH-002..006, only the per-case criteria actually asserted | Direct-vs-SQL real-store comparisons, authenticated policy outcomes, bounded positive-label/terminator flows, and reject-then-healthy-retry methods are candidates. Parser-only, standalone typed-rejection without state oracle, and capability-descriptor methods are not product coverage contributors. |
| Query adapter/resource/cursor tests | REQ-QUERY-001..006 and REQ-QUERY-008; the per-case map selects AC-MP-003/012 or AC-QUERY-004..006 only where the method asserts them | Real data, budget, cursor, cancellation, or store-preservation flows are candidates; parser/constructor guards alone are not. |
| SQL comment/parser/budget tests | REQ/AC-SQLC-002..004, REQ-AISQL-003 / AC-AISQL-007, and REQ-QUERY-004 / AC-QUERY-004 where applicable | Parser shape and byte-boundary checks are traceable but not sufficient contributor flows by themselves; query execution/result assertions are required. |

Existing comments on `NativeQueryCursorTests` (`AC-IS-001`) and parser cases (`AC-ROC-006`) do not match their current owning criteria and should not be copied into the crosswalk.

TASK-OWNER-REJECTED-TRIVIAL-PRUNE removes the standalone null-constructor case
under the owner's whole-flow test rule. It never executes a query or establishes
an owning acceptance criterion. Preserve all actual query operation cases and
production guards. Root owns the source deletion, full build and normal/scalar
operation regressions; regenerate the native coverage census after removal.
ADR: N/A, no product behavior, public/data contract or architecture changes.


TASK-REL-004-INNER-JOIN-001..006 extends REQ-QUERY-007 only with the exact bounded
Q2/AST2 Text primary-key INNER equijoin in [ADR-118](../ADR/ADR-118-bounded-relational-inner-join.md).
AC-QUERY-007-JOIN-001/002 and AC-REL-004-JOIN-001..006 map to explicit language/AST
admission, declared schemas/aliases, both persisted resource and row grants,
join/order field-use, safe projection, one committed read-cut, deterministic
ordinal order and cumulative existing work/byte/result/deadline limits. The
linked ADR owns exact public optional metadata, version inventories, source
roles, error/reopen/concurrency and real Aspire RF3 SDK/official-MCP flows. Q1 and
AST1 operations stay current. Arbitrary/distributed joins, FK semantics, full SQL
and native SQL client protocol remain required and open. Source/runtime evidence
for this first operator is pending; no plan task closes from its contract alone.
