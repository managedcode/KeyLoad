# Prepared storage write acceptance

Goal: one privately owned mutation projection per staged transaction generation,
shared by validation, canonical payload serialization and actual apply. Related
REQ-MP-002/AC-MP-006/012 and REQ-STORAGE-009; decision ADR-035. Actors are the
node-local provider and its synchronous public IAtomicTransaction caller. Grains,
transport, client credentials, formats and topology stay outside this source scope.

- AC-PSW-001: PrepareChanges creates/caches one sorted StorageMutation[] for the
  current staged set. PreparePayload and facade publication reuse the same array;
  no second projection, generic descriptor, extra byte copy or dummy cache exists.
  Key/value Stage ownership copies remain. Static full source/diff proof plus real
  validation/commit correctness is required; runtime memory improvements await CI.
- AC-PSW-002: successful replacement, deletion and Reset invalidate both caches
  before the next validation/commit. Repeated ValidateCommit is harmless and final
  WAL payload equals independently serialized final mutations byte for byte. A
  real store read/reopen returns the same final values and position. Caller input
  mutation cannot change committed bytes.
  Null tombstones must remain nullable null during the byte-array to read-only
  memory projection. An explicit empty Put remains a found zero-length value;
  Delete is absent before/after commit, WAL replay and snapshot/reopen, including
  borrowed-reader callback and exact range/record-count behavior. Canonical WAL
  has JSON null for Delete and an empty base64 string for empty Put; every byte
  and exact frame limit is asserted. Historical empty rows are preserved because
  no delete provenance can distinguish them from legitimate empty values.
- AC-PSW-003: an oversized rejected Stage leaves the already validated staged set
  intact; caller may catch ResourceExhausted then validate/commit the prior set.
  An empty/reset-to-empty commit has no WAL frame and no position advance. Existing
  exact MaxFrameBytes boundary and canonical 52-byte header/checksum tests remain.
- AC-PSW-004: same gate, callback, validation, WAL write/flush/apply/observer order,
  poisoning/error, recovery and ownership semantics; no public/data migration or
  analyzer/test weakening. Actual enabled builds, formatter/governance and full
  exact-SHA GitHub unit/recovery/RF3 SDK/MCP must pass before qualification.

| Criterion | Automated assertions / level | Required verification |
|---|---|---|
| 001/002 | NEW PreparedTransactionTests, real ZoneTree public commit with validate/replacement/delete/reset, owned input mutation and byte-exact WAL/read/reopen | GitHub UnitTests MTP command; source cache/projection review is an explicit manual supplement |
| 003 | Same new real-store tests for rejected oversized stage and reset-to-empty; existing FrameBudgetTests exact base64/frame boundaries | GitHub UnitTests, no local execution |
| 004 | Existing real complete-frame corruption/checkpoint/process recovery and Docker RF3 SDK/MCP | complete ci.yml exact SHA, development build then formatter/static review |

No new package, fixture double, test seam or persisted/wire/API compatibility path.
Rollback this cache source unit only; data stays compatible and mandatory quality
policy remains. Failure/cancellation regressions retain existing real paths. Numeric
coverage and measured allocations/RSS remain unqualified until genuine collectors
and baseline/candidate resource evidence exist; source inspection cannot replace them.
