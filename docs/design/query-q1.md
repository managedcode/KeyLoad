# Q1 query contract

Protocol 1 exposes read-only queries over one collection in one atomic partition. SQL and the C# expression builder lower into AST version 1. JSON supplies that AST directly. All three adapters enter the same admission gate, structural validator, authorization binder, access-path planner and executor. Unsupported constructs produce an explicit error.

`GET /v1/query/capabilities` requires an authenticated caller and reports the dialect, AST version, supported predicate families and configured budgets. `POST /v1/query` accepts SQL; `POST /v1/query/ast` accepts `AstQueryRequest` from `KeyLoad.Abstractions`.

```json
{
  "partition": {
    "tenantId": "acme",
    "databaseId": "shop",
    "transactionDomainId": "order-processing",
    "partitionKey": "customer-42"
  },
  "query": {
    "collection": "orders",
    "alias": null,
    "projection": [{ "path": "/@id", "alias": "id" }, { "path": "/status", "alias": "status" }],
    "filter": {
      "kind": "comparison",
      "left": { "kind": "field", "path": "/number" },
      "operator": "=",
      "right": { "kind": "value", "value": 1 }
    },
    "order": [{ "path": "/@id", "descending": false }],
    "limit": 20,
    "explain": false
  },
  "parameters": null,
  "allowFullScan": false,
  "cursor": null,
  "astVersion": 1
}
```

Fields use bounded JSON pointers; `/@id` and `/@revision` address canonical metadata. SQL aliases are resolved before normalization and do not change the normalized plan. Scalars are strings, booleans, decimal-policy numbers or JSON null. Arrays and objects are not scalar parameters or operands. Parameter operands use `{"kind":"parameter","name":"status"}` and bind through the request's scalar parameter dictionary.

| Construct | Semantics and cost | Required permission |
| --- | --- | --- |
| `=`, `!=`, `<>`, `<`, `<=`, `>`, `>=` | Same scalar types; null or missing yields unknown. Equality can select a point/composite equality index; remaining checks cost one comparison per candidate. | `Query` and `DocumentsRead`; protected input needs its field-use grant. |
| `AND`, `OR`, `NOT` | Three-valued logic; only true enters the result. Cost follows the bounded predicate tree. | Every input field is authorized before candidate access. |
| `IN` / `NOT IN` | Bounded scalar list, with comparison's null/missing behavior. | Field-use permission on the input and any field operands. |
| `IS NULL`, `IS MISSING` | Explicit distinction between a present JSON null and an absent path. Negation negates the corresponding test. | Field-use permission on the tested field. |
| Projection | Canonical identity/revision plus selected fields or `*`; omitted classified values remain omitted or null in a selected alias. | Row scope and current field-read projection. |
| `ORDER BY` | Ordered-key scalar policy, followed by canonical ID as a deterministic tie break; bounded in-memory sort. | Protected sort fields need field-use grants. |
| `LIMIT` | Positive result count within configured bounds. | Ordinary query permission. |
| `EXPLAIN` | Reports the authorized access path, atomic partition and scan budget. | The same binding permissions as execution. |

Full scans require explicit opt-in. Candidate count, result bytes, request bytes, predicate depth/nodes, parameter count, projection count, sort keys, execution time and simultaneous queries are bounded. This profile still requires the broader tenant memory/CPU governor and batch executor qualification recorded in the implementation tracker.

Each page uses one consistent storage read gate. Its cursor is signed and binds the normalized query, principal/policy epoch, schema, node/read generation, original cut and collection data version. Document changes atomically advance that version, including ACL changes. Unrelated catalog or collection writes preserve the cursor; a change to its source rejects continuation with `CursorExpired`. Principal revocation/policy change is rechecked before every page. Replica installation changes the read generation; compaction preserves it. A cursor is a short-lived continuation contract, not an indefinitely retained MVCC snapshot.

The C# builder accepts mapped properties, constant scalar comparisons, Boolean composition, field ordering, anonymous/member projections, and `Contains` over captured constant arrays or plain lists. `QueryFunctions.DocumentId` and `DocumentRevision` expose metadata. `QueryFunctions.IsNull` and `IsMissing` preserve Q1's explicit null/missing distinction; equality against a null literal lowers to `IS NULL`. Predicates follow canonical Q1 semantics, rather than executing a compiled CLR delegate over deserialized objects.

Property names follow the protocol's JSON naming policy and `JsonPropertyName` attributes. Captured fields may supply constants; application property getters, arbitrary method calls, custom operators and lossy numeric casts are rejected. No user assembly is executed by the server. Builder branches hold independent query contexts. More SQL features, query functions, vector attachments, session tokens, distributed plans and the PostgreSQL differential baseline remain tracked extensions.
