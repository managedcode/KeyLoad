# ADR-010: Bounded query execution and security barriers

Status: Accepted; end-to-end qualification pending. Related current baseline: [QueryExecution](../Features/QueryExecution.md), [ResourceExecution](../Features/ResourceExecution.md), and product source [sections 13–15, 23–26, and 29–30](../design/architecture-v0.3.uk.md).

## Context and decision

Queries combine parsing/planning, index/scan work, point dereferences, graph expansion, search branches, projection, and serialized response metadata. Unbounded stages can consume memory/CPU or leak data through filtering/ranking. A request carries one bounded cancellation/deadline/work/read/result budget through relevant operators. Authorization and row/field policy must be applied before data can affect an observable result; security barriers cannot be moved below a filter or ranking stage that reveals protected values.

## Rationale and consequences

One budget prevents each operator from independently spending the full limit. Explicit failure preserves honesty when completeness cannot be proven; partial results require a separately explicit contract. Typed authorized ASTs and field lineage prevent SQL/JSON/C# surface differences from bypassing policy. Budgets may reject large valid workloads and must be surfaced as stable errors.

## Related requirements

QueryExecution `REQ-QUERY-001..003`/`AC-MP-003`, GraphTraversal `REQ-GRAPH-002..004`, DocumentStorage `REQ-DSTORE-003`, EventStreams `REQ-EVENT-001..003`, and Search `REQ-SR-001..005`.

## Implementation contract

1. Freeze per-request budget dimensions, operator charge points, error behavior, and authorization-before-observation rules.
2. Add real tests for scan/index/point accounting, metadata/result bytes, cancel/deadline, hidden rows/fields, aliases, explain, and following-operation health.
3. Implement shared budgets under `src/KeyLoad.Abstractions/Features/ResourceExecution/` and `src/KeyLoad.Core/Features/ResourceExecution/`; each canonical slice owns its operators under `Features/<Slice>/`.
4. Any budget or policy change is versioned/configured explicitly; rollback restores the prior bound without allowing an unbounded fallback. Capability manifests list only operators with an enforced budget.
5. GitHub CI executes TUnit and real RF3 API scenarios with the same caller-visible errors; root joins security/search/query owners and checks retained diagnostics without sensitive payloads.

Dependencies: [ADR-004](ADR-004-committed-read-views.md), [ADR-006](ADR-006-strict-derived-indexes.md), [ADR-009](ADR-009-search-provider-boundaries.md), [ADR-013](ADR-013-authorized-query-ast.md), [ADR-014](ADR-014-principals-rbac-row-policy.md), [ADR-015](ADR-015-sensitive-data-lineage.md), [ADR-018](ADR-018-global-rank-fusion.md), [ADR-019](ADR-019-managed-ann.md), and [ADR-022](ADR-022-policy-epoch-revocation.md). Stop if an operator cannot prove complete bounded output or policy order; do not silently truncate.

```mermaid
flowchart LR
    Request[Authenticated request] --> Parse[Validate and bind authorized AST]
    Parse --> Budget[One work time cancellation and result budget]
    Budget --> Operators[Bounded index scan graph or search operators]
    Operators --> Barrier[Row and field security projection]
    Barrier --> Result[Complete response or explicit rejection]
```


## TASK-KL051-NORMALIZED-PLAN-ERROR-001 — actual adapter plans and vector attachment boundary

REQ-KL051-PLAN-001 / AC-KL051-PLAN-001 refine existing REQ-QUERY-004/005/006 and AC-QUERY-004/005/006 under ADR004/010/118. Real SQL named decimal/string parameters, preserved JSON AST with same typed parameters and native C# Q1 builder literal lowering execute same indexed predicate/order/projection. Actual EXPLAIN pages must byte-match all three inputs, with independent literal accessPath/atomicPartition/nativeScanBudget plan, complete literal rows/source revisions and full-store/position invariance. Fresh persisted denied principal must yield exact same PermissionDenied safe detail on all three actual input operations; no returned partial page. Following privileged full literal results must remain healthy. This is real executor work, not static normalized-AST getter equality.

REQ-KL051-VECTOR-001 / AC-KL051-VECTOR-001 map REQ-QUERY-007 and existing graph-search profile to actual SQL named vector-array attachment, typed C# GraphSearchRequest and its public JSON roundtrip. Persist two real cosine vectors/raw documents, execute SQL SearchSqlAsync and native GraphSearchAsync for both typed/public JSON inputs, compare complete results with independent literal document/entity/revision/JSON/rank score/empty expansion. Wrong dimension must reject exact Validation before native access, preserve full store/cut, then valid actual operation completes. SQL parser safe detail and typed-search validator safe detail are distinct existing contract messages and remain exact; code parity does not fabricate message equivalence.

Source-proven boundary: KeyLoadQuery<T> currently explicitly supports scalar Q1 expression lowering only; there is no C# LINQ vector attachment/parameter-marker API. SQL vector attachments use Q1.Search.v1/SqlGraphSearchRequest, not scalar SELECT. These tests do not introduce a builder/parallel planner or claim three-input LINQ vector plan equivalence. Original KL051 remains open for complete vector-builder lowering and equivalent public profile errors; actual supported subset may be qualified only after original native execution. No unsupported expression is silently evaluated client-side.

Ownership UnitTests QueryExecution Cases/Helpers, existing ZoneTree/TestDatabase/QueryEngine/SearchEngine and actual public KeyLoadQuery APIs. Docs then tests; root guarded join/format/build/current native discovery normal/scalar/recovery/RF3/Linux gates; preserve originals and no source-only PASS. Rollback only tests/appendix, no production/dependency/protocol change.


## TASK-KL079-NATIVE-FEED-LIVE-001

REQ/AC-FEED-002/003/005 and REQ/AC-QUERY-001/003, AC-MP-003/012 retain one native owner read cut, persisted current/historical authorization, signed original cursors, gap-free snapshot/tail and no raw PII. ReadChangeFeed's existing two-argument API deliberately delegates to its new original-cancellation overload; no legacy path or schema. Original DatabaseEngine clock owns cursor expiry and operation deadline. One existing ReadExecutionBudget and read grant charge all actual raw storage bytes/record attempts before decode/copy, projected result retention and the complete final response. Request.MaxBytes still bounds change payload and preserves the first undelivered entry; it does not replace complete wrapper/read-work limits. Feed cancellation produces no returned partial page. Existing live reader/view budget and profile stay unchanged.

Order: this contract and canonical ChangeFeeds/ADR010 appendices precede source. Core owns public feed same-cut operation; Orleans forwards exact original token through existing native CQRS/unique request grain. Tests: native observed point-read cancellation/deadline -> original error/no partial/full canonical cut unchanged -> independent complete literal healthy feed/empty continuation; genuine Aspire RF3 SDK/official MCP reconnect, replay, historic/current row+field redaction, persisted revoke/token invalidation -> exact grant repair/fresh cursor -> healthy; live initial snapshot/concurrent committed writes/deltas/delete/current revocation -> ordinary query literal parity/healthy fresh subscription. Cold feed continuity is exercised by the RF3 reconnect case. SQL Q1 CALL uses existing tools/decoder, no new dialect/operation.

Ownership: existing Core ChangeFeeds.cs, existing Orleans GrainCoreReadCapabilities.cs, feature-local Unit and Integration ChangeFeeds roles, docs/Features/ChangeFeeds.md and ADR010 appendices. No shared fixture mutation, new option/quota/provider, alias/Id/default/deadline/protocol change. Root joins source and owns compiler/census/native normal+scalar/recovery/Linux RF3 qualification; source-authored arguments are not observed native counts or PASS. Original KL079 full criteria and mandatory full product/fault/endurance/coverage gates stay open. Rollback source stage only, no persisted migration.


## TASK-KL079-FULL-LIVE-PAGE-002

REQ/AC-FEED-002/003/005 and AC-MP-003/012 retain the existing six-field LiveQueryChange and five-field LiveQueryPage. Independent upsert revision2 and native tombstone revision3 come from the original stored document mutation, not a constructor overload. Complete SDK/official MCP/Q1 tail pages compare literal changes, through sequence, hasMore and the actual unchanged owning read cut. Each returned opaque cursor is nonempty and is consumed through the same real route; the complete empty continuation preserves through sequence, hasMore=false and the same unchanged cut. Cursor bytes are never fabricated or required to equal independently issued tokens. Current persisted revocation and obsolete-policy token failures cover SDK, official MCP and both Q1 routes, with no partial result, bracketed by existing full canonical no-effect checks; exact policy repair leads to a fresh healthy full page on all routes.

Order: feature/ADR contract, native constructor/delete semantic proof, bounded assertion helpers, unchanged two genuine RF3 case identities. No production schema/alias/Id, runtime option, timeout, topology or authorization change. Root owns fresh compiler/native census/Linux operations; this fixture correction alone does not qualify the whole task. Rollback removes only these fixture assertions and appendix together.


## TASK-KL079-COMPLETE-PAGE-COLD-003

REQ/AC-FEED-002/003/005 retain the same bounded polling/retention, current persisted privacy and six-field LiveQueryChange (original upsert revision2/delete revision3) contracts. The existing RF3 feed case must assert all seven page fields: complete independent literal changes, actual opaque cursor, exact through/tail/firstAvailable/hasMore, and actual read cut bounded by the original successful receipt. Fixture tail2 includes its hidden second row; updated tail3 follows only the actual acknowledged replacement. Each SDK, official MCP and both Q1 route consumes its own returned opaque cursor; the next page is the independently known hidden position or complete empty tail. Different issued cursor bytes/read cuts are not fabricated or required equal. Every original assertion remains.

The existing live RF3 case closes its actual original reader/administrator before genuine all-three fixture kill/restart, recreates fresh callers from the same persisted credentials and verifies original physical owner/incarnation and monotone observed generation (no +1/positive-initial assumption). The original pre-cold subscription cursor is then consumed for the actual fresh delete, exact native revision3/receipt/Remove and full page/empty continuation on all four routes, followed by persisted revocation, obsolete-policy token refusal, exact policy repair and fresh healthy snapshot/tail. This is continuation of the real original subscription, not resynchronizing under a replacement token. No connection-grain, public schema, limit/deadline, clock, topology or production behavior change.

Existing two `FeedLiveRf3Tests` identities are unchanged. Native discovery/source/PE/PDB and Linux normal/scalar outcomes plus actual owned resource/reader cleanup remain required; source review and local prior15 cases do not close the task. Existing Unit/projection process/native snapshot-install paths remain mandatory, with full product/endurance gates separate and open. Rollback removes only this fixture oracle/cold extension coherently; no state/format migration.
