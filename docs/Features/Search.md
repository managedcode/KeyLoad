# Search

Status: Accepted resource repair contract; implementation and CI qualification pending.
Decision: [ADR-035](../ADR/ADR-035-memory-performance.md), TASK-MP-006B.
Scope: exact persisted-policy text/vector/hybrid reads under one committed apply cut.
Public request/response JSON, BM25 tokenization and reciprocal-rank fusion remain identical.

| Requirement | Acceptance / pass and fail condition | Test and evidence |
|---|---|---|
| REQ-SR-001 | AC-MP-004: every visible corpus document still affects BM25 count/average/term frequency, including missing fields; ranks, ordinal ID ties, normalization and query-term accumulation are exact | Real-store Unicode/repeated/missing/nonmatching corpus and golden score/rank cases; GitHub TUnit |
| REQ-SR-002 | AC-MP-004: vector candidates obey space/revision/row/field authorization; query finite/dimension/metric validation occurs once; selected SIMD/scalar metric preserves accumulation | Real-store stale/hidden/mismatched-space cases plus finite/zero/large-vector metric golden cases |
| REQ-SR-003 | AC-MP-003/004: scans consume scoped borrowed records; retained rank state contains identity/statistics/scores rather than full corpus JSON/vector arrays | Real large-payload rank cases, bounded read failures and allocation/resource artifacts |
| REQ-SR-004 | AC-MP-004/005: branch ranks and text-then-vector RRF accumulation are unchanged; select at most Limit results before decoding/projecting final payloads and account actual final point reads | Exact text/vector/hybrid ranks, ties/weights, shared-budget and output boundary cases |
| REQ-SR-005 | AC-MP-004/012: cancellation/work/result exhaustion leaves the committed position unchanged and a following call succeeds; no unbounded gate/iterator lifetime | Real persisted-policy cancellation/overflow/healthy-following-call tests; RF3 SDK/MCP integration |
| REQ-SR-006 | AC-MP-003/004/011/012: all analytical engines on one DatabaseEngine obey one positive MaxConcurrentQueries ceiling; saturated/cancelled calls cannot perform ranking or storage work | TASK-MP-006C real-store mixed-entry admission/release cases in UnitTests/Features/ResourceExecution; exact GitHub qualification |

```mermaid
flowchart LR
    Caller --> Validate[Validate and prepare query]
    Validate --> Cut[Authorize within one read cut]
    Cut --> Corpus[Visit visible corpus records]
    Corpus --> Metadata[Keep bounded identity and rank statistics]
    Metadata --> Fusion[Exact branch ranks and fusion]
    Fusion --> Top[Select requested top results]
    Top --> Project[Budgeted selected point reads and projection]
    Project --> Output[Measure complete response]
```

Lead owns `Core/Features/DocumentStorage/DocumentStorageKeys.cs`,
`Core/Features/DocumentStorage/VisibleDocumentReads.cs`,
`Core/Features/Search/VisibleVectorReads.cs` and existing Core read-helper joins.
Both visitor methods use the existing read view and one ReadExecutionBudget; they
decode a single record, check persisted row visibility and invoke an owned-record
callback without accumulating a full page. Vector visits charge each referenced
document and reject stale/deleted/hidden candidates as before. Existing public
array-returning read helpers consume the same visitor algorithm for their distinct
owned-result contracts. No callback may mutate the read transaction or escape the cut.

TASK-MP-006B owns `Query/SearchEngine.cs`, new `Query/Features/Search/` helpers and
new `UnitTests/Features/Search/` tests. It may update only the shared-budget numeric
calculation in `ReadExecutionTests.TextAndVectorBranchesShareOneReadByteBudget`
to include the real selected-document reads; all assertions and existing cases stay.
Other test/contract/server/CI/docs files belong to the lead or their existing owner.

Text ranking retains lengths and query-term counts plus identities only for
matching records; all visible documents still contribute corpus statistics.
Compile the text JSON pointer once. Vector ranking retains identities and scores;
prepare the query's finite validation and cosine norm once, compute only the chosen
metric, and preserve the existing Vector.Widen/Vector.Dot/scalar reduction grouping.
Exact hybrid fusion requires bounded metadata for every ranked candidate; dropping
branches to Limit before fusion is forbidden. A bounded top-result heap then
selects final payloads. Final selected point reads trade at most Limit extra reads
for removing full-corpus payload retention, remain inside the same read cut, and
consume the shared raw-byte budget. They are never free or described as eliminated.
`DocumentStorageKeys.RecordKey` and `Prefix` supply canonical document keys to
both visitors and selected reloads; their stored bytes remain identical.

Positive, negative, edge and error coverage includes missing/empty text, Unicode,
repeated terms, differing corpus lengths, same-score IDs, zero vectors, nonfinite
values, dimension/space mismatch, stale revision, hidden/redacted fields, exact and
excess byte/output/work budgets, cancellation and recovery of subsequent reads.
Tests use real ZoneTree and persisted policy, never fakes. GitHub Actions owns all
test/load execution. Build/formatter checks are development/static evidence only.
UI/data migration are N/A because stored format and wire shape do not change;
RF3/MCP public integration and measured server memory remain required join gates.

## Portable SIMD validation qualification

TASK-MP-006D, REQ-SR-002 and AC-SEARCH-001 expand the metric edge proof before
optimizing finite-value validation. Public `SearchEngine.Similarity` cases and
real-store search cases live in NEW
`tests/KeyLoad.UnitTests/Features/Search/VectorMetricValidationTests.cs` and
`VectorMetricGoldenTests.cs`. The test worker owns only these files; the lead
alone owns any `PreparedSimilarity.Validate` implementation, CI, docs and join.

Pass requires exact error codes/details for query and candidate NaN/+Infinity/
-Infinity at every full-block lane and scalar-tail position; empty, 4097 and
mismatched dimensions; query-validation-before-invalid-metric precedence. Finite
lengths 1, runtime vector width, width+1, two widths+1 and 4096 preserve selected
metric goldens, finite extremes and zero-vector handling. Normal and
`DOTNET_EnableHWIntrinsic=0` GitHub invocations must both qualify the same source;
the public scoring oracle and real-store outcomes must agree. No private test
provider or mock is introduced.

Only full finite-validation blocks may use portable `Vector.Abs` and strict
`Vector.LessThanAll(..., +Infinity)`; the length check, scalar `float.IsFinite`
tail, query/candidate error order and all metric reductions remain identical.
Existing tests and full RF3/SDK/MCP gates remain required. ADR-035 records ordered
baseline/implementation/fallback stages. This internal compatible optimization has
no data/wire migration; rollback restores the validation loop. Numeric performance
budgets and improved throughput/allocation claims still require TASK-MP-011B's
matched actual server receipts.

## Повний пошуковий контракт

Актори: authorized text/vector/hybrid caller та майбутній index-generation worker. Entry points: [SearchRequest](../../src/KeyLoad.Abstractions/Queries.cs), [SearchEngine](../../src/KeyLoad.Query/SearchEngine.cs), [canonical vectors](../../src/KeyLoad.Core/GraphAndSeries.cs), [.NET SDK](../../src/KeyLoad.Client/KeyLoadClient.cs) і [HTTP search](../../src/KeyLoad.Server/ApiEndpoints.cs). Source-present request-time lexical scoring, exact vector scoring і weighted rank fusion відрізняються від майбутніх provider-backed indexes.

| Вимога | Acceptance / flows | Test mapping |
|---|---|---|
| REQ-SEARCH-001: canonical vector space/revision/dimensions/metric мають exact semantics | AC-SEARCH-001: valid vector search збігається з exact metric oracle; nonfinite/malformed/dimension/space mismatch відхиляється; stale/deleted vector не входить у result; finite large/zero values мають declared handling | Existing `HybridFusionRanksEligibleDocumentsAndInvalidatesStaleVectors`, `LargeFiniteVectorsAndSampleValuesRemainValidAndSimilarityDoesNotOverflow` у [GraphAndSearchTests](../../tests/KeyLoad.UnitTests/GraphAndSearchTests.cs); metric edge expansion PLANNED |
| REQ-SEARCH-002: lexical/hybrid branches мають deterministic scoring, weights та ties | AC-SEARCH-002: empty/missing/repeated/Unicode terms і same-score IDs зберігають current lexical/fusion algorithm; corpus statistics і branch-rank windows не silently обрізані перед exact fusion | Existing hybrid test та REQ-SR-001/004 golden cases; full golden/multi-branch quality corpus PLANNED under [ADR-018](../ADR/ADR-018-global-rank-fusion.md) |
| REQ-SEARCH-003: усі branches використовують один authorized current cut | AC-SEARCH-003: hidden rows/vertices/protected field-use заборонені; payload/metadata не містить omitted PII; invalid/revoked grants відхиляються до unsafe scoring; selected point reads залишаються в тому самому cut | Existing [SecurityAndQueryTests](../../tests/KeyLoad.UnitTests/SecurityAndQueryTests.cs), GraphAndSearchTests; real SDK/MCP search adversarial expansion PLANNED |
| REQ-SEARCH-004: work/cancellation/results і freshness semantics явні | AC-SEARCH-004: exact bounds зберігають result, excess budget/cancellation дає typed failure з healthy following call; source revision перевірена; derived watermark/generation не advertised до реалізації | Existing [ReadExecutionTests](../../tests/KeyLoad.UnitTests/Features/ResourceExecution/ReadExecutionTests.cs) `TextAndVectorBranchesShareOneReadByteBudget`, `SearchAndGraphResultBudgetsIncludeProtocolMetadata`; REQ-SR-003/005 і AC-MP preserved |
| REQ-SEARCH-005: provider/index lifecycle не змінює canonical authority | AC-SEARCH-005: PLANNED provider audit/rebuild/generation-swap/recovery tests доводять declared tokenizer/BM25 statistics/privacy/freshness; missing/incompatible projection дає declared fallback/error, не silently weaker result | PLANNED KL-028/029/031/039/067/097 suites; [ADR-009](../ADR/ADR-009-search-provider-boundaries.md), [ChangeFeeds](ChangeFeeds.md) |
| REQ-SEARCH-006: ANN/global ranking/graph retrieval проходять окремі correctness-quality-resource gates | AC-SEARCH-006: PLANNED exact oracle і quality corpus перевіряють recall, ties, filter/row scope, global candidate completeness, versioned rank and recovery; unavailable capability explicit unsupported | PLANNED KL-030/032/055–060/074, [ADR-018](../ADR/ADR-018-global-rank-fusion.md), [ADR-019](../ADR/ADR-019-managed-ann.md); алгоритм/provider остаточно не обрано |

Target map: Abstractions/Core/Query/Client/Server/tests `Features/Search/`; shared adapters — ClientApi, provider/codec/host integration — один lead. Frontend N/A, окремий UI не запитано. PII lineage — [ADR-015](../ADR/ADR-015-sensitive-data-lineage.md), budgets/security — [ADR-010](../ADR/ADR-010-query-budgets-security.md). Existing flat SearchEngine та mixed GraphAndSeries лишаються ADR-032 migration debt; new ownership не робить legacy layout compliant.

Усі current test methods — source coverage, не passing run. Provider-backed BM25, qualified managed ANN, online generation swap, full freshness barriers, graph-scoped/global distributed retrieval залишаються planned/in-progress; proposed choices не реалізуються до contract acceptance. Product qualification тільки exact GitHub real TUnit/recovery/Docker RF3 SDK/MCP та actual JSON benchmarks, без invented speed/quality numbers.
