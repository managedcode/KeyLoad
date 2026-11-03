# ADR-118: bounded typed-row INNER JOIN

Status: Current implementation contract; exact-source build, operation, cluster and Linux qualification remain separate gates. This finite operator supports REQ-REL-004 and REQ-QUERY-007 without claiming full SQL.

## Current request and execution

SQL and typed AST requests have one current shape. Neither request, unified SQL envelope nor capability manifest carries a KeyLoad format generation. Microsoft ScriptDOM owns syntax parsing; KeyLoad rejects unsupported syntax trees before effects. `SelectQuery.InnerJoin` carries the declared right collection/alias and both key paths. `Selection.SourceAlias` identifies the projection source. The manifest advertises `InnerJoin`, `InnerJoin.LeftPredicate`, `QueueEntityRefJoin` and `EventEntityRefJoin` by behavior.

```sql
SELECT TOP (100) l.order_id AS order_id, r.id AS customer_id, l.total AS total
FROM orders AS l INNER JOIN customers AS r ON l.customer_id = r.id
WHERE l.total >= @minimum
ORDER BY l.order_id ASC;
```

The two typed collections are distinct and share the admitted `PartitionRef` and transaction domain. Aliases are distinct identifiers; output aliases are unique and explicit. The left join column is declared Text. The right join column is exactly the declared non-null Text primary key. Text equality has no implicit cast, normalization or collation override. Null/missing/unmatched keys, deleted records and row-denied records emit no pair.

The left source is an explicitly opted-in scan in one ZoneTree operation view. Each visible retained left row probes the canonical right document key through the same view. The requested order is the ascending left primary key, compared ordinally; storage byte order is not substituted for SQL text order. Retained pairs are bounded by offset plus limit. Cancellation and explicit expiry are checked through scan, probe, sorting and projection. [ADR-149](ADR-149-no-operation-limits.md) governs the absence of scan, read-byte and result-byte ceilings.

Both collections require persisted Query and DocumentsRead. Both join keys and the left ordering key require current field-use authority before scans. Selected source values pass independent row visibility and field-read/redaction policies before assembly. Use authority never grants Read authority. Projected-only fields gain no unconditional Use requirement. Declared schemas and unique aliases are validated before data work.

Every observed left record must match its admitted partition, collection and actual storage key before deletion, ACL or predicate omission. Each observed right probe must match its exact declared identity. Every readable nondeleted typed record contains exactly one primary property equal to its canonical entity ID. Mismatches return a data-free Corruption diagnostic. These checks do not create another storage authority or read cut.

The page contains exact source identities/revisions in left/right declaration order. Its `EntityId` and `Revision` identify the left record; `Json` contains only selected aliases. Redaction paths are source-qualified. `CutPosition` is captured inside the same operation view, `AccessPath` identifies the primary-key join, and no join continuation is accepted. This local committed cut does not imply a cross-node global snapshot.

## Supported predicates and model references

`InnerJoin.LeftPredicate` uses the common scalar predicate tree over declared top-level left columns, including comparisons, Boolean composition, IN, BETWEEN, null and missing tests, and named scalar parameters. Parameter and field-use validation precede candidate reads. False or unknown predicates omit the right probe. Right-qualified, nested and metadata predicate paths are refused before IO. A right alias named payload or headers cannot acquire left-source meaning.

`QueueEntityRefJoin` uses the existing queue model view as its left source. Only the declared canonical reference join, such as `q.payload.to = d.entity_ref`, resolves a complete same-partition identity and probes the declared typed target. Queue field/header policies are rebased through the existing model policy owner. Missing or hidden targets omit the pair; malformed or mismatching references refuse. The read changes no ACK, lease, payload, queue order or counters.

`EventEntityRefJoin` uses the existing retained event/topic view and validates the source generation, contiguous history and event IDs. Fresh source and reference-field authority remains mandatory even on empty sources. It resolves only the declared typed target and never performs a remote probe.

These model joins share current explicit source-qualified projections and both source policies. A masked parent reference cannot expose its nested ID; a denied right field remains masked. Revocation and repair are observed from the same-view persisted principal. Predicate Use and reference Use remain independently enforced.

## Unsupported execution

Outer/cross/comma/self/multiple joins, non-primary right indexes, arbitrary right predicates, derived tables, aggregates, window expressions, arbitrary projection expressions, EXPLAIN and join cursors remain unsupported. Valid unsupported syntax is distinguished from malformed syntax. KeyLoad performs no client evaluation or alternative reader selection. Full SQL semantics, foreign keys, distributed joins and SQL-client interoperability retain their own open qualification scope.

## Observable acceptance

| Criterion | Acceptance |
|---|---|
| AC-REL-004-JOIN-001 | Current SQL and AST accept the closed shape; unsupported joins and invalid source/alias forms refuse before scanning. |
| AC-REL-004-JOIN-002 | Exact text matches, ordinal order, aliases, source revisions, omission and safe redaction match independent literal results. |
| AC-REL-004-JOIN-003 | Persisted resource/domain/row/field policy is enforced without disclosure; healthy operation follows denial and repair. |
| AC-REL-004-JOIN-004 | Original caller cancellation or elapsed explicit expiry returns no partial page; retained candidates respect offset plus limit. |
| AC-REL-004-JOIN-005 | SQL, JSON AST, generated serialization, SDK and official MCP use the same current result and capability contracts. |
| AC-REL-004-JOIN-006 | Actual cluster read-after-write, negative/healthy follow-up, concurrent atomic source updates and owned recovery retain the complete source image and receipts. |
| AC-QUERY-007-JOIN-001/002 | Supported operators, exact refusal behavior, one authorized cut and deterministic source-aware projection are explicit. |

`JoinCapabilityWholeFlowTests` exercises actual JSON/generated-binary AST round trips, real SQL parity, complete independent literal pages, and unsupported predicate/Explain/alias/self-source requests with no reads or effects and a following healthy join. `SqlInnerJoinExecutionTests`, the source-identity and policy cases, and the cluster join cases retain their own complete-operation checks.

REQ/AC-QUERY-JOIN-FILTER, QUERY-QUEUE-SELECT, QUERY-EVENT-SELECT, QUERY-JOIN-ALIAS, QUERY-COMPOSED-LINEAGE and QUERY-COMPOSED-PLAN keep their existing stable requirement identities. Their tests must retain original seed/repair receipt replay, actual persisted policy repair, unchanged complete source images and two owned cold continuations. Source review and local functional cases do not establish Linux cluster qualification or performance.

Orleans owns connection execution and placement. The query calls the existing canonical database and storage owners; it introduces no dispatcher, storage provider, lock or cluster runtime.
