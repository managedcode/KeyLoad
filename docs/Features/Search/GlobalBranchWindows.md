# GlobalBranchWindows within Search

Global branch windows under [ADR-096](../../ADR/ADR-096-global-branch-windows.md) are the kernel for distributed search: a coordinating Orleans grain queries several logical partitions through ordinary Orleans calls and merges their per-modality windows. Replica copies of one partition are not independent shards. The kernel introduces bounded per-modality global windows and complete EntityRef ordering; it does not advertise a global snapshot or strict multi-shard BM25.

| Requirement | Acceptance and tests |
|---|---|
| REQ-RANK-001: global window before fusion | AC-RANK-001: independent centralized oracle and two/three window layouts return identical exact candidates/ranks for identical comparable scores, missing branch contributes zero, duplicated replica candidates contribute once; RRF is applied after global modality ordering. `GlobalBranchWindowTests` |
| REQ-RANK-002: full stable identity and comparable witnesses | AC-RANK-002: same Id in different tenant/database/domain/partition/collection remains distinct and ties use ordinal full EntityRef; duplicate reference with conflicting score/revision fails explicitly; nonfinite values or differing score-profile/corpus/statistics epochs fail, never win by arrival order |
| REQ-RANK-003: bounded truthful completeness | AC-RANK-003: record/byte/window/deadline/cancellation limits are checked before retention, failed/missing/approximate/truncated windows cannot be marked exhaustive; exact mode fails closed, explicit incomplete mode retains its missing-window/approximation flags; boundary failures release all owned temporary work |
| REQ-RANK-004: real existing runtime join | AC-RANK-004: the same core candidate-order/merge kernel is consumed by current SearchRankFusion through a single-partition adapter, preserving all current exact text/vector/graph results and source-cut authorization. Single-window fast path avoids redundant materialization; unit and replicated-cluster suites pass |

The internal generated v1 BranchWindow contract containing branch
name/kind, source logical window identity, comparable score-profile and corpus/
statistics-scope witness, completion/approximation/truncation flags and finite
candidates with full EntityRef, document revision and modality score. The merge
request declares the exact expected logical-window identities, desired candidate
window size and explicit incomplete permission. Strict default rejects any missing
window or noncomplete input. All windows of one branch must agree on score scope,
profile and statistics epoch. Each input is ordered by descending score then full
EntityRef; identical replica duplicates must agree on revision and score before
deduplication. A declared finite top-L window is window-complete, not exhaustive
RRF over candidates outside that union. Results retain that distinction.

Exact internal DTO freeze, with zero-based sequential generated IDs in listed
order and aliases `keyload.query.<kebab-type-name>.v1`: GlobalBranchKind is
Text=0/Vector=1/Graph=2; GlobalBranchScope(ScoreProfile,CorpusScope,StatisticsEpoch);
GlobalBranchCandidate(Reference,Revision,Score);
GlobalBranchWindow(BranchName,Kind,SourceWindowId,Scope,Candidates,Complete,
Approximate,Truncated,Exhaustive=false);
GlobalBranchMergeRequest(BranchName,Kind,Scope,ExpectedWindowIds,Limit,
AllowIncomplete=false); GlobalBranchMergeResult(Candidates,MissingWindowIds,
WindowComplete,Approximate,Truncated,Exhaustive,ExaminedCandidateCount).
String identifiers are nonblank and bounded by JsonData.Identifier; candidate
revision is positive; arrays are initialized; duplicate/unknown source IDs reject.
The merger receives the shared ReadExecutionBudget, charges every examined
record and exact bytes before retaining it, and retains no more bytes than the
budget's result ceiling (the optional `QueryExecutionOptions.MaximumResultBytes`).
There is no record or window-count ceiling ([ADR-149](../../ADR/ADR-149-no-operation-limits.md)).
Strict mode rejects Approximate or Truncated input; explicit incomplete mode
retains those flags. Exhaustive also requires every source Exhaustive and no
candidate omitted by the requested output window. Local fusion may use a narrow
single-window fast adapter over its already authorized canonical SearchScore
array; it does not fabricate witness DTOs or advertise distributed completeness.

Tie comparison is ordinal TenantId, DatabaseId, TransactionDomainId, PartitionKey,
Collection and Id, each compared as an independent component; never concatenate
ambiguous separators. Merge one modality before assigning1-based ranks, then
existing weightedRrfV1. A bounded k-way heap plus bounded unique-candidate state
is allowed; no task per record or unbounded transport. Every examined candidate
is charged even if duplicated, rejected or beyond the selected window. The
single-partition adapter reuses the same kernel/order and existing authorized
local branch arrays without inventing a global statistics epoch or shard
identity. Full fan-out/read-cut/policy/statistics contracts remain a later stage.

Validation rejects malformed requests, identifiers, uninitialized arrays,
nonpositive/oversized limits and duplicate/unknown logical source windows.
Corruption rejects malformed/conflicting candidates or incomparable scope.
BudgetExceeded rejects result-byte and explicit-expiry exhaustion; cancellation keeps
its cancellation exception. Strict missing/incomplete windows give
OwnershipLost; strict approximate/truncated windows give UnsupportedCapability.
The exact metadata of the expected and received window lists is included in
the one shared result-byte accounting. Every examined candidate across all
windows is counted in the same read ledger.

The merger and order live in the Query Search slice (Contracts/Models/Queries/Validation) with a narrow join into SearchRankFusion; rankers, physical topology, persisted statistics, public SearchRequest, policy projections and existing expected results are unchanged.

Frontend/new SDK/MCP syntax N/A: existing search consumes its local adapter.
Canonical data migration N/A; this internal computational stage persists nothing.
Full fan-out qualification requires an actual multiple-partition layout, missing-partition handling, bounded fan-out, current authorization/read-cut and quality/performance evidence.

```mermaid
flowchart LR
  Windows[Comparable per-modality windows] --> Validate[Scope completeness and work bounds]
  Validate --> Merge[Deduplicate and order full EntityRef]
  Merge --> Ranks[Global modality ranks]
  Ranks --> RRF[Weighted RRF and truthful completeness]
```
