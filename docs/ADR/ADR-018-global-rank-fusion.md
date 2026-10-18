# ADR-018: Global per-modality windows and weighted rank fusion

Status: Proposed. Distributed statistics epochs, branch-window completeness, and tie policy need a quality prototype before this becomes an implementation mandate.

## Context and decision

Current `SearchEngine` builds lexical and exact-vector rankings from one authorized read view and combines branch ranks using weighted reciprocal rank fusion. Per-shard corpus statistics and local top-k pruning can change global order or omit a candidate. The target is a documented, deterministic fusion contract with explicit candidate windows and observable approximation.

Evaluate global per-modality windows followed by a versioned weighted RRF function. Specify which text statistics are global or approximate, stable tie-breaking, missing-branch contribution, candidate completeness, and resource limits before accepting wire-visible options. Do not claim global ranking completeness from current local scoring.

```mermaid
flowchart LR
    Read[Authorized branch reads] --> Text[Text candidates and statistics epoch]
    Read --> Vector[Vector candidates and exact oracle]
    Text --> Window[Global modality windows]
    Vector --> Window
    Window --> Fuse[Versioned weighted rank fusion]
    Fuse --> Project[Final authorized projection]
```

## Alternatives and consequences

Local scoring is simpler but shard distributions can skew relevance. Raw-score normalization introduces incomparable units and unstable fusion. Rank fusion makes branch scale independent but needs fixed windows and deterministic ties. Quality claims require a labeled relevance corpus; none is inferred here.

## Related requirements and implementation contract

Related: `REQ-SEARCH-001/AC-SEARCH-001`, `REQ-SEARCH-002/AC-SEARCH-002`, `REQ-SEARCH-003/AC-SEARCH-003`, `REQ-SEARCH-004/AC-SEARCH-004`, `REQ-SEARCH-005/AC-SEARCH-005`, `REQ-SEARCH-006/AC-SEARCH-006`; `REQ-SR-001/AC-MP-004` and `REQ-SR-004/AC-MP-004/005`; ADR-006, ADR-009, ADR-013, ADR-019, ADR-022. Current exact/hybrid source is in `src/KeyLoad.Query/Search/Queries/QueryExecution/SearchEngine.cs`; intended target is `src/KeyLoad.Query/Search/` and `tests/KeyLoad.UnitTests/Search/`.

1. Freeze schema, statistics scope/epoch, candidate-window semantics, ties, and RRF version before implementation.
2. Add real-store relevance/quality fixtures for lexical-only, vector-only, hybrid, missing branches, ties, filters, ACLs, cancellation, and budget exhaustion.
3. Implement bounded branch windows and fusion under the same authorized cut; preserve exact-vector oracle and current accepted request semantics.
4. Persisted statistics and generation updates must support bounded rebuild, checkpointing, rollback, and stale-generation rejection.
5. Run TUnit and cluster Search through .NET and official MCP SDK in GitHub Actions; publish quality evidence with source SHA before claiming qualification.

Current source: `src/KeyLoad.Query/Search/Queries/QueryExecution/SearchEngine.cs`, `src/KeyLoad.Core/GraphAndSeries.cs`; current tests: `tests/KeyLoad.UnitTests/GraphAndSearchTests.cs`. Planned external/provider files are owned by Search, not this ADR. Dependencies: ADR-009, ADR-019, ADR-020, ADR-022. Owner: Search lead; global quality/review join: root.

## Controlled prototype stage

REQ/AC-SEARCH-007 in
[Search](../Features/Search.md) define a controlled relevance study: 32 fixed
documents, four-dimensional vectors, six independent query and graded-qrel
definitions, Recall@5/10, MRR@10, nDCG@5/10 and test-only per-branch widths
1/4/8/32, with independent eligibility and metric goldens over real
ZoneTree/ZoneTree.FullTextSearch and production three-way ranking. There is no
production per-branch window control in SearchRequest; the study therefore
measures sensitivity over complete real branch observations using the existing
fusion and merger kernels, with explicit truncation. It cannot qualify an absent
request-time feature or a distributed scorer. No dependency, persisted format,
wire or runtime ranking change occurs; removing the controlled tests is the
rollback.

## Distributed execution

Global per-modality windows across several atomic partitions are specified by
REQ-DQUERY-GLOBAL-001..005 in
[DistributedQueryExecution](../Features/QueryExecution/DistributedQueryExecution.md).
The connection grain calls the ZoneTree owner of each requested partition through
ordinary Orleans calls under its call-local identity; each owner returns exact
lexical statistics and per-modality candidate windows after fresh persisted
authorization. The parent sums statistics with checked arithmetic, fuses with
`weightedRrfV1` and refuses (never truncates silently) when a complete global
window cannot be proven. This ADR remains Proposed for the broader global-ranking
contract.
