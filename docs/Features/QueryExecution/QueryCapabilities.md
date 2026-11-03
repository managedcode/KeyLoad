# Current query capabilities and adapter parity

The current SQL, JSON AST and C# expression adapters enter the same authorized query engine. There is one request shape per operation, with no application format version or language-generation selector. The syntax owner is Microsoft.SqlServer.TransactSql.ScriptDom; the manifest names its selected grammar as `TSQL.ScriptDOM180`. Grammar acceptance, supported execution semantics and SQL-client interoperability have separate qualification gates.

## Manifest and requests

This is the capability and adapter-parity contract for [QueryExecution](../QueryExecution.md).

The manifest contains `SqlDialect`, `Scope`, `NumericPolicy`, `MissingPolicy`, `Adapters`, `Predicates`, `FullScanRequiresOptIn`, `ReadOnly` and `ReadProfiles`. It describes available operations, without granting access to a database or exposing resource inventories.

`QueryRequest` carries `Partition`, `Sql`, optional `Parameters`, `AllowFullScan` and optional `Cursor`. `AstQueryRequest` carries the same controls with `Query` replacing `Sql`. `SqlOperationRequest` adds the optional stable `CommandId` for mutation identity. No envelope, AST or dialect version is accepted. Unsupported syntax trees, operators and scalar types fail before candidate execution.

Current profiles include scalar reads, model views, `InnerJoin`, `InnerJoin.LeftPredicate`, `QueueEntityRefJoin`, `EventEntityRefJoin`, ordering/page capabilities, `graph-search` and `AST.Explain`. The manifest is a capability inventory, not another execution path.

## Semantics and authority

Collection operations require current persisted Query and DocumentsRead. Predicate and order fields require field-use authority; returned records use current row visibility and field projection. Numeric, null and missing semantics are shared across adapters. Named values bind separately from SQL text. Full scans require explicit consent, and page retention follows the requested limit and offset. The caller's cancellation and explicit expiry govern the complete operation.

A signed continuation binds its purpose, normalized query, principal and policy epoch, resource lifetime and schema, storage incarnation/node/read generation, source change epoch, original cut, expiry and continuation position. It has no format discriminator. A changed binding refuses continuation; no alternate reader is selected. Scalar live cursors similarly retain purpose, query hash, change-feed continuation and resource lifetime. These fields enforce security and concurrency rather than application compatibility.

AST/C# Explain produces the existing authorized one-row plan. Join and partition leaves refuse Explain. Existing model Explain uses its model-specific source validation. SQL parsing does not add an EXPLAIN keyword.

## Adapter example

The seeded corpus returns `b/Bravo/20`, then `d/Delta/40`, using its signed continuation. Named parameters are `active=true` and `minimum=20`.

```sql
SELECT TOP (1) title, score
FROM query_parity
WHERE active = @active AND score >= @minimum
ORDER BY order_id;
```

```csharp
var query = KeyLoadQuery.From<QueryRowModel>(partition, "query_parity", translationOptions)
    .Where(row => row.Active == true && row.Score >= 20m)
    .OrderBy(row => row.OrderId)
    .Select(row => new { title = row.Title, score = row.Score })
    .Take(1);
var page = await sdk.QueryAstAsync(query.ToRequest(allowFullScan: true), cancellationToken);
var plan = await sdk.QueryAstAsync(query.Explain().ToRequest(true), cancellationToken);
```

The discovered official MCP descriptor exposes the same typed request and result through `keyload_query_ast`. Unified SQL `EXEC keyload_query_ast @args` delegates to that canonical operation.

## Requirements and qualification

| Requirement | Observable acceptance | Whole-operation cases |
|---|---|---|
| REQ-QCV-001 / AC-QCV-001 | Complete literal current manifest is equal across direct, SDK and official MCP reads. | `QueryCapabilityParityWholeFlowTests.ManifestRoundTripsBuilderAndSqlPagesExplainCurrentReaderAndTwoColdOwners`; `QueryCapabilityParityClusterWholeFlowTests.ManifestAndBuilderSqlMcpPagesExplainPersistedReaderRepairAndTwoColdOwners` |
| REQ-QCV-002 / AC-QCV-002 | Independently authored AST equals the C# builder; public JSON and generated Orleans round trips execute the same complete pages. | Same cases. |
| REQ-QCV-003 / AC-QCV-003 | SQL, AST, SDK, MCP and unified SQL return literal rows and a valid original continuation; Explain matches the independent complete plan. | Same cases. |
| REQ-QCV-004 / AC-QCV-004 | Persisted permission removal refuses reads/planning without a partial result; repair restores results while the static manifest stays unchanged. Unsupported comparisons refuse before candidate work. | Same cases. |
| REQ-QCV-005 / AC-QCV-005 | Original write receipts replay and both owned cold continuations conserve records and authority bindings. | Same cases. |

These cases are functional evidence, not performance measurements. Source review, compiled diagnostics, focused execution, complete build, process recovery, actual Aspire cluster execution and Linux qualification remain distinct gates. No speed, power-loss durability or production-readiness claim follows from this contract.
