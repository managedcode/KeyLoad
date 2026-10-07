# ADR-118: bounded typed-row INNER JOIN

Status: Accepted for implementation; source and runtime qualification pending. Date: 2026-10-07. Owner: QueryExecution integration owner. This is a first operator slice under existing REQ-REL-004 and REQ-QUERY-007; it does not close either requirement or the full SQL workstream.

## Product boundary

Implement one read-only SQL INNER JOIN for two typed relational Collection resources inside the single PartitionRef already present in a query request. The left relation is an explicitly opted-in bounded collection scan. For each visible left row, the executor probes the right relation by its declared non-null Text primary key through the existing canonical document key. The left join column must also be declared Text. The right source column in ON must be exactly the right table's declared primary-key column. This is a primary-key indexed nested-loop join; it does not require a new secondary index encoding or another storage engine.

Accepted exact form (aliases and projected names are identifiers, values are data, and this first form has no WHERE):

    SELECT l.order_id AS order_id, r.id AS customer_id, l.total AS total
    FROM orders AS l INNER JOIN customers AS r ON l.customer_id = r.id
    ORDER BY l.order_id ASC LIMIT 100

The left ORDER BY path must equal the left schema primary key and be ascending. The executor orders the bounded result with ordinal string comparison on that key; it MUST NOT assume ZoneTree/KeyCodec byte order equals SQL string order. Since each left primary key is unique and each right primary-key probe returns at most one visible row, the ordering has no ties. LIMIT uses the existing query limit validation/default and is applied after deterministic ordering. AllowFullScan=true is mandatory because the left source is scanned. Existing MaxScanRecords, MaxQueryReadBytes, QueryDeadlineSeconds, MaxResults, MaxBatchBytes, MaxQueryBytes, token/depth, and concurrent-query admission remain the only knobs; add no join-specific default/cap.

Only two different collection names, two distinct identifier aliases, one INNER JOIN, one equality predicate, explicit non-star projection, unique output aliases, one primary-key ascending order term and the existing bounded limit are admitted. Projection is top-level declared relational columns only. Join key values compare only as exact text: no cast, collation override, normalization, expression, or implicit coercion. A missing/null left join key and an unmatched key produce no pair, as required for an inner equality join. Tombstoned or row-denied records never produce output. Reject LEFT/RIGHT/FULL/CROSS, comma joins, self-join, multiple joins, WHERE, GROUP BY, aggregation, subquery, USING/NATURAL, non-PK right key, non-text key, SELECT *, duplicate aliases, descending/non-PK ordering, EXPLAIN, model/event/queue sources, and any cursor explicitly before storage scanning. Rejection is a fixed safe UnsupportedCapability or Validation outcome according to the existing parser/validation category; never fall back to client evaluation or a broader scan.

## Versioned request, AST, and result shape

Keep unified SQL envelope SqlOperationProtocol.Version=1, existing Q1 scalar SQL, AST1, routes, operation names, and all existing field numbers/aliases intact. Add explicit query-language selection:

- Append QueryDialectVersion (native field ID 5, default 1) to QueryRequest; value 1 preserves Q1 and value 2 opts into the closed Q2 subset.
- Append QueryDialectVersion (native field ID 6, default 1) to SqlOperationRequest; its existing outer envelope Version field ID 5 remains 1. The server SQL compiler forwards this value to the canonical QueryRequest.
- Add the optional typed InnerJoin member as native field ID 8 on SelectQuery. Q1/AST1 require it to be absent. Q2 join AST requests use AstVersion=2; AST1 remains unchanged and rejects join metadata. Q2 is the explicit superset for Q1 plus this single operator, not a promise of complete SQL.
- Add source-alias fields only additively: Selection.SourceAlias at ID 2; the join clause stores right collection/right alias/left key path/right key path. Existing selection IDs 0–1, SelectQuery IDs 0–7, and all existing serialized contracts stay unchanged. Add a stable alias for the new join contract in NativeContractAliases; never repurpose or renumber an alias/field.
- Add QueryDialectVersion=2 / a stable Q2.InnerJoin.v1 capability profile to the existing capability manifest. Existing Q1 adapters and every prior capability stay present. Capability/schema tests prove Q1 is unchanged, Q2 is explicit, and unsupported version/shape fails closed.
- Preserve QueryPage and QueryRow existing fields/IDs. Append QueryRow.Sources at native field ID 5, null/omitted on every existing Q1 row. Add a generated aliased QueryRowSource value containing exact source alias, entity ID, and revision. A Q2 pair row keeps EntityId and Revision as the left row's identity and includes exactly two Sources entries in declaration order (l, then r) so the pair is unambiguous. Json is a flat JSON object containing only explicitly projected output aliases; duplicate aliases reject before scan. Redacted is true if either source projection redacted fields; RedactedFields uses source-qualified paths such as l.ssn and r.ssn. QueryPage.CutPosition is the cut captured inside the owning view; AccessPath is a stable bounded-primary-key-join profile; Cursor is always null and any supplied cursor rejects.

The exact current HTTP/SDK query response and official MCP keyload_query_execute / keyload_sql_execute result must serialize the same QueryPage/QueryRow shape. No new route, dispatcher, grain, caller role, SQL protocol claim, or private extension result is introduced.

## Read, authority, execution, and resource contract

QueryEngine continues to admit the operation once through its existing query concurrency governor and construct one ReadExecutionBudget from the request cancellation token and existing DatabaseLimits. It calls existing DatabaseEngine.WithQueryView for the left collection; inside that single Store.Read view it loads the persisted principal and left resource, then requires Capability.Query | Capability.DocumentsRead for the right collection and resolves it with DatabaseEngine.Resource(view, partition, name, ResourceKind.Collection). Both resource resolutions validate transaction-domain equality. Both persisted relational schemas and the typed key declarations, all query-field-use requirements (including both join keys and the order field), and input/projection aliases are validated before the first document scan or point read. Client-supplied roles have no effect.

The executor scans only the canonical left collection prefix from DocumentStorageKeys.Prefix through the existing view and shared read budget. It counts every delivered left storage record (including deleted/row-hidden records) as scan work. For each live row that passes Authorization.CanReadRow, it charges one additional join-probe work unit before the right primary-key lookup. Total delivered left records plus attempted right probes cannot exceed configured MaxScanRecords; range lookahead is separately byte-charged by the native view and any HasMore beyond the allowed left scan rejects with BudgetExceeded. Each right read uses DocumentStorageKeys.RecordKey plus ReadExecutionBudget.ReadRecord<DocumentRecord> so bytes are charged before decode. No result is disclosed unless both left and right records pass their persisted row ACLs.

Both rows are projected using the existing DatabaseEngine.Project(principal, resource, row) on that same view before the selected values are assembled. This preserves per-resource field redaction and canonical JSON; the output assembler qualifies redaction metadata by alias and never returns a raw unprojected row. Sorting stores at most the configured scan/work maximum in memory, compares left primary-key strings ordinally, and checks cancellation/deadline at each scan/probe/sort/projection phase. Exact serialized page bytes, including source identities and redaction metadata, are checked with existing ReadExecutionBudget.CheckResult / MaxBatchBytes; raw store bytes are charged against MaxQueryReadBytes. No streaming/materialization beyond the existing bounded page, no hidden retry, no second read cut, and no work after the read callback.

The page cut is captured from database.Store.Position inside the same WithQueryView callback before reads; because all rows and resource metadata are read under one native Store.Read gate, the result is one local committed cut. This does not claim a cross-node global snapshot or power-loss durability. The normal replication/read actor continues to own placement and RF3 acknowledgements; the join adds no cross-partition reads and does not bypass one grain per request.

## Stable criteria under existing requirements

These are subcriteria for existing REQs, not new product REQs:

- AC-REL-004-JOIN-001: exact Q2/AST2 positive shape compiles; Q1/AST1 and all unsupported join variants preserve their existing behavior and reject unsupported inputs before storage access.
- AC-REL-004-JOIN-002: same-partition typed-row join returns only exact text-key matches, ordered by ordinal left primary key, with explicit output aliases and exact Sources identity metadata; null/missing/unmatched, deleted, and row-denied cases emit no pair.
- AC-REL-004-JOIN-003: persisted Query/DocumentRead, both resource transaction domains, both row ACLs, both join-key field-use policies, safe projection/redaction, and no-disclosure error behavior are demonstrated with real persisted policy.
- AC-REL-004-JOIN-004: cumulative scan/probe, raw-byte, deadline/cancellation, result count, and serialized result-byte ceilings fail closed at boundary and boundary+1 using the already validated configuration; no new defaults or unbounded candidate retention.
- AC-REL-004-JOIN-005: SQL/AST capability manifests, native serialization, HTTP SDK, and official MCP expose the same explicit versioned result and keep every Q1 contract/output unchanged.
- AC-REL-004-JOIN-006: actual whole-operation RF3 SDK + official MCP read-after-write flow, denial/error with unchanged persisted rows, successful healthy follow-up, and recovery/reopen result are verified. Exact-source Linux qualification remains pending until authentic artifacts exist.
- AC-QUERY-007-JOIN-001: declares the supported Q2/AST2 operator and exact unsupported cases; no silent client evaluation, partial/cross-shard execution, cursor, or global snapshot claim.
- AC-QUERY-007-JOIN-002: real QueryEngine and public callers prove the single persisted authorized read cut, deterministic pair order, exact source identity/redaction, cancellation, work/byte/result bounds, and safe failure behavior.

An integration test must seed both schemas and rows through real SDK commits, read through SDK plus official MCP, compare exact Rows, Sources, AccessPath and JSON, and assert each cut is at/after the seed commit. A deterministic concurrent RF3 scenario uses one atomic command to move a left reference and update the matched right generation; every observed join must show one consistent generation pair. Persisted identities lacking right Query/DocumentRead or field-use permission must be rejected without disclosing values, and an administrator query afterward must return the original expected rows. Unit cases use real ZoneTree and test actual store close/reopen; the RF3 suite reopens by its owning test fixture/process recovery flow only if that exact scenario is supported by existing harness, otherwise keep reopen evidence in the real ZoneTree unit flow and report RF3 process restart as a separate gate. Negative budget, denial, malformed-input and cancellation flows are read-only, assert exact existing error categories and no persisted state change, then run a healthy query.

## Deliberately open scope

This stage does not implement or qualify arbitrary/multiple joins, self-joins, non-primary right indexes, cross-partition or federated joins, outer/cross joins, filters on joined sources, derived tables/subqueries, aggregate/group/window expressions, DML, foreign keys, checks, defaults, cascades, constraint deferral, distributed snapshots, resumable join cursors, generalized typed AST predicates over source aliases, full SQL grammar, PostgreSQL wire protocol, or a standard SQL client connection protocol. These remain required product work; they need their own bounded contracts/ADRs and measurements. REQ-REL-004, REQ-QUERY-007, full SQL syntax and native protocol remain open after this slice. Do not update status to Implemented or advertise full SQL.

## Ordered implementation ownership

1. TASK-REL-004-INNER-JOIN-001 — freeze this ADR and update only current owning RelationalStorage.md / QueryExecution.md requirement mappings. Root owns shared public/transport joins.
2. TASK-REL-004-INNER-JOIN-002 — Abstractions owns src/KeyLoad.Abstractions/Features/QueryExecution/Contracts/QueryAst.cs, new InnerJoinClause.cs, QueryRequest.cs, SqlOperationContracts.cs, QueryRow.cs, new QueryRowSource.cs, NativeContractAliases.cs, and Query capability manifest contracts. It appends field IDs only, with Q1/AST1 golden serialization tests.
3. TASK-REL-004-INNER-JOIN-003 — Query owns src/KeyLoad.Query/Features/QueryExecution/Execution/SqlParser.cs, SqlSyntax.cs, SqlTokenizer.cs only if needed, Validation/QueryValidation.cs, Validation/QueryFieldAuthorization.cs, Contracts/QueryCapabilityCatalog.cs, Queries/QueryEngine.cs, and new Queries/RelationalInnerJoinExecutor.cs plus Execution/RelationalJoinProjection.cs. Core remains the only storage/index authority; call its existing DatabaseEngine.Resource, Authorization, Project, ReadExecutionBudget, and canonical DocumentStorageKeys APIs. No Core storage mutation or copied key codec/index writer.
4. TASK-REL-004-INNER-JOIN-004 — Server/API owner updates src/KeyLoad.Server/Features/QueryExecution/Queries/SqlOperationCompiler.cs to forward dialect version. Existing QueryApi, GrainQueryReadCapabilities, route, SDK send path, MCP query operation and official SDK registration remain the same unless source review proves an actual typed route join is needed; any expansion requires exact new guards before coding.
5. TASK-REL-004-INNER-JOIN-005 — Unit owner adds tests/KeyLoad.UnitTests/Features/QueryExecution/Cases/SqlInnerJoinContractTests.cs, SqlInnerJoinExecutionTests.cs, and a feature-local fixture only if the existing TestDatabase cannot express real store reopen. Keep each type within local policy and use real ZoneTree/persisted authorization.
6. TASK-REL-004-INNER-JOIN-006 — Integration owner adds tests/KeyLoad.IntegrationTests/Features/RelationalStorage/Cases/RelationalSqlRf3JoinTests.cs and, only if necessary, a cohesive helper/fixture under Helpers/. Use existing ClusterFixture, SDK, and official MCP APIs; no fake nodes, application-side join or manually started containers.
7. Root joins, reviews stable serializer/schema and all budgets, runs canonical build/format/unit/recovery/RF3 SDK+MCP gates, and records exact source-bound artifacts. Failed/missing gates remain open; local source review does not qualify product behavior.

## Required operation-level tests

Unit, real ZoneTree:
- exact 2-table match and multiple left rows that point to one or more right PKs; verify exact ordinal pair order, aliases, source IDs/revisions, cut and explicit projection; insert in reverse/random order to prove result order is SQL ordinal, not native byte order.
- null/missing/unmatched left key, deleted row, denied left and denied right row; each omits output. A nullable/redacted column remains safe in JSON and RedactedFields is correctly alias-qualified.
- both resource permissions, both join-field-use permissions, missing/wrong-kind/untyped resource, cross-domain resource, wrong join type/right non-PK, missing full-scan consent, duplicate aliases, star, invalid versions, all unsupported join forms, supplied cursor; each rejects before scanning where applicable and never returns raw payload.
- exact MaxScanRecords and +1 across delivered-left plus probe work, right lookup bytes, total query read bytes, result rows and serialized page bytes; operation deadline/cancellation, and an actual successful query after each rejected/canceled call.
- concurrent atomic writer toggling paired generation values while queries execute; every returned pair shows the same generation. Close/reopen a real store and repeat the exact expected join and identities.

Real RF3, existing AppHost-owned fixture:
- configure both typed schemas and seed cross-table rows with authenticated SDK command operations; query through KeyLoadClient.QueryAsync and official MCP keyload_query_execute (and unified SQL MCP adapter if existing wrapper is the intended target) with dialect 2; assert same complete page, row JSON, pair identities, access path and cuts after the seed commit.
- use a persisted principal with left-only and then missing join-key field use; SDK and official MCP receive safe PermissionDenied, no source values leak, all seeded state stays unchanged, then admin reruns and gets exact expected result.
- concurrently commit an atomic generation/reference update on one request while RF3 reads execute; only whole old or whole new joined generation is allowed. Assert normal RF3 membership/acknowledgement via existing fixture; no new topology or durability claim.
- cancellation and work/byte-limit rejection are visible as typed safe errors, settled through the existing SDK/MCP path, and followed by a successful admin read. Actual recovery/reopen flow stays with the existing process-recovery/AppHost fixture; do not claim a test fixture rebuild is a process restart.

The integration owner freezes the exact additive request/result shape before scoped source work. Root retains source review, shared joins and every native/delivery gate. This stage leaves the parent relational/full SQL requirements open.

## Root implementation refinements frozen before source work

Current Q1/AST1 operation semantics, field IDs and aliases remain active. Q2 is an
explicit SELECT opt-in and AST2 accepts current valid AST1 operations as well as
this closed join shape. With no join, source-alias metadata must be absent and
normal Q1 execution still applies. Join SQL selected as Q1 and join metadata in
AST1 reject before scan. Unknown versions reject before operation execution.
The unified CALL path accepts only the default query dialect selector 1; a
nondefault selector on CALL is explicitly unsupported rather than ignored.

The current capability manifest's singular protocol/AST/dialect fields remain
its existing defaults. Append explicit `SupportedAstVersions` at native ID16 and
`SupportedQueryDialectVersions` at ID17 with exact inventories [1,2]; append the
Q2.InnerJoin.v1 read profile. Do not silently redefine a default field as the
latest supported version. Add no capability beyond this qualified operator.

Use `JsonIgnore(Condition = WhenWritingNull)` on QueryRow.Sources and other new
optional join members so current non-join row bodies omit new null metadata.
The common typed public SDK/MCP schema intentionally adds the optional join
metadata; existing schema properties, required members, type sets and hints stay
current. Native schema oracles must include the exact optional extension and its
two-source bounded item shape, based on actual official SDK output. Q1 result
bodies and operations remain unchanged; do not claim the common typed schema is
byte-identical after adding a public member. Preserve every current generated
serializer alias/field ID and add only new explicit aliases.

Require field-use authority for both join keys and the left ordering key before
scanning. Project selected values through existing field-read/redaction rules;
a projected-only field does not gain a new unconditional field-use requirement.
Validate declared columns and unique aliases first. Do not expose a right probe
or projected source unless both row ACLs permit it. Define a redacted projected
value using the current safe omission/null semantics and qualify its metadata by
source alias without returning raw source JSON.

Retained candidates, including strings/projection/identity metadata, must stay
bounded by the existing scan/work and charged byte budgets; prefer retaining
only the requested deterministic LIMIT candidates where possible. Never assume
native key order equals SQL ordinal text order. Cancellation/deadline checks
must preserve their original typed error even if sorting callbacks throw; do
not allow a library wrapper exception to become a fabricated Validation result.
The complete returned page is byte-checked after LIMIT, including identities and
redaction metadata; LIMIT is ordinary SQL truncation and not a continuation.

The exact source map permits cohesive feature-local files prefixed SqlInnerJoin,
RelationalInnerJoin or RelationalJoin under the declared roles when required by
200-type/64-method limits; no partial-type bypass or repository-wide layer.
Existing current conformance cases which use version2 as the formerly unknown
control must move that negative control to unknown version3 while preserving
their complete-operation assertions. QueryApi/SDK/Orleans transport stays shared;
only exact optional-schema conformance assertions in the existing Integration
QueryExecution/ClientApi slices may join the additive metadata changes. All
other expansion requires root source-map review before code.

Rollout is one homogeneous current-source RF3 image and the existing public
routes, with explicit language/AST selection. Rollback removes this operator,
new optional members/profiles and matching tests together; no persisted data,
replication/atomic journal format or deployment topology changes. No old-format
reader, migration, compatibility fallback, FK promise, full SQL or native SQL
client protocol qualification is introduced. Root owns build/format/unit/scalar,
real process recovery, actual Aspire RF3 SDK/MCP, original Linux artifacts and
stage commit/push. Luna owns the guarded implementation packet only.

