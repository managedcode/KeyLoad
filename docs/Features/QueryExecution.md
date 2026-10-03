# QueryExecution

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
[quality-gates acceptance](../../quality-gates.acceptance.md) and ADR-033.
QueryEngine keeps the public SQL/AST/live facade; its private live owner is
`Features/ChangeFeeds/LiveQueryExecutor.cs`. First-authored
`Features/QueryExecution/QueryConstructionTests.cs` asserts the explicit invalid
null-database constructor boundary; real existing query suites cover valid input.
The numeric-enabled Query build is clean; exact-SHA test qualification is pending.

AC-CQ-014 accepts the preserving migration of the three legacy adapter/live/security
query test files to this canonical feature test slice. All original method/assertion
coverage remains, with internal cohesive classes, invariant inputs and explicit
deterministic200-document/50-query and100-mutation/12-identity flow assertions.
REQ-QUERY-003..006 map to this additional test-source acceptance and TASK-MP-010UQ;
the detailed matrix, exact worker ownership and required GitHub proof are in
quality-gates.acceptance.md and quality-gates.plan.md at the repository root.
Existing QueryConstruction/SqlParserContract/QueryResource cases retain their
constructor, error-precedence, cancellation and budget boundary assertions.
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

Актори: .NET/SQL/JSON caller і authorized reader. Entry points: [QueryEngine](../../src/KeyLoad.Query/QueryEngine.cs), [SqlParser](../../src/KeyLoad.Query/SqlParser.cs), [public AST](../../src/KeyLoad.Abstractions/QueryAst.cs), [SDK query builder](../../src/KeyLoad.Client/KeyLoadQuery.cs), [HTTP query operations](../../src/KeyLoad.Server/ApiEndpoints.cs). Current Q1 — bounded read-only scalar dialect, а не PostgreSQL wire/full SQL compatibility.

| Вимога | Acceptance / flows | Test mapping |
|---|---|---|
| REQ-QUERY-004: versioned capability/grammar не виконують unsupported expressions | AC-QUERY-004: supported Q1/AST проходить validation; unknown version/operator, non-scalar parameter, malformed AST або unsupported CLR expression дає explicit rejection до scan чи execution application code | Existing `AstRejectsUnknownVersionOperatorsNonScalarParametersAndBudgetsBeforeScanning`, `MalformedPolymorphicAstIsRejectedByTheProtocolDeserializer`, `UnsupportedExpressionsNeverInvokeApplicationGettersOrDelegates` у [QueryAdapterTests](../../tests/KeyLoad.UnitTests/QueryAdapterTests.cs) |
| REQ-QUERY-005: SQL/JSON/C# є adapters одного authorized AST і typed scalar semantics | AC-QUERY-005: equivalent requests повертають той самий order/access path/PII omissions та interchangeable cursor; null/missing/IN/parameters визначені; builder branches не ділять mutable query context | Existing `SqlJsonAndCSharpAdaptersHaveTheSameSeededResultsAndAccessPath`, `CursorContinuesAcrossEquivalentSqlJsonAndCSharpForms`, `NullMissingAndInHaveExplicitCanonicalSemantics`, `BuilderBranchesHaveIndependentQueryContexts` у QueryAdapterTests |
| REQ-QUERY-006: planner/Explain і continuation зберігають cut, scope та policy authority | AC-QUERY-006: point/index/full-scan paths obey однакову authorization; tampered/wrong-scope/stale cursor відхилено; зміна source/policy invalidates відповідний cursor; protocol metadata входить у result bound | Existing `InvalidCursorIsRejectedBeforeACollectionScanUsesItsBudget`, `CatalogAndOtherCollectionWritesPreserveTheCursorButSourceWritesInvalidateIt`, `QueryResultByteBudgetIncludesIdentityAndRedactionMetadata`; [SecurityAndQueryTests](../../tests/KeyLoad.UnitTests/SecurityAndQueryTests.cs) revocation cases |
| REQ-QUERY-007: extended/distributed operators оголошуються тільки після versioned semantics і qualification | AC-QUERY-007: PLANNED capability fixtures та real RF3 tests доводять declared partition cuts/failure/cursor/budget semantics; unsupported joins/graph/search extensions залишаються explicit unsupported, без client-side silent evaluation | PLANNED suites для KL-037/055/095 та distributed/extended AST ADR contracts |

Q1 source-present: typed adapter validation, authorization, deterministic scalar order, bounded current-cut query/cursor та capability manifest. Global serializable snapshot, arbitrary joins/user delegates, cross-shard execution і всі proposed graph/event/search extensions не оголошені реалізованими. [Search](Search.md) і [ChangeFeeds](ChangeFeeds.md) мають власні behavior contracts; live scalar query використовує той самий safe query boundary.

Рішення: [ADR-004](../ADR/ADR-004-committed-read-views.md), [ADR-010](../ADR/ADR-010-query-budgets-security.md), [ADR-012 dialect](../ADR/ADR-012-sql-dialect.md), [ADR-013 AST](../ADR/ADR-013-authorized-query-ast.md), [ADR-020 contexts](../ADR/ADR-020-independent-query-contexts.md), [ADR-022 policy epoch](../ADR/ADR-022-policy-epoch-revocation.md). Один integration owner володіє shared AST/codec/security/cursor contracts; parser/planner/executor work scopes відділені після freeze. Qualification тільки exact GitHub TUnit/recovery/RF3 SDK/MCP; test source не є passing outcome.
