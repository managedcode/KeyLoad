# FilteredRetrieval within Search

Root accepts this implementation contract on 2026-10-04 for KL-032/060.
Decision: [ADR-087](../../ADR/ADR-087-filtered-retrieval.md). The original
architecture acceptance criteria remain mandatory; this is an ordered delivery
contract, not a declaration that the tasks are qualified.

## Public stage F1 and acceptance

`SearchRequest.AllowedIds`, native field11, is an optional immutable array of
canonical document IDs in the request's existing partition/collection. Null
preserves existing exact search. An empty array selects nothing. Duplicates are
normalized with ordinal identity; unknown, hidden, deleted and stale entities
never return a hit or an existence/count diagnostic. Validate every identifier,
the incoming array length against MaxScanRecords, aggregate serialized request
bytes against MaxQueryBytes and all existing search limits before reading.
Persisted authorization remains required even for an empty allowlist.

| Requirement | Acceptance, pass/fail and mapped tests |
|---|---|
| REQ-FILTER-001: allowlist is an output eligibility constraint | AC-FILTER-001: real-store text, vector and hybrid queries equal an independent exact oracle over the eligible authorized corpus; null/empty/duplicates/unknown IDs and stable ties pass. `FilteredSearchOracleTests` |
| REQ-FILTER-002: one canonical policy/read cut remains authoritative | AC-FILTER-002: hidden/deleted/stale/reinserted/revoked hits never survive; field/capability denial remains explicit even for zero results. `FilteredSearchPolicyTests` |
| REQ-FILTER-003: bounded exact fallback produces complete results | AC-FILTER-003: input/work/result limits and cancellation fail with the existing typed failure and no partial success; the next healthy read succeeds; no full JSON/vector retention beyond existing bounds. `FilteredSearchBudgetTests` |
| REQ-FILTER-004: adaptive ANN uses the entire admissible graph | AC-FILTER-004: internal planner uses full-corpus ordinal eligibility, exact small-set, bounded expansion and fully charged fallback; 100/10/1/0.1 percent and correlated cohorts reach recall >=0.95 or fail explicitly. `AdaptiveFilteredPlannerTests`; F2 below |
| REQ-FILTER-005: public approximation and RF3 are separately qualified | AC-FILTER-005: F3 proves declared versioned completeness/mode/cost metadata and actual .NET/official MCP RF3 results after faults. Planned `FilteredSearchRf3Tests`; existing array replies remain complete exact results in F1 |

## Ordered execution and ownership

1. Root owns the appended shared SearchRequest field, JSON/native ingress and
   negotiated contract epoch joins; no older peer may silently ignore a filter.
2. Luna lifecycle_wave owns SearchEngine.cs, Features/Search/VectorRanker.cs,
   new FilteredSearch/FilteredVectorPlanner helpers and named TUnit test files.
   Apply eligibility before branch ranks and fusion; lexical corpus statistics
   continue to use the authorized corpus, not a caller-controlled IDF corpus.
   Reuse canonical visible-vector/document checks and rank/fusion contracts.
3. F2 adds the computational adaptive planner over the existing packed ANN
   candidate with actual charged eligibility/cardinality/expansion/fallback
   counters. Do not rebuild a graph from the output-filtered set or silently
   replace complete public exact rankings with an ANN/windowed reply.
4. F3 depends on ADR-019's accepted durable generation/replay and explicit public
   approximation contract. Root freezes that contract before its implementation;
   F1/F2 do not close AC-ANN-007/008 or the complete KL-032/060 criteria.
5. Root reviews/integrates, builds/formats, runs mapped cases and unit/scalar,
   recovery and genuine RF3 through the actual Aspire AppHost, records original
   source-bound outcomes, commits the stage and continues independent tasks.

Baseline: build22/formatter22 passed; full unit18 passed3060/3060; scalar22
passed3059/3060 with ComparisonHostCleanupTests startup/pipe-join failure. That
unrelated failure remains recorded and does not block F1 implementation.
Client/MCP transport reuse the existing typed search body; interactive UI N/A.
No canonical stored record migration occurs in F1. F2 remains computational;
rollback rejects filtered capability on older nodes rather than dropping IDs.

```mermaid
flowchart LR
    Request[Bounded allowlist] --> Cut[Persisted policy and canonical read cut]
    Cut --> Corpus[Authorized corpus and statistics]
    Corpus --> Eligible[Output eligibility]
    Eligible --> Exact[Complete exact branch rankings]
    Exact --> Fusion[Unchanged weighted fusion]
    Fusion --> Reply[Authorized complete result or typed failure]
```
