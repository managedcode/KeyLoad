# ADR-147: Id-ordered query continuation

Status: Accepted 2026-10-24 under the owner's direction to make KeyLoad serve real workloads at benchmark scale. In source; Linux qualification pending.

Superseded in part by [ADR-149](ADR-149-no-operation-limits.md): `MaxScanRecords` is removed, so a query range read asks storage for at most `MaximumRangeRecords` records.

## Context

A query cursor carried only an offset. Every page re-ran the whole access path and
then discarded the rows before that offset. A full scan read the entire collection
on every page, so reading a collection of n documents in pages of k cost about
n²/k record reads. The heap of retained rows also grew with the offset. The
benchmark verification reads one million documents in pages of 256 ordered by
document id, and it failed with `BudgetExceeded`. It failed even earlier for
another reason: the deployment's `MaxScanRecords` (1,100,000) is larger than the
records one storage range read may examine (`MaximumRangeRecords`, 100,000), and
storage refuses such a request before reading.

## Decision

- A query whose only order is ascending document id (`/@id`) has no OFFSET and is
  not Explain. Its cursor carries the last emitted document id (`QueryCursorClaims`
  field 14, `AfterId`) with offset zero instead of an offset. All other queries keep
  offset continuation.
- Within one partition, document ids order exactly as the collection's storage keys.
  For such a query, a full scan seeks to the key after `AfterId` and stops as soon
  as one row beyond the page is accepted; that row proves another page exists.
  Point and index paths skip the rows at or before `AfterId`.
- A cursor's shape must match its query. An id cursor presented for an offset query,
  an offset cursor presented for an id-ordered query, or an id cursor with a nonzero
  offset is `CursorExpired`. Every existing check stays: incarnation, node, read
  generation, principal, policy epoch, schema, query hash, source epoch, cut and
  expiry. Because the source epoch must be unchanged, no document of the collection
  changed between pages, so id continuation returns exactly the rows offset
  continuation would.
- A scan that starts after an id, or that stops early, covers only part of the
  collection. It may use a complete cached row-visibility set but never publishes
  one. A first-page scan that reaches the end of the collection publishes its set
  as before.
- Every query range read asks storage for at most `min(MaxScanRecords,
  MaximumRangeRecords)` records. A larger configured scan budget cannot exceed what
  storage serves in one range read.

## Consequences

- Reading a whole collection page by page reads each document once. Each page
  examines its rows plus one, and also every row that its filter, authorization or
  deletion skips.
- `MaxScanRecords` bounds one page's examined rows instead of the collection size
  for these queries, so large collections page within the default budget.
- The cursor version stays 2. Field 14 is additive, and a cursor issued before
  this change fails closed for id-ordered queries. Cursors are short-lived signed
  tokens and are never persisted.
- Multi-partition queries still have no continuation.

Related: REQ-QUERY-006, REQ-QUERY-009 / AC-QUERY-009, [ADR-010](ADR-010-query-budgets-security.md).
