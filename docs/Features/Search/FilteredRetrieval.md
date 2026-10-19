# FilteredRetrieval within Search

Decision: [ADR-087](../../ADR/ADR-087-filtered-retrieval.md). Filtered retrieval has three layers: the public allowlist, the computational adaptive planner and the declared public approximation.

## Public allowlist and acceptance

`SearchRequest.AllowedIds`, field11, is an optional immutable array of
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
| REQ-FILTER-004: adaptive ANN uses the entire admissible graph | AC-FILTER-004: internal planner uses full-corpus ordinal eligibility, exact small-set, bounded expansion and fully charged fallback; 100/10/1/0.1 percent and correlated cohorts reach recall >=0.95 or fail explicitly. `AdaptiveFilteredPlannerTests`; adaptive planner below |
| REQ-FILTER-005: public approximation and cluster are separately qualified | AC-FILTER-005: the public approximation proves declared versioned completeness/mode/cost metadata and actual .NET/official MCP cluster results after faults. Planned `FilteredSearchClusterTests`; existing array replies remain complete exact results in the public allowlist |

## Stages

1. The public allowlist appends the shared SearchRequest field with JSON/ingress; no older node may silently ignore a filter. SearchEngine, VectorRanker and the FilteredSearch/FilteredVectorPlanner helpers apply eligibility before branch ranks and fusion; lexical corpus statistics keep using the authorized corpus, not a caller-controlled IDF corpus, and the canonical visible-vector/document checks and rank/fusion contracts are reused.
2. The adaptive planner layer adds the computational adaptive planner over the existing packed ANN candidate with actual charged eligibility/cardinality/expansion/fallback counters. A graph is never rebuilt from the output-filtered set and complete public exact rankings are not silently replaced by an ANN/windowed reply.
3. The public approximation depends on ADR-019's accepted durable generation/replay and explicit public approximation contract; the allowlist and planner do not close AC-ANN-007/008.

The SDK and MCP transports reuse the existing typed search body; interactive UI is N/A. No canonical stored record migration occurs in the public allowlist, the adaptive planner is computational, and older nodes reject the filtered capability rather than dropping IDs.

## Public allowlist cluster tests

The public tests (`FilteredSearchClusterTests`) map AC-FILTER-001/002/003 and the public-allowlist portion of AC-FILTER-005 to the Aspire replicated cluster with the real .NET SDK and official MCP search calls, independent exact eligible result expectations, empty/duplicate/unknown IDs, current write/delete and persisted policy denial, with unique resources, bounded cancellation and cleanup. No approximation or adaptive-planner qualification follows from these cases.

## Computational planner contract

The adaptive planner implements AC-FILTER-004 within the already real packed ANN executor. It adds
no public approximate reply or persisted format. The full admitted graph remains
the search graph; output eligibility never removes intermediate vertices. The
caller supplies the existing canonical full-corpus ordinal bitmap, whose exact
word count and unused trailing bits are validated before execution. Copy it once
into query-owned bounded scratch and charge every word/popcount operation to the
same AnnWorkBudget and ReadExecutionBudget; null is the whole corpus and an
empty eligible set returns zero candidates after admission.

`FilteredVectorPlanner` creates the actual execution plan consumed by
PackedAnnSearch: eligible count, result count, small-set exact choice and initial
level-zero breadth. For the approximate path, breadth starts at the larger of
EfSearch and `ceil(resultCount * corpusCount / eligibleCount)`, capped by the
existing `min(corpusCount, 4096)` capacity. Use checked long arithmetic and named
constants. Retry by bounded doubling only when too few eligible candidates were
found. Reuse the full graph; when the breadth cap is reached with insufficient
eligible candidates, execute the fully charged exact fallback. Small eligible
sets use exact search directly and enumerate actual bitmap set bits, charging
word/ordinal work without scoring ineligible vectors. No budgets reset across
planning, graph expansion or fallback, and no insufficient/partial exact result
becomes success.

Keep the existing AnnSearchResult constructor and mode meanings. Add in-memory
init metrics EligibleCount, ExpansionPasses and FallbackDistanceEvaluations;
ExpansionPasses counts actual level-zero SearchLayer calls, while fallback
distances count only the exact fallback phase. Existing total work/distance/edge
metrics and scratch reservations remain authoritative. These are computational
values, not persistence or inter-grain contracts. Public Search remains the public allowlist
complete exact array; the public approximation still requires its separate accepted approximation,
generation and cluster contract before exposure.

Map AC-FILTER-004 to deterministic 100/10/1/0.1-percent eligibility cohorts,
including eligibility correlated with nearest clusters and isolated endpoint
selection reached through ineligible intermediates. Compare IDs/scores against
an independent exhaustive scalar oracle, assert recall at least0.95 on the stated
cohorts, exact modes at1.0, deterministic ties and no ineligible hit. Exercise
small/zero sets, malformed bitmap, overflow-safe bounds, expansion/fallback
metrics, original scratch caps, work exhaustion/cancellation and a subsequent
healthy query. Use actual packed indexes and existing real ZoneTree seed helpers,
not a fake graph or authorization policy. Local quality fixtures are development
evidence; the required scale/performance and public approximation gates remain separate.

```mermaid
flowchart LR
    Request[Bounded allowlist] --> Cut[Persisted policy and canonical read cut]
    Cut --> Corpus[Authorized corpus and statistics]
    Corpus --> Eligible[Output eligibility]
    Eligible --> Exact[Complete exact branch rankings]
    Exact --> Fusion[Unchanged weighted fusion]
    Fusion --> Reply[Authorized complete result or typed failure]
```


## Retained policy refusal and same-root recovery

REQ-FILTER-POLICY-COLD-001 refines REQ-FILTER-002/003: an empty output
allowlist never bypasses persisted capability, field-use or revocation checks.
AC-FILTER-POLICY-COLD-001 maps to the existing
`FilteredSearchPolicyTests.EmptyAllowlistStillEnforcesPersistedCapabilitiesFieldUseAndRevocation`
identity, without changing its arguments or original three precise denials.
Capture the complete bounded canonical key/value inventory and commit position
under the actual store read gate before refusal; verify exact unchanged bytes
and position after each refusal. Repair the reader through the existing real
ConfigurePrincipal operation, retain the separately revoked principal, and
verify an independently literal complete eligible vector result.

Join the original owner before each of two same-root cold opens. Reuse
its validated storage options and actual configured DatabaseEngine factory;
require unchanged node/incarnation and complete canonical inventory. On each
opened owner, the still-revoked principal must remain Unauthenticated, the
repaired reader must return the literal authorized document/reference/revision,
score, unredacted body, empty redaction fields and absent explanation, and
queries must preserve every canonical byte and position. The oracle scan cap
is test-local and must reject HasMore rather than accepting a partial image.


## Independent full fallback after refusal

REQ-FILTER-FALLBACK-ORACLE-001 refines REQ-FILTER-003/004: the exact fallback
result after an exhausted adaptive traversal is compared with an independent
exhaustive eligible ranking, never solely with an earlier executor return.
AC-FILTER-FALLBACK-ORACLE-001 maps to the unchanged original
`AdaptiveFilteredPlannerTests.AcFilter004InsufficientAdaptiveCandidatesUseChargedExactFallbackThenHealthySearch`.
Keep its actual 100 bottom-ranked eligible records, real 10,000-record execution
seed, original index options, original budgets and N-1 typed BudgetExceeded.
The original measured and subsequent healthy result must each equal the
independent complete eligible order and every candidate ID, revision and score;
retain mode/expansion/fallback/total distance and work assertions. Capture the
original position and complete authorized canonical vector model before
querying and compare after refusal and healthy continuation. No query effect
or incomplete exact result is accepted.

REQ-FILTER-PUBLIC-SPARSE-001 refines REQ-FILTER-001/003/004: the admitted
public ANN operation must deduplicate an allowlist, ignore unknown IDs and
exclude every disallowed navigation vertex before rank fusion. The
original eligible result capacity is bounded by actual admitted cardinality.
The page must retain its requested mode, actual execution mode, completeness,
generation and same authorized cut; filtered reads cannot alter canonical
records, files or the store position.

AC-FILTER-PUBLIC-SPARSE-001 maps to
AnnPublicWholeFlowTests.ActualProvisionedFilteredSearchDeduplicatesUnknownIdsWithoutReturningNavigationNodesAndRemainsHealthy:
the existing genuine three-row publication receives the literal
allowlist seed-00000, missing-ann-id, seed-00000. Both actual full-breadth
Approximate and insufficient-candidate ExactFallback paths return exactly the
independently expected seed-00000 reference, revision1, original document body,
redaction metadata and rank1 contribution1/61. The repeated filtered request
and subsequent original complete unfiltered page remain healthy; complete
canonical rows, position and owned file evidence remain unchanged. Existing
fixture admission, publication, producer/lease cleanup and failure
aggregation remain intact.

This is a three-row functional control through SearchEngine.ApproximateSearchAsync,
not cluster, actual100k/1M, public SDK/MCP interoperability, recall or performance
qualification. The complete original cases remain selected. No product API,
schema, quota, index algorithm or source mutation is introduced by this test.
