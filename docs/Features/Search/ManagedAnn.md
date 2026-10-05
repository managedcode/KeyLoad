# Managed ANN candidate

Status: R1 contract accepted by root under the owner's 104-task implementation scope, 2026-10-04. R1 source and independent tests are implemented and reviewed; strict full Release build16 passes. Focused real-store normal06 and scalar07 each pass19/19 with unchanged whole source/runtime inventories and all15 recall cells>=0.988. Full-suite/delivered-source qualification and R2/R3 implementation remain pending. Canonical slice: Search. Decision: [ADR-019](../../ADR/ADR-019-managed-ann.md); parent [Search](../Search.md). Maps REQ/AC-SEARCH-001/003/004/005/006 to KL-030/031/032/059/060.

R1 is an independently authored immutable packed managed HNSW computational candidate. It consumes an owned bounded snapshot of actual canonical VectorRecord values and returns owned internal candidates. It is not enabled in SearchEngine, SQL, HTTP, SDK or MCP. Canonical ZoneTree data, native WAL, persisted authorization, one-grain-per-request, RF3 and exact branch/fusion semantics remain authoritative.

```mermaid
flowchart LR
    Store[Canonical ZoneTree vectors] --> Owned[Owned bounded source snapshot]
    Owned --> Builder[Private budgeted packed HNSW build]
    Builder --> Index[Immutable candidate generation]
    Index --> Query[Budgeted query and current eligibility]
    Query --> Candidates[Owned candidates and explicit internal mode]
    Exact[Independent exact metric oracle] --> Quality[Recall and resource gates]
    Candidates --> Quality
    Quality --> Later[Separate source-cut replay and public capability contracts]
```

## Requirements and acceptance

| Requirement | Acceptance: measurable pass/fail | Automated mapping |
|---|---|---|
| REQ-ANN-001: packed finite owned state with pre-allocation admission | AC-ANN-001: small actual-store corpus builds within a 32 KiB index cap; copied vectors use dimension-aligned blocks at most 64 KiB, no mandatory 256 MiB chunk; count/byte/scratch/array arithmetic is checked before allocation. Invalid options, dimensions, metric, identity, order, duplicates, revision, field/space or nonfinite input yield typed Validation. Excess positive resource limits yield BudgetExceeded. | PackedAnnValidationTests, PackedAnnReservationTests; actual ZoneTree vectors |
| REQ-ANN-002: scores preserve the existing exact metric semantics | AC-ANN-002: every returned score equals SearchEngine.Similarity in the same invocation mode for Cosine, Euclidean and DotProduct, including zero, ordinary, subnormal, large finite values and dimensions 1/4096. Invalid query is rejected even for an empty index or eligibility set. Normal and hardware-disabled reports are retained separately. | PackedAnnMetricTests; independent public oracle |
| REQ-ANN-003: finite work, memory, deadline and cancellation without partial success | AC-ANN-003: exact/excess work and scratch boundaries, already-cancelled and genuinely in-progress cancellation/deadline, immutable concurrent queries and a healthy following call are observed. Failure returns no candidate result and cannot mutate a prior successful generation. Counters report actual work and per-call deltas. | PackedAnnBudgetTests, PackedAnnOwnershipTests; real ReadExecutionBudget and cancellation |
| REQ-ANN-004: eligibility constrains output without hiding navigation nodes | AC-ANN-004: disallowed nodes remain traversable but never appear in output. Bitmap length/outside bits are validated. Small eligible sets use exact search; insufficient admitted approximate candidates trigger charged complete eligible exact fallback or typed failure. Result mode is truthful. Filter selectivity/correlation and deterministic score/ordinal-ID ties are covered. | PackedAnnFilterTests; actual persisted-policy canonical corpus |
| REQ-ANN-005: real-corpus quality must precede admission | AC-ANN-005: at least 10,000 actually loaded canonical records and 100 independent queries per metric/selectivity/correlation cell are compared with complete eligible exact Top10. Recall@10 is at least 0.95 in every cell; a failing cell blocks admission rather than being averaged away. Include all/10%/1%/0.1% eligibility and geometry-correlated eligibility, seed, loaded counts, options and actual mode. | PackedAnnRecallTests and original TUnit artifacts; development controls, not global performance qualification |
| REQ-ANN-006: rebuilding reflects canonical mutation and owns retained bytes | AC-ANN-006: actual ZoneTree vector-only update at unchanged DocumentRevision, document update/stale vector, deletion and reinsertion change the rebuilt corpus appropriately. Mutation of input buffers/list after Build cannot change a successful index; failed rebuild leaves the prior index queryable and identical. | PackedAnnRebuildTests, PackedAnnOwnershipTests |
| REQ-ANN-007: persisted projection has a source-cut and bounded replay/publication contract | AC-ANN-007: R2 must prove pinned snapshot plus ordered outbox tail, same-revision vector update, duplicate replay, deletion/reinsert, obsolete generations, atomic publish, corruption, locks/leases/retention and actual crash/restart boundaries against real ZoneTree. Pending separate root format/lifecycle contract; no implementation exception. | Future Search projection unit/process-recovery suites; KL-031/039/097 |
| REQ-ANN-008: public approximation and freshness are explicit and authorized | AC-ANN-008: R3 must prove versioned exact/approximate/fallback/completeness metadata through SQL, .NET and official MCP; current row/field grant and policy epoch in one read cut; genuine RF3 migration, follower loss/catch-up, revocation and no stale/hidden candidate payload. Pending separate root public contract; no implementation exception. | Future Query/Search/ClientApi and Docker/Aspire RF3 suites; KL-032/059/060 |

AC-ANN-001–006 must pass before R1 is described as qualified. R1 cannot close AC-SEARCH-005/006 or AC-ANN-007/008. No fabricated fixtures, fakes, assertion relaxation, skips or tolerance-based score substitution are permitted; actual TestDatabase fixtures use genuine canonical ZoneTree state and persisted policy. Recall is the declared quality threshold; score equality uses the exact current public oracle in the same scalar/intrinsic mode.

## Frozen R1 internal API

Namespace: KeyLoad.Query.Features.Search. These computational objects are not persisted or passed between grains; no serialization DTO or alias is introduced in R1.

```csharp
internal sealed record PackedAnnOptions; // init properties and defaults below
internal sealed class AnnWorkBudget {
    internal AnnWorkBudget(ReadExecutionBudget readBudget, long maxWorkUnits);
    internal long WorkUnits { get; }
    internal long DistanceEvaluations { get; }
    internal long EdgeVisits { get; }
    internal void Check();
    internal void Charge(long units);
    internal void ChargeDistance(int dimension);
    internal void ChargeEdge();
}
internal enum AnnSearchMode { ExactSmallSet, Approximate, ExactAfterInsufficientCandidates }
internal readonly record struct AnnCandidate(int SourceOrdinal, string DocumentId,
    long DocumentRevision, double Score);
internal sealed record AnnSearchResult(AnnCandidate[] Candidates, AnnSearchMode Mode,
    long WorkUnits, long DistanceEvaluations, long EdgeVisits);
internal sealed class PackedAnnIndex {
    internal static PackedAnnIndex Build(VectorSpace space, IReadOnlyList<VectorRecord> records,
        PackedAnnOptions options, AnnWorkBudget budget);
    internal VectorSpace Space { get; }
    internal int Count { get; }
    internal long RetainedBytesUpperBound { get; }
    internal long BuildScratchBytesUpperBound { get; }
    internal AnnSearchResult Search(ReadOnlyMemory<float> query, int limit,
        ReadOnlyMemory<ulong>? eligibility, AnnWorkBudget budget);
}
```

PackedAnnOptions properties: Connections=16 (4/8/16/32/64 only), EfConstruction=128 (Connections..4096), EfSearch=128 (1..4096), MaxLevel=16 (1..16), ExactThreshold=256 (0..4096), MaxRecords=5,000,000 (1..5,000,000), MaxIndexBytes=268,435,456 (1024..8,589,934,592), MaxScratchBytes=8,388,608 (1024..268,435,456), Seed=0x4B45594C4F414431UL. These are finite internal candidate caps, not changes to public DatabaseLimits. Numeric products, array lengths and sum/growth arithmetic must be checked before allocation; oversized input must not first allocate and then fail.

Input identities are nonempty canonical JsonData.Identifier values, unique and strictly StringComparer.Ordinal ordered. All records have one field, the full supplied VectorSpace, positive DocumentRevision and finite vectors of its dimension 1..4096. Validate full space identifiers/metric/dimension and input even when empty. Reject unordered/duplicate input rather than sort it inside an unbudgeted dictionary. The later canonical collector owns its bounded charged sort. Build copies vectors into dimension-aligned blocks no larger than 64 KiB (the last block uses its actual size), keeps only identity/revision metadata and packed primitive graph arrays, and retains no caller list, full DocumentRecord, borrowed view, database handle or source vector array. RetainedBytesUpperBound conservatively includes managed headers/arrays and retained UTF-16 strings; this formula is not measured RSS.

Levels use deterministic SplitMix64(Seed plus sorted source ordinal), a geometric 1/Connections promotion rate and MaxLevel cap. HNSW insertion uses a greedy upper walk, bounded layer search, base maximum degree 2*Connections and upper maximum degree Connections. Cosine/Euclidean use diversified neighbors with a fill-from-pruned phase; DotProduct uses exact nearest-neighbor selection for both new edges and reciprocal overflow under the accepted refinement below. No clock/global Random or imported source is used. Build has one private writer and publishes only on complete success. An index is immutable and supports concurrent searches; each search owns its scratch. Updates/deletes produce a newly built successful generation in R1 rather than mutate the old one.

AnnWorkBudget wraps the actual ReadExecutionBudget.Check, so real cancellation and elapsed deadline propagate. Work units are one examined input component/metadata character, one traversed graph edge, and one component of a distance evaluation; finite validation and the existing metric reduction are included in that bounded distance quantum. ChargeDistance increments actual DistanceEvaluations and charges dimension units before scoring. ChargeEdge increments EdgeVisits and charges one unit before examination. Heap/neighbor/level/visited/validation/copy loops charge bounded work and check cancellation; no distance quantum exceeds 4096 dimensions. Counter/growth arithmetic is checked; nonpositive maximum or negative charge is typed Validation, excess is BudgetExceeded. Budget has one execution writer; Volatile-readable counters permit bounded real cancellation synchronization. Search returns deltas from that call, not lifetime/build counters.

Search validates and privately copies the query before using unchanged PreparedSimilarity for every score. Limit is 1..1000, output sorts descending score then ordinal DocumentId. Eligibility null means all nodes; otherwise it is exactly ceil(Count/64) ulong words with zero bits outside Count, copied and charged before traversal. It is an internal eligibility set, not caller-supplied trusted authorization. Navigation may traverse every node; output eligibility is always enforced.

AnnSearchResult additionally has the internal init-only property `long ScratchBytesUpperBound`, populated by every successful mode. This reports the admitted logical upper reservation, not measured allocation/RSS or another wire API. BuildScratchBytesUpperBound reports the corresponding successful build reservation. The host needs these resource facts for later aggregate admission; tests can exercise exact/excess configured byte caps from reported reservations without guessing private formulas.

Empty/small eligible sets use exact scoring. Larger sets use ef at least max(Limit, EfSearch), with bounded doubling through at most min(Count,4096). If fewer than min(Limit,eligibleCount) admitted candidates are found, the same call executes fully charged exact eligible fallback or fails, never partial success. Modes distinguish small exact, approximate and insufficient-candidate exact fallback. Actual heap/bitset/query/eligibility/result capacities, temporary construction state and simultaneous old/new buffers during growth all fit MaxScratchBytes before allocation; no unbounded BCL queue or retained query pool is allowed. All result IDs/revisions are canonical owned metadata; document revision alone is not a freshness marker.

## Ordered implementation and join

| Stage / owner | Exact file ownership and permissions | Dependencies, artifacts, verification and join |
|---|---|---|
| R1 algorithm / query_wave, gpt-6-luna high | NEW src/KeyLoad.Query/Features/Search/PackedAnn*.cs and AnnWorkBudget.cs only; no shared/public/Core/Server/package/project/docs/CI/test edits | Accepted API above; actual complete algorithm/bounds; static inspection only in worker. Root checks diff, numeric complexity, strict build, formatter and independent tests. |
| R1 independent tests / cluster_wave, gpt-6-luna high | NEW tests/KeyLoad.UnitTests/Features/Search/PackedAnn*.cs only; no existing tests/helpers/contracts edited | Accepted API; real TestDatabase/canonical ZoneTree and existing public exact metric oracle. Author positive/negative/edge/error cases for AC-ANN-001–006; no runtime/build/commit in worker. |
| R2 assessment / lifecycle_wave, gpt-6-luna high | Read-only existing Core/Server/Search/outbox/ZoneTree lifecycle sources; no writes or execution | Report exact source-cut, replay, publication, serialization, lock and memory join points/gaps. No invented format/contract implementation. |
| Root integration and evidence | ADR/spec/task graph/shared joins; exclusive build/test/commit ownership | Inspect every diff, resolve API/bounds issues, run strict solution build/format/governance then TUnit through Aspire normal/scalar; retain original evidence. Commit completed stage, push main, qualify delivered SHA on Linux. |

Workers stop and escalate on missing contracts, required shared changes or an unmet bound; they cannot expand scope, invent public/persistence contracts or weaken existing tests/rules. File/type/method/nesting limits remain enforced. No worker starts a competing build, formatter, test, commit or source cleanup.

R2/R3 must freeze their complete REQ/AC/ADR implementation contract before write-capable delegation. Their source inventory, admission and generation lifetime must preserve canonical source-cut identity, PutVector updates with the same DocumentRevision, outbox order/checkpoints, node identity/incarnation/data epoch and authorized policy/schema/read generation. Borrowed views cannot escape a read cut; open storage ownership cannot migrate with Orleans activations. Later persisted KeyLoad-owned typed payloads require native generated Orleans binary serialization with stable Alias/Id and an explicit format-upgrade contract.

## Slice surfaces, delivery and rollback

Query/Search owns R1 algorithm; UnitTests/Search owns independent actual-store tests. Core/Search and Server/Search are pending R2 canonical collector/outbox and node-local projection lifetime. Abstractions/Search, Query/QueryExecution, Client/Search, Server/ClientApi and SDK/MCP tests are pending R3 versioned public capability and diagnostics. Process recovery and IntegrationTests/Search qualify R2/R3. Frontend N/A: no UI is requested. Benchmarks/Search and genuine GitHub comparison artifacts are required R4 resource/performance evidence; the complete required record/operation counts and fair isolated topology contracts remain mandatory.

R1 changes no canonical key, identity digest, WAL, replication/atomic journal, persisted format, authorization grant or public response. Rollback removes the unused internal candidate. Later rollout exposes only an explicit qualified capability, with atomic derived generation publication and declared exact fallback/error. Disabling or rebuilding the disposable projection never loses acknowledged canonical state. Local 10,000-record quality controls, counters and builds cannot prove global performance, power-loss durability, endurance, coverage or production readiness.

## Accepted packing and reservation refinement, 2026-10-04

Root review found that allocating Count*MaxLevel*Connections upper slots retains mostly unused levels; at5,000,000 nodes/M16/MaxLevel16 that upper array alone requests5,120,000,000 bytes. This is an arithmetic observation, not measured performance. R1 must use one packed upper-offset array `int[Count+1]` and exactly `Connections*sum(actualLevel[i])` upper edge slots. Compute the deterministic level total in a charged preflight before graph allocation, then use the same levels for offsets and validate node/layer addresses. Base slots stay exactly Count*2*Connections. No rectangle of unowned upper levels or old alternative implementation remains.

Let A(width,length)=64+Align8(width*length), with checked arithmetic and prechecked CLR array length; Align8 rounds payload bytes upward to8. For retained metadata use an8-byte reference upper bound and S(text)=64+Align8(2*(text.Length+1)), including the string terminator. The fixed retained-object allowance is512 bytes, separate from every array header. RetainedBytesUpperBound is exactly512 plus A for IDs(8,N), revisions(8,N), levels(1,N), base edges(4,2MN), upper offsets(4,N+1), upper edges(4,M*sumLevel), vector-block references(8,blockCount), every actually sized float block(4,blockFloatCount), and S for every retained DocumentId plus the three VectorSpace strings. Count all extra retained fields/arrays if implementation introduces them; do not hide them in the fixed allowance. This conservative portable reservation is not exact managed heap size.

Build marks may use one reusable epoch `int[N]` to avoid clearing the corpus per insertion. Query marks use one `ulong[ceil(N/64)]` bitmap, with charged bounded clearing between probes, rather than a full int[N] per concurrent query. Layer candidates retain only fixed int[E], double[E], bool[E] capacities; no corpus-sized candidate queue is added. Build E=min(N,EfConstruction); approximate query E=min(N,4096). No per-ef array growth occurs. Intermediate query probes count eligible candidates in the layer buffers; allocate the final candidate array only after the successful probe or in complete exact fallback. Do not accumulate intermediate owned result arrays.

Use a1024-byte fixed scratch-object allowance, separate from each A array reservation. BuildScratchBytesUpperBound=1024+A(4,N)+A(4,E)+A(8,E)+A(1,E)+A(4,d+1)+A(8,d+1)+A(4,d)+A(4,d), where d=0 for empty source and otherwise2*Connections. Source/index-owned arrays are counted in the independent retained reservation, never charged a second time as transient scratch. Query initial reservation is1024+A(4,Dimension) plus A(8,bitmapWords) only when eligibility is supplied. Copy/validate/count the bitmap under that reservation, then admit actual K=min(Limit,eligibleCount). Exact scratch adds A(32,K) for the owned AnnCandidate array. Approximate scratch additionally adds A(8,ceil(N/64))+A(4,E)+A(8,E)+A(1,E), retaining that reservation through any exact fallback until settlement. AnnCandidate has a conservative32-byte element allowance under the current four-field contract. A changed struct/layout or additional allocated array requires updating the reservation before allocation.

Every positive byte cap is inclusive: reported reservation equal to the configured cap passes; one byte below fails BudgetExceeded before the corresponding allocation. Empty/sparse eligibility cannot be rejected merely for min(Limit,Count) result slots that will not be allocated. Queries still validate privately copied values even for no eligible records. AC-ANN-001/003 tests derive exact/excess caps from the first successful reported reservation, and cover positive sparse bitmap admission; they do not assert a guessed private layout or physical-memory equality. Root owns the shared contract, query_wave owns the same15 new algorithm files and any new PackedAnn* helper needed for visits, cluster_wave owns only the same new PackedAnn* tests. All strict build/Aspire/Linux gates remain pending.

## Root traversal and real-budget review, 2026-10-04

The layer candidate capacity `ef` bounds retained candidates, not the number of expansions. Continue expanding the best unprocessed retained candidate until none remains; a replacement is unprocessed and every node is marked before scoring, so an admitted pass expands at most Count unique nodes. The actual work budget, deadline and cancellation bound traversal. Stopping after exactly ef expansions can miss later improving candidates and is not the accepted HNSW termination contract. Preserve the fixed arrays, deterministic ordering and all existing quality thresholds when correcting this loop.

AC-ANN-003 must exercise the actual ReadExecutionBudget with TimeProvider.System and genuine CancellationTokenSource; a TimeProvider subclass that changes time or cancels on its own timestamp call is a fake and is prohibited. Use the actual persisted 10,000-record corpus with a wide ef=4096 search, observe positive real EdgeVisits before cancellation from a separately scheduled bounded observer, and settle both tasks on every path. A real one-second request deadline must interrupt charged work after traversal starts, return BudgetExceeded without a result, and leave the previously completed index usable. Building the healthy index uses its ordinary independent build budget, not the deliberately expiring request budget. No retry, delayed production hook, fake clock, partial result, or relaxed edge/deadline assertion may hide a failure; if the actual workload cannot exercise the declared boundary, report it to root for a concrete workload correction before further execution.

AC-ANN-005 counters describe the selected mode. ExactSmallSet traverses no graph edges and must report zero EdgeVisits. Approximate or exact-after-insufficient-candidates modes must report actual positive graph traversal. Every cell still requires positive work/distance counters, its own Recall@10 >=0.95 over 100 independent queries, canonical result/score identity and the declared source/options. This corrects an invalid test expectation that every exact cell traverses edges; it does not waive a quality or resource criterion. Root owns this contract and join record; query_wave corrects only its new algorithm files, cluster_wave corrects only its new tests. No runtime gate starts before both scopes freeze again.

## Accepted insertion-work correction, 2026-10-04

The first actual Aspire run passed17/19 cases; both10,000-record cases exceeded their unchanged1,000,000,000-unit build budget before quality or cancellation assertions. Source review found avoidable reciprocal reselection and confused insertion degree with maximum retained degree. [HNSW Algorithm1](https://arxiv.org/pdf/1603.09320) selects M new neighbors, and prunes a target only after its adjacency exceeds its per-layer maximum. Use Connections new neighbors at every inserted layer; retain base2M/upperM capacity. If a reciprocal edge fits, preserve existing edges and insert the new ordinal in bounded sorted order without scoring/reselecting them. On overflow, retain the existing diversified selection over the complete old-neighbors-plus-new-source set. All copies, comparisons and graph writes remain charged and cancellable.

Build validates unique strict ordinal-ID ordering. Therefore source ordinal comparison gives exactly the same ID tie and adjacency order without rescanning retained strings. Charge the actual integer comparison; validate the copied owned ID ordering before graph construction. Remove unused ID parameters from private traversal/selection helpers, retain copied IDs for output, and preserve exact public scores, deterministic levels, packed capacities and reservation formulas. This changes actual work, not its limits or acceptance thresholds. Root owns the contract; query_wave edits only its new algorithm files. Existing independent tests remain unchanged, and both failed original reports remain retained. No performance or recall pass is inferred from this correction.

## Accepted worst-slot cache refinement, 2026-10-04

The second real Aspire run again passed17/19 and exceeded the same build work cap; both original stacks now identify FindWorst. Add one operation-local integer WorstSlot to the existing layer buffers. Reset it to zero at the start of every pass. During initial append, update it with one charged comparison against the prior worst. At capacity, compare the incoming candidate against the cached worst before scanning: a rejected candidate changes nothing and needs no full scan. After an accepted replacement, recompute the exact worst across the retained slots with the existing charged comparator. Cache validity must hold for every active slot count and reset before any subsequent pass; it never becomes authoritative graph state.

The fixed1024-byte scratch-object reservation includes this additional integer; every admitted array and retained-index reservation stays unchanged. No extra candidate array, queue, dictionary, score tolerance, ef reduction, work-cap increase or acceptance waiver is introduced. All comparisons and scan iterations remain charged and cancellable. Root owns this contract; query_wave changes only its new PackedAnnLayerBuffers/LayerSearch files. TASK-ANN-R1-ROOT-JOIN must retain both failed cohorts, review cache invariants, rebuild, format and repeat the unchanged genuine-store normal/scalar tests before qualification.

## Accepted bounded dual-heap refinement, 2026-10-04

The third genuine Aspire cohort still passes17/19; both10,000-record builds exceed the unchanged1,000,000,000 work cap in reciprocal-link work before recall or deadline assertions. The cache is correct but does not remove every repeated scan. Source review also establishes a full retained-slot scan for every next-candidate extraction, and for every accepted worst replacement. Root selects the primary paper's two-priority-queue search representation before changing neighbor-selection semantics. This is an exact search-order implementation refinement, not evidence that reciprocal diversification now meets its budget.

Replace the operation-local Processed bool array and WorstSlot cache with a fixed best-candidate heap and a fixed retained-worst heap. Retained slots still use int[E] Nodes and double[E] Scores. The best heap owns int[E] slot indices and int[E] inverse positions; position−1 means that the retained slot has no unprocessed frontier entry. The worst heap owns int[E] slot indices and needs no inverse map because only its root is replaced. Both heaps have finite counts no greater than the retained count, which is at most the admitted ef and E. No lazy stale-entry accumulation, corpus-sized frontier, BCL priority queue, result duplication or per-probe allocation is allowed.

At each pass reset both heap counts and charge/initialize every best inverse position to−1 before seeding slot0. Append a newly retained slot to both heaps. Pop the exact best unprocessed slot from the best heap while leaving it in the retained-worst heap. At capacity compare the incoming score/ordinal with the worst root; rejection mutates nothing. On acceptance, remove any unprocessed entry for that retained slot through its inverse position, replace its node/score, sift the changed worst root downward, and insert the new unprocessed entry. Lower ordinal remains the exact tie-breaker for equal scores. Every sift, removal, comparison, initialization and copy remains charged/cancellable. Final output sorting occurs only after traversal; heaps are not consulted after slots are sorted, and the next pass resets them. Remove the superseded Processed/cache/full-scan helpers immediately.

This supersedes the earlier layer-array reservation only: the current layer arrays are four int[E] arrays (Nodes, best slots, best positions, worst slots) and one double[E] array. With the existing A formula, current BuildScratchBytesUpperBound is1024+A(4,N)+4*A(4,E)+A(8,E)+A(4,d+1)+A(8,d+1)+2*A(4,d). Current approximate query scratch is the admitted exact scratch plus A(8,ceil(N/64))+4*A(4,E)+A(8,E). E, d, query copy/eligibility, K, result reservation, retained-index formula, finite byte/work caps and inclusive boundary semantics stay as defined above. The fixed1024-byte object allowance includes the bounded heap-control objects/counts; no heap array is hidden in it. Admission must use this formula before any added array is allocated.

Root owns this decision and reviews exact heap/map/reset invariants. query_wave may edit only its new PackedAnn algorithm files and add cohesive PackedAnn heap helpers. Independent tests, fixtures, default options, counters, score oracle, neighbor diversification and all AC thresholds remain unchanged. Strict full build/formatter/governance and genuine Aspire normal/scalar reports are required again. If reciprocal work still fails, retain that result and decide the next correction from evidence; never silently increase caps or claim recall.

## Accepted DotProduct neighbor-selection refinement, 2026-10-04

The fourth original Aspire report records all five Cosine and all five Euclidean cells on10,000 actual rows/100 independent queries each: per-cell recall0.998–1.0 with the required truthful modes. DotProduct still exceeds the same1,000,000,000 build work cap in reciprocal-link work; no DotProduct quality or in-progress deadline acceptance is reached. The whole report remains17/19. The complete source guard fails because the concurrent benchmark chat changes its own files; separately retained inventories show unchanged ANN source and every runtime binary. These are actual local observations, not complete R1 or exact-source Linux qualification.

Raw DotProduct is a similarity, not a metric distance. Root chooses the paper-defined SELECT-NEIGHBORS-SIMPLE variant for the DotProduct graph: take the highest exact source score candidates, with the existing lower-ordinal tie order, for both Connections new edges and degree-limited reciprocal overflow. Do not run candidate-to-selected diversity distance evaluations for this metric. Cosine/Euclidean retain their current diversification. This is a declared candidate-quality tradeoff; simple selection may lose useful bridges, so the same five independent DotProduct cells must still achieve recall≥0.95 before admission. No assumption that it will pass is permitted.

Use the existing already-ranked bounded candidate arrays and charged unique/self exclusion, copy and ordinal adjacency sort. Candidate degree, heaps, native exact scoring, levels, retained/scratch arrays and reservations, defaults, work/deadline caps, filters, modes and result order remain unchanged. No normalization, vector lifting, new retained norm array, metric substitution or altered oracle. query_wave changes only its new neighbor-selection source; root owns the ADR/task join and independently reviews both selection paths. Retain all four original cohorts and repeat full strict/static gates and unchanged real-store Aspire normal/scalar acceptance tests.

## Accepted real shared-request deadline regression, 2026-10-04

The fifth original Aspire cohort passes18/19 tests, including all15 independent10,000-row/100-query recall cells at0.988–1.0. Its remaining deadline test assumes one wide search must take more than one second; the optimized real search completes earlier. That observation does not waive AC-ANN-003 or justify slowing the product. Retain the failed original report and the unchanged ANN/runtime inventory; the separate benchmark documentation change still means the whole shared-checkout source guard failed.

The regression must execute repeated real wide searches against one actual shared ReadExecutionBudget and AnnWorkBudget with a one-second TimeProvider.System deadline. Each successful call retains the existing charged execution and per-call deltas. Immediately before each invocation capture the cumulative EdgeVisits; the invocation interrupted by the deadline must add positive edge visits and return no AnnSearchResult. Prior completed calls cannot substitute for evidence that the failing invocation traversed the graph. No delay, fake clock, inflated work cap, relaxed assertion or product slowdown is allowed. Retain the separate real in-progress cancellation case and the healthy following search.

Root owns the contract and runtime evidence. cluster_wave may change only its new PackedAnnWideSearchBoundary test helper and add a cohesive new PackedAnn deadline helper if required by the existing method/type limits; it may not change product sources, budgets, defaults, fixtures, existing shared helpers, packages, docs or Git. Bound and join the actual worker and cleanup without hiding unexpected failures. Root reviews the implementation, rebuilds and repeats the real Aspire normal/scalar gates before qualification.

## Accepted packed-owned validation refinement, 2026-10-04

Unit44b completes3197/3204 cases. Four unchanged10,000-row ANN builds exceed
the ordinary30-second read deadline before recall/filter/deadline assertions.
The source repeats finite-component validation on vectors already copied and
checked into private packed blocks. Root accepts TASK-ANN-R1-OWNED-SCORES:
retain every untrusted Create/Score validation and the identical metric formulas,
but permit internal CreatePacked/ScorePacked only with an actual PackedAnnVectors
carrier and ordinal. Make its block constructor private so Copy remains the sole
finite-checked ownership factory. Keep dimension/ordinal/metric checks, original
budget/deadline/cancellation charges and all existing options/corpus/oracles.
No retained norm array, normalization, approximation or public bypass is added.

REQ-ANN-009 / AC-ANN-009: real-store copied packed vectors score exactly like
the existing untrusted/public metric oracle for all three metrics, ordinary,
zero, subnormal, large finite values and dimensions1/4096; NaN/infinite or invalid
untrusted input and invalid copy/metric/dimension remain typed rejection.
Mutation of original vectors after Copy cannot change packed scores. New
PackedAnnOwnedSimilarityTests map this criterion; existing metric/ownership and
all four failed10,000-row tests remain unchanged and mandatory.

The DotProduct candidate lists are unique: SearchLayer marks each candidate
visited before admission, and reciprocal overflow verifies the added source is
absent from existing unique adjacency. A dedicated simple-selection path may
copy the already-ranked nonself candidates up to degree, then perform the
unchanged ordinal adjacency sort, without repeated membership scans. Charge
each actual examination/copy/sort and keep source-score/lower-ordinal ties exact.
Cosine/Euclidean diversification stays unchanged. Real graph/recall tests must
prove unique bounded adjacency and unchanged quality; a source argument alone
does not establish performance or deadline success.

Root owns this contract, ADR and all joins/gates. Luna cluster_wave owns only
PreparedSimilarity, PackedAnnVectors, PackedAnnConstruction,
PackedAnnNeighborSelection, PackedAnnLayerSearch and PackedAnnExactSearch plus
new cohesive Search helpers/tests needed for this refinement, in a private exact
current-source patch. Preserve numeric limits and existing tests. Review owned
carrier/uniqueness invariants, run full Release/format/governance and unchanged
Aspire normal/scalar/related recovery/RF3; retain original failures and exact-source
Linux results. No acceleration or qualification claim precedes those results.

## Accepted prepared-value refinement, 2026-10-05

TASK-ANN-R1-PREPARED-VALUE refines the existing owned-score path without changing
its score formulas, work accounting or admission. The current diversification and
reciprocal-overflow loops allocate one PreparedSimilarity object for each examined
candidate. Use a private-constructor readonly PreparedPackedSimilarity value,
created only from an actual finite-checked PackedAnnVectors carrier and ordinal.
Its internal API is Create(PackedAnnVectors, int ordinal, DistanceMetric) and
Score(PackedAnnVectors, int ordinal), returning the exact double similarity.
It retains only that owned query slice, metric and one query norm; no norm array,
new retained vector copy, normalization, approximate formula or public bypass.

REQ-ANN-010 / AC-ANN-010: its scores equal SearchEngine.Similarity exactly in the
same native/scalar invocation mode for all three metrics, zero, subnormal, large
finite values and dimensions1/4096. Invalid ordinal, metric, dimension and
untrusted nonfinite input still fail through the original typed validation.
Mutating input buffers after Copy cannot affect the value. A warmed synchronous
loop of actual packed-value construction and scoring must allocate zero bytes
as measured on that same thread; setup, persisted corpus and assertions remain
outside that interval. Run this exact-zero measurement independently of other
TUnit cases, preserving its warmup, corpus, all metrics and zero-byte threshold;
the existing concurrent ANN correctness cases remain concurrent. The original
concurrent scalar failure and isolated diagnostic result are retained before
this scheduling correction. This is an allocation oracle, not a latency claim.

Keep one canonical implementation of the existing ordered metric reductions in
a feature-local execution helper consumed by both PreparedSimilarity and the
new packed value. Preserve the exact widen, reduction, tail, zero and square-root
order. PackedAnnNeighborSelection uses the value in diversification and overflow;
the existing budgeted PackedAnnLayerSearch score join charges the identical work
before scoring. Ordinary untrusted search and other prepared-query ownership stay
unchanged. No public DTO, native alias/field ID, canonical record or persisted
index format changes are permitted.

Root freezes this contract and ADR-019, and owns integration and gates. Luna
query_wave owns a private exact-base patch for PreparedSimilarity,
PackedAnnNeighborSelection, PackedAnnLayerSearch and new cohesive Search/Execution
numeric/value helpers only. Luna cluster_wave independently owns new
PackedAnnPreparedValueOracleTests and cohesive feature-local test helpers for the
exact-score, invalid-input, ownership and allocation criteria. Implement
the shared numeric helper, finite-owned value, consumed loop joins, then independent
public-oracle/allocation tests. Preserve every existing test, corpus, deadline,
option, graph order, budget charge and error. Root reviews the complete diff,
builds/formats/governs the full solution and runs Aspire native/scalar metric and
the unchanged ANN corpus/recall/filter/deadline gates before qualification.
Rollback reverts the computational joins; there is no data migration. Public
SDK/MCP/frontend additions are N/A because this internal optimization preserves
their existing exact operation contracts. Comparable GitHub measurements and
remaining ANN projection/public/RF3 acceptance are still mandatory.

## Accepted prepared-value construction join, 2026-10-05

TASK-ANN-R1-CONSTRUCTION-VALUE extends the accepted finite-owned prepared value
through packed construction traversal. The exact cb5 Linux scalar report retains
two unchanged 10,000-row filtered-planner failures during the ordinary 30-second
build deadline; it does not establish that allocations caused those timeouts.
Construction currently creates a PreparedSimilarity reference per inserted row.
Remove that reference allocation by using the already accepted readonly
PreparedPackedSimilarity carrier. Keep one shared Greedy/SearchLayer/neighbor
traversal implementation via an internal IPackedAnnSimilarity contract and
constrained generic calls. Implement the contract explicitly on the existing
sealed untrusted-query class and readonly owned value; no boxing, delegate,
second traversal, vector copy, cached norm array or changed formula is allowed.

REQ-ANN-011 / AC-ANN-011: for all three metrics, actual class and owned-value
traversals over the same genuine finite packed graph return identical greedy
nodes, ordered candidate ordinals and exact double scores with identical charged
work/distance/edge counters. Preserve all untrusted/owned validation and existing
zero-allocation value, independent similarity, graph adjacency, 10,000-row recall,
filter, scalar, cancellation and deadline cases with their original limits.
The construction join must use the value directly and constrained traversal must
not box it; source review and a warmed zero-byte actual value traversal measurement
cover that specific claim, with setup/assertions outside the measured interval.
This does not claim improved elapsed time, full ANN qualification or deadline repair.

## Accepted native test-resource admission, 2026-10-05

The original fb586 Linux scalar report contains3260/3263 passes and three fresh
30-second construction-deadline errors before the filter/cancellation assertions.
Its testcase spans reach16 concurrent cases. Contention is a hypothesis; these
observations do not prove a production algorithm defect or performance gain.

REQ-ANN-013 / AC-ANN-013: heavyweight10,000-row packed ANN fixture builds share
one native keyed TUnit admission resource. The same complete normal/scalar suites
retain all cases,10,000-row corpora,100-query quality cells, construction options,
30-second operation deadlines, work/memory bounds and independent assertions.
The key governs these test fixtures only; it changes no product admission,
parallel execution contract, default or measured performance claim.

TASK-ANN-TEST-ADMISSION: root owns this freeze and ADR-019 join; the Luna worker
owns only a new UnitTests Search/Helpers/PackedAnnBuildResources.cs named key and
native NotInParallel attributes on the two10,000-row AdaptiveFilteredPlanner
methods, the wide PackedAnnBudget method, and the10,000-row PackedAnnRecall and
PackedAnnFilter methods. Existing test bodies remain byte-identical. Production,
shared fixtures, AppHost, global concurrency, budgets, packages and CI receive no
worker edits. Root verifies the private base/post-hash packet, then full strict
build/format/governance and actual Aspire normal/scalar suites. Exact-source Linux
originals determine whether construction and the previously unreached assertions
pass. Failure retains the original diagnostics and calls for actual profiling;
raising deadlines or shrinking corpora is not a fallback. Rollback removes the
key and its attributes; API/data migration and frontend are N/A for this test-only
resource contract.

## Accepted construction observation contract, 2026-10-05

REQ-ANN-014 / AC-ANN-014: all seven existing synchronous 10,000-row, 16-component
packed builds emit a bounded test-scoped observation after that same call settles,
including failed builds. Retain the caller's actual `AnnWorkBudget`, unchanged
corpus, options, work cap, deadline, admission key and assertions. Invoke Build
exactly once on the same thread; return its original index or preserve its
original failure. Capture Stopwatch ticks and current-thread allocated bytes
immediately around Build, excluding formatting, output, loading and assertions.
Report only fixed scenario/phase, declared record count/dimension/metric/options,
safe outcome/code, and actual work/distance/edge counters. Never print vector or
document values, IDs, principals, paths or exception text. These observations
are development diagnostics, not BenchmarkDotNet or performance qualification.

TASK-ANN-BUILD-OBSERVATION owns four existing Search/Cases files:
AdaptiveFilteredPlannerTests, PackedAnnBudgetTests, PackedAnnRecallTests and
PackedAnnFilterTests; add only Models/PackedAnnBuildObservation.cs and
Helpers/PackedAnnBuildObservationRunner.cs. The model holds data only. The
synchronous helper owns measurement and TUnit output, not caller inputs/budgets.
The original Build failure remains primary if diagnostic output also fails;
preserve native fatal priority and retain nonfatal secondary errors through the
existing CQRS failure contract. Do not retry, launch tasks, add timeouts, change
global concurrency, or continue later assertions after a failed Build.

Root freezes this contract and ADR-019 before the Luna worker prepares a private
base/post-hash packet. Root reviews, joins, builds strictly and runs actual Aspire
normal/scalar cases before recording exact-source Linux evidence. AC-ANN-014
requires all seven real call sites and unchanged original outcomes, plus source
review of the single-call/measurement/privacy/failure boundaries. API, storage,
frontend, SDK/MCP and migration are N/A for this test-only diagnostic stage.
Rollback removes the observations and restores direct unchanged Build calls.

## Accepted adjacency-count packing, 2026-10-05

REQ-ANN-015 / AC-ANN-015: eliminate the repeated degree scan used only to count
an already owned contiguous neighborhood. Preserve every ordered neighbor,
selection rule, vector score, edge examination, index/scratch reservation and
original work/deadline/cancellation cap. The private RAM representation may pack
the count into the first existing int slot: ordinal+1 occupies bits0..22 and
count occupies bits23..30. The admitted maximum5,000,000 records and base
degree128 fit those fields. Empty neighborhoods remain zero. Read the count with
one actual charged lookup; every subsequent edge visit remains charged.
Removed count scans cease contributing work units because that work is gone.

TASK-ANN-R1-NEIGHBOR-COUNT owns only Query Search Execution/PackedAnnGraph.cs.
Root accepts this contract before joining its private base/post packet. An
independent Luna worker owns new Search/Cases/PackedAnnNeighborCountTests.cs
against an actual canonical TestDatabase corpus and genuine built graph arrays:
all metrics, partial/full base and upper adjacency, replace/clear/restore,
ordered deterministic replay, exact/one-under work, cancellation and public exact
candidate/score parity. Preserve the existing 10,000-row recall/filter/deadline
tests in normal and scalar modes. Root records the unchanged implementation
baseline, then joins and runs the same source-bound workload after strict build.
Local observations establish development evidence only; no speedup, diagnosis of
the previous timeout, global benchmark winner or Linux qualification is assumed.

This representation is disposable operation-owned RAM; no persisted index,
canonical ZoneTree data, public payload, alias or field ID changes. Rollback
restores the degree scan without rewriting data. Full existing Search acceptance
and delivered-source Linux gates remain required.

Ordered graph: root freezes the contract and ADR, cluster_wave Luna/high prepares
only PackedAnnConstruction, PackedAnnLayerSearch, PreparedSimilarity,
SimilarityMetricMath and the new Search/Contracts/IPackedAnnSimilarity.cs in a
private exact-base packet. Query's ordinary prepared-query public validation and
all other product sources are unchanged. Independent tests are a separately
assigned new PackedAnnConstructionValueTests and cohesive Search helpers against
actual existing packed fixtures; root owns their later assignment, integration,
full Release/format/governance and Aspire normal/scalar unchanged ANN gates.
No worker may alter corpus, budgets, options, formulas, graph ordering, packages,
shared configuration, docs or Git. Escalate a missing seam or changed accounting.
Rollback reverts computational joins only. Data/wire/public SDK/MCP/frontend
changes are N/A; no persisted format or canonical authority changes.


## Accepted narrow amendment: directly adjacent duplicate budget checks

## REQ-ANN-016 / AC-ANN-016

REQ-ANN-016: In Search-owned ANN paths, where `AnnWorkBudget.Check()` is immediately followed by `AnnWorkBudget.Charge(...)` with no intervening expression, branch, work, allocation, or early exit, remove only the redundant explicit `Check()`. `Charge` itself performs the same deadline/cancellation check before validating and adding the unchanged work amount. Do not remove checks anywhere else, including checks on loop/branch exits that may perform no charge. Preserve every charge amount, counter, evaluation, graph edge, allocation, score, ordering, output, cap, cancellation token, and deadline.

AC-ANN-016: A source review identifies and removes only the 12 directly adjacent pairs present in the frozen base across the eight files listed in the task manifest. Each retained `Charge` still checks the same `ReadExecutionBudget` before its original work-counter update. No check is removed when execution may exit without charging. Result bytes/order/scores, work/distance/edge counts, admitted resources, cancellation/deadline/error outcomes, and normal/scalar behavior remain unchanged for identical inputs; root compares the same source and unchanged native corpus in both modes. This source-only change does not assert or claim lower elapsed time, fewer counted work units, an explanation for the SHA55 deadline observations, or qualification.

Traceability: REQ-ANN-016 -> AC-ANN-016 -> ADR-019 amendment -> TASK-ANN-DUPLICATE-BUDGET-CHECKS -> exact-source diff review and root-owned same-source native normal/scalar observations. Unit, process-recovery, public API, storage, migration, and RF3 are N/A for this bounded internal-check change; existing full gates remain applicable to delivery.

## Ordered implementation and ownership

1. Freeze this amendment and ADR-019 addition against the exact base hashes in `../BASE-MANIFEST.sha256`.
2. The private source packet removes the explicit `Check()` from only these base-adjacent pairs: `PackedAnnLayerBuffers` (three reset loops), `PackedAnnNeighborOrdering.SelectSimple`, `PackedAnnNeighborSelection.SelectDiverse`, `PackedAnnBuilder` (level assignment and ID-order validation loops), `PackedAnnApproximateSearch`, `FilteredVectorPlanner.Plan`, `PackedAnnVectors` block allocation, and `PackedAnnExactSearch` (corpus and bitmap loops).
3. Root reviews every hunk and joins to its stable checkout. Root owns strict build, formatter, governance, unchanged-input Aspire normal/scalar execution, and delivery evidence. The private worker runs no gates.
4. Rollback restores the eight original source files byte-for-byte. No data/public format migration exists.

The workers do not own product documentation or shared files beyond this proposed private freeze; root performs the accepted specification/ADR join. Existing corpus, admission key, operation budgets, deadlines, options, tests, and acceptance thresholds are unchanged.


## Accepted REQ/AC-ANN-017: remove duplicate budget checks in charged inner loops

## Evidence and scope

Exact SHA55 Build-only observations for the unchanged 10,000-row, 16-dimensional
DotProduct corpus (seed `5423839510519694385`, Connections=16,
EfConstruction=128, 30-second deadline) reported about 165 million work units,
4.37 million distance evaluations and 12.41 million edge visits. These are
observed aggregate counts; they do not attribute a count to one method or
establish the cause of the deadline outcomes.

Current source explains the counting path. `AnnWorkBudget.ChargeDistance(16)`
adds 16 work units and one distance evaluation; `ChargeEdge` adds one work unit
and one edge visit. During each HNSW insertion, `PackedAnnLayerSearch.Greedy`
walks each upper-layer neighbor, while `SearchLayer` repeatedly pops retained
candidates and `VisitNeighbors` charges every edge examined, including edges
to already-visited nodes. Unseen nodes are scored. Neighbor diversification and
full reciprocal-neighbor replacement also score vectors. These are actual
algorithmic operations; the report does not reveal their per-method split.

A distinct source-level hot path is duplicate deadline/cancellation observation
inside charged inner loops. Each iteration of all four `PackedAnnCandidateHeaps`
sift loops calls `budget.Check()` and then unconditionally calls `IsBetter` or
`IsWorse`; those comparators immediately call `budget.Charge(1)`, which itself
calls the same `ReadExecutionBudget.Check()` before updating work. Each
`PackedAnnLayerSearch.Greedy` iteration likewise calls `budget.Check()` and
then unconditionally calls `graph.NeighborCount`, which charges before reading
the count. These sites repeat clock/cancellation checks without adding counted
work or an uncharged exit path.

## REQ-ANN-017 / AC-ANN-017

REQ-ANN-017: Remove only the standalone explicit `budget.Check()` at the start
of each iteration of `SiftBestUp`, `SiftBestDown`, `SiftWorstUp`,
`SiftWorstDown`, and `PackedAnnLayerSearch.Greedy`. Retain the immediately
following comparator/neighbor-count charged operation, which performs the same
cancellation/deadline check before its existing work-counter update. Preserve
all calls on paths that can exit without a charge, all charge amounts and
counters, comparison/selection order, score computations, candidate contents,
work/scratch/index caps, original deadline, caller token and portable normal /
scalar semantics. Do not broaden this to the `SearchLayer` outer loop: `PopBest`
can return an empty-heap sentinel without charging, so its explicit check is
required and must remain.

AC-ANN-017: A source diff removes exactly five loop-entry `budget.Check()`
calls at the sites above and changes no other code. Root uses the existing
`PackedAnnBuildObservationRunner` with the unchanged 10k corpus and options to
compare each mode to its own pre-change observation (normal-to-normal and
scalar-to-scalar): exact work, distance and edge counters must remain stable,
and existing correctness output/oracles must pass. The native `AdaptiveFilteredPlannerTests.AcFilter004...`
continues to check the independent scalar-cohort candidate oracle;
`PackedAnnBudgetTests.AcAnn003...` continues to exercise real cancellation and
deadline interruption; recall/filter/full existing tests retain their original
coverage. No assertion, corpus, or test source changes are allowed. Measurements
record actual elapsed/build observations but make no speedup claim unless
same-source before/after data supports one. The prior SHA55 deadline failures
remain evidence only; this change does not assert they are repaired or caused
by duplicate checks.

Traceability: REQ-ANN-017 -> AC-ANN-017 -> ADR-019 amendment ->
TASK-ANN-INNERLOOP-CHECKS -> exact-source patch review and root-owned unchanged
input normal/scalar observations. No new corpus, test seam, budget, deadline,
workload, package, persistence, public API, migration, or RF3 change occurs.

## Ordered ownership

1. Root reviews and accepts this exact REQ/AC/ADR contract against the frozen
   source hashes in `../SOURCE-BASE.tsv` before implementation.
2. Private source owner edits only `PackedAnnCandidateHeaps.cs` (the four
   sift-loop checks) and `PackedAnnLayerSearch.cs` (the one Greedy-loop check).
3. Root owns cumulative joins with any earlier pending ANN-016 source packet,
   strict build/format/governance, and identical-source normal/scalar Aspire
   measurements and full required gates. The private worker runs no build,
   test, benchmark, Git, or checkout edit.
4. Rollback restores these five original checks. No persisted or public format
   changes exist.

## Accepted reciprocal insertion invariant, 2026-10-05

REQ-ANN-018: Build inserts strictly ascending source ordinals with one private
writer. Before insertion at a given layer, no prior node at that layer can have
an edge to the new source. The selected reciprocal targets are distinct prior
nodes; installing the source's own outgoing adjacency does not mutate their
adjacency. Remove only the impossible target-adjacency membership scan for the
new source from `PackedAnnNeighborSelection.AddReciprocalLinks`, and delete its
now-unused `ContainsNeighbors` helper. Preserve candidate deduplication,
selection and ordering, every actual distance and edge examination, graph
updates, per-target cancellation/deadline checks, byte/work admission and the
original 30-second `ReadExecutionBudget` bound. This invariant applies to each
layer separately; it does not authorize a mutable-index update path.

AC-ANN-018: Independent native tests inspect complete successful graph
adjacency for valid prior/new source identities, unique neighbors, degree bounds
and deterministic ordinal ordering on small deterministic and 10,000-record
DotProduct corpora. Existing exact score, recall, eligibility, fallback,
cancellation, deadline and healthy-follow-up assertions remain unchanged.
Successful before/after controls must preserve graph contents, scores, distance
and edge counts; work counters truthfully omit only comparisons no longer
performed. An interrupted build has no complete graph/counter baseline and
cannot establish that equivalence by comparing its partial counters. Root
retains the original Linux normal/scalar deadline failures at source `788b8fd`
and runs actual normal/scalar Aspire observations before any speed or deadline
repair claim. Source proof alone is not a performance or acceptance result.

The test-only successful-control observation uses a fixed v1 SHA-256 framing
of the native state's count, entry point, maximum level, each ordinal's exact
ID/revision/level and every layer's complete ordered adjacency. It hashes only
the actual independent native snapshot, without a second graph builder or
stored golden replacement. Capture build-only elapsed ticks and current-thread
allocations before snapshot/assertion/output work; retain actual build work,
distance and edge counters and the complete fixed options. Use the same two
controls and test source for original and candidate builds in each native mode.
Only successful complete hashes can establish graph parity; failed controls
remain failed native reports and cannot supply a graph baseline. Hashing and
fixed test output stay outside the product and its execution budget.

Traceability: REQ-ANN-018 -> AC-ANN-018 -> ADR-019 amendment ->
TASK-ANN-RECIPROCAL-INSERTION -> independent native graph tests and original
Aspire reports. The Luna source owner produces a hash-guarded private packet
for `src/KeyLoad.Query/Features/Search/Execution/PackedAnnNeighborSelection.cs`
and independent tests in the Search test slice. Root owns contract review,
baseline/after observations, join, strict build/format and all required gates.
No public, persistence, package, long-operation or RF3 contract changes occur.
Rollback restores the scan and helper; R2/R3 remain separately unimplemented.
