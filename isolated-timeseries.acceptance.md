# Isolated intensive TimeSeries acceptance

Goal: meaningful native Linux1/2/3-node append/read/latest/statistics/windows
comparisons over identical data, one engine and scenario per independent runner,
raw JSON and complete authenticated30-cell site data. Canonical slice is
BenchmarkComparisons; decision ADR059. Existing270 wire and legacy48-input
TimeSeries/schema1/library regressions remain unchanged. Production KeyLoad isRF3.

In scope: KeyLoad and TimescaleDB, six independent native preflights,30 intensive
cells, real SDK/official MCP/Npgsql, bounded deterministic output oracles,
source/image/topology/ACK/resource facts and separate family qualification.
Out of scope: an in-memory library in the node matrix, local qualification,
automatic Timescale failover, read scaling, power-loss readiness, license changes,
ManagedCode workarounds, pooled percentile/winner or SIMD acceleration claims.

Root accepts the staged contract below before implementation. Actors are the
trusted own-main GitHub executor, one cell-owned native cluster/client, persisted
KeyLoad principals and official native PostgreSQL role. Native error semantics
and authorization differences remain explicit; PostgreSQL does not acquire a
fictional KeyLoad policy or error code. Public KeyLoad contracts remain unchanged.

| Requirement | Criterion | Pass / fail and test method |
|---|---|---|
| REQ-BC-059 isolated serious TimeSeries cells | AC-TSI-001 | Closed2×3×5 plan and separate6 preflights. Exactly one selected engine/native node count/scenario per Linux VM. Reject invalid/foreign selection before allocation. Genuine model/source tests and all actual cells; no in-memory or independent-server cluster substitute. |
| REQ-BC-060 shared exact corpus and ordered results | AC-TSI-002 | Freeze4096 seeds and operation plans below. Common workload hash excludes private run namespace. Exact returned timestamp/sequence order, seed sequences1..4096, full identity/value/canonical tags and cardinality. Fail on reordered, missing, duplicate or extra rows. Pure independent-oracle TUnit plus genuine native response/readback tests. |
| REQ-BC-061 actual native ACK/copies and ownership | AC-TSI-003 | Observe exact1/2/3 members, endpoint identities, image/config/source/versions and seed/final cuts before and after timing. ACK1/2/2 separately from observed copies1/2/3. Actual native image/replication/private-schema/cleanup/fault tests; no inferred replication. |
| REQ-BC-062 bounded honest latency/throughput | AC-TSI-004 | Five repetitions,256 warmups EACH repetition then10000 measured attempts,16 client workers,30s operation deadline. No measured retries. Capture all50000 compact attempts and each actual result assertion while releasing full responses promptly. Freeze timing below, bounded cell/teardown and real process telemetry. Fail on missing samples, errors, cancellation, unbounded buffering or invented resources. |
| REQ-BC-060 complete TimeSeries operations | AC-TSI-005 | One-sample acknowledged append; inclusive bounded raw read; latest timestamp/sequence tie order; complete half-open Count/Sum/Min/Max/Average; dense From-anchored/clamped windows including empty windows. Positive/boundary/empty/tie/negative/fault tests through actual native operations; native contract differences retained. |
| REQ-BC-063 actual public KeyLoad qualification | AC-TSI-006 | Real SDK and official MCP prove append/raw/latest/aggregate/windows, replay, event dedup/conflict atomicity, budgets, finite arithmetic, persisted projection/revocation, cancellation and healthy followup. No client guard masquerades as a public negative. Actual node1/2/3 tests and existing RF3/recovery suites; credentials never become trusted roles or logs. |
| REQ-BC-064 complete native family evidence/publication | AC-TSI-007 | Distinct closed family protocol authenticates all30 current jobs/artifacts/digests and common images; preserves raw bytes. Missing/failed/stale/mixed/duplicate/skipped family blocks its publication. Complete collector/projection/freshness tests, exact-SHA all30 and actual full site/provider/live proof. Existing270/legacy gates and thresholds remain. |
| REQ-BC-062 maintainability/coverage/portability | AC-TSI-008 | .NET10/TUnit/analyzers/format/governance; scalar/normal correctness, changed-source80/70 and critical90 from genuine native collection. Separate instrumented qualification from measured images. No source/configuration-only numeric claim. Required full GitHub checks and matched coverage evidence. |

## Frozen profile and corpus

Profile intensive-timeseries-4096-c16: seed1729, samples4096, operations10000,
warmup256 PER repetition, repetitions5, concurrency16, operation timeout30s.
Positive timestamps/widths are aligned to microseconds common to PostgreSQL and
KeyLoad. Existing tick/minimum/maximum KeyLoad public edge qualification remains.

UTC epoch is2026-01-01T00:00:00Z. Logical seed group g=0..255, item i=0..15:
EventId=s- plus original index g*16+i as six decimal digits; timestamp=epoch+
g*5minutes+floor(i/4)minutes; value=((g+1729)%31-15)+(i-8)/4.0; canonical tags
{"kind":"intensive","revision":1}. Each group has four ties per timestamp and
one empty minute. Append seed groups and items in descending g/i order, in16
sequential chunks of256 samples. Persisted sequence is insertion ordinal1..4096.
Expected raw order is timestamp ascending then persisted sequence ascending;
compare the returned array directly, never sort away a native ordering defect.
Workload SHA256 binds canonical seed identities/UTC ticks/values/sequences/tags
and operation plans, excluding the random private schema/partition/series/run ID.

Read operation index k selects group k%224, with query From=epoch+g*5minutes+
1minute, Until=From+160minutes. Raw includes Until, fits at most516 samples and
uses limit1000. Latest uses inclusive AtOrBefore=epoch+(k%256)*5minutes+3minutes,
with highest sequence among the newest timestamp. Aggregate uses the same From
and exclusive Until with exactly512 samples and MaxSamples10000. Latest returns
the tied item i=12 with sequence4096-16g-12, rather than i=15; insertion was
reversed. Windows uses that half-open range,
width3minutes, MaxSamples10000/MaxWindows1000; expected54 dense windows, final
end clamped. The independent oracle derives each range without native results.
Also qualify untimed before/after-seed, full seed, empty gaps, exact endpoints,
nonzero anchors, absent series and narrow ties. Sum/extrema/count compare exactly
over quarter values; average tolerance is absolute1e-12, never rounded to zero.
Measured three-minute windows are all nonempty. The separate gap check is
[epoch+5g+4minutes, epoch+5g+5minutes), width1minute: exactly one empty window
with Count0/Sum0/null extrema/null average. Seed sequence=4096-(16g+i).

Append has a fresh warmup series and fresh measured series PER repetition, both
initially empty. One operation sends one unique sample in one acknowledged native
transaction/SDK CommandRequest, with a deterministic fresh command/EventId and
timestamp=epoch+10days+k milliseconds. Actual commit sequence is receipt-derived;
do not equate it with concurrent submission index. All10000 records must have
unique sequences1..10000 and exact event/value/tags. Read back using predetermined
disjoint timestamp ranges of at most1000 rows; never skip tied records via a
timestamp-only cursor or aggregate seed plus appended10000 beyond the cap.
Warmup records are independently verified and excluded from measured attempts.
For both append phases k starts at0. EventId is a- plus k as five decimal digits;
value=((k+1729)%31-15)+(k%16-8)/4.0 and tags equal the seed tags. Logical series
are seed, warm-r0..r4 and measured-r0..r4 inside each private partition/schema.
GUID command identity uses SHA256(UTF8(runId+":"+purpose)) first16 bytes in the
existing .NET Guid constructor convention; purpose contains phase/repetition/k
using invariant decimal. Run namespace is excluded from the common workload hash.

## Timing and resource contract

Use16 bounded closed-loop workers. For each attempt, start Stopwatch immediately
before the actual client operation; stop after complete response decode/receipt.
Validate the full returned data AFTER that latency clock, then release it before
the same worker starts its next request. At most16 full response objects are
retained. Store compact index/repetition/latency/outcome/result count/digest and
append receipt sequence, never all returned arrays. No parallel detached verifier
or unbounded queue. Prepare expected plans/oracles outside clocks.

Headline throughput is useful successes divided by the complete repetition wall
interval, INCLUDING this bounded per-attempt validation/backpressure overhead.
Record accumulated validation-worker elapsed time separately and explain the
load model; summed concurrent validation time is not a repetition wall percentage. Do not
reuse the270 family's buffered-output/excluded-validation denominator or imply
continuous saturated server-only throughput. Percentiles are nearest-rank per
repetition over all10000 attempts; publication uses the five per-cell repetition
medians/summaries, never pooled samples or a median per each of16 client workers.
Native process CPU/allocations/RSS/GC and container/resource facts remain measured
and labelled by process; missing observations are explicit unavailable values.
Client CPU/allocations/RSS/GC are required; server CPU/RSS/container facts must be
captured before/after. Server managed allocation/GC unavailable to native engines
is labelled unavailable with its reason, never zero or an omitted field. Warmup
uses the same16 closed-loop clients and plans k0..255, verifies every result and
releases it before timing; no warmup attempt enters the50000 measured samples.
Intensive cells have a90-minute overall deadline including readiness/seed/warmup;
preflights30minutes, with outer job150/60minutes respectively. Linked per-call
deadlines remain30s. All independent teardown stages get30s,
retain partial raw/diagnostics and preserve primary failures. Incomplete cells
cannot publish or be retried silently.

## Native ownership and negative flows

KeyLoad uses the existing benchmark-only fixed RF1/RF2/RF3 opt-in and exact
persisted voter guard, request-grain boundary, node-local hosts and ordered apply
gate. Actual SDK creates the TimeSeries resource using persisted authority.
No original legacy target's hard RF3 contract changes.

Timescale uses the existing digest-pinned2.30.2-pg18 image, one primary and zero/
one/two native physical standbys. Establish actual entrypoint/user-switch/PGDATA
compatibility before bootstrap implementation. Root alone makes shared primary
DNS explicit. Require fsync/on, synchronous_commit/on, exact streaming identities,
ANY1 sole standby at n2 / ANY1 of two at n3, flush/replay of every observed copy.
Keep commit quorum separate from all-copy readback and no automatic failover claim.
Native Npgsql/SQL implements all five operations with parameters and bounded
output. Reuse TimescaleSchemaLifecycle's real private owner marker and cleanup;
additional owned identity/counter tables or routines require the detailed native
SQL implementation contract before that worker starts. Cleanup never touches a
foreign namespace or replaces marker validation with a guessed table name.

Public KeyLoad negatives: identical-command replay; fresh-command identical-event
dedup; changed sample/tags Conflict with complete batch rollback; invalid/inverted
ranges/width/caps and overflow lookahead; absent/empty and finite sum overflow;
persisted field projection/revocation; actual cancellation with healthy followup.
Preserve actual SDK typed Cancelled versus MCP exception semantics. Native SQL
negative outcomes are checked against PostgreSQL's declared contract/SQLSTATE;
an inverted SQL range returning empty is not relabelled as KeyLoad BudgetExceeded.
Observe genuine public errors, never synthesize them from expected fixture codes.

## Staged boundaries, compatibility and test matrix

Canonical map: Comparisons/Features/BenchmarkComparisons/TimeSeries/Intensive
for NEW TimeSeriesIntensive*/KeyLoadTimeSeriesIntensive*/TimescaleTimeSeriesIntensive*
domain/adapters; matching NEW Unit/ComparisonTests prefixes; AppHost NEW
IsolatedTimeSeries*; host NEW TimeSeriesIntensiveHost*. Root owns all existing
shared files, selectors/settings, solution/configuration, workflows, versioned
JSON contracts, source/coverage inventories, site and durable docs.

No product API/data-format migration. New phase is explicit profile
timeseries-intensive with typed target KeyLoad/TimescaleDB, nodes1..3, scenario
Append/RawRangeRead/Latest/Aggregate/Windows and preflight/intensive phase; reject
before native allocation. Legacy timeseries and270 settings/bytes stay valid.
Family JSON/native image/provenance schema is frozen separately before evidence
implementation; it must never relax270 validators or impersonate their job IDs.
Rollback removes only the additive new routes/owned data and leaves old archives,
product defaults and published facts intact. Missing new evidence blocks refresh.

| AC | Planned automated proof / command / required evidence |
|---|---|
|001| Closed plan/options/resource model TUnit; actual six preflights and30 distinct Linux job URLs. GitHub comparison suite only. |
|002| Independent pure corpus/sequence/ties/UTC/hash tests; real SDK/Npgsql direct-array equality and full native seed/final readback. |
|003| Genuine Docker image/config inspection, native primary/standby queries and per-node copy/cut checks, owner-marker/fault/cleanup tests. |
|004| Real native concurrent run50000 attempts, zero errors, bounded result lifetime/retention, actual wall/validation/time/resource receipts; strict corrupt/missing sample rejection. |
|005| Independent five-operation oracle, empty/tie/endpoint/window/clamp and native negative cases; no fake target. |
|006| TUnit actual SDK and official C# MCP clients at native1/2/3; existing full RF3/recovery qualification retained. |
|007| Real Node/file/TUnit strict family corruption and authenticated provider/freshness; complete raw30 and site/native Chrome/coverage/provider/live proof. |
|008| Actual solution Release build/format/governance, normal/scalar TUnit, recovery/RF3 and separately instrumented native80/70/90 reports. |

No automated exception permits skipped suites or missing required telemetry.
The explicit native managed-GC/allocation unavailable reason above is a declared
applicability fact, never a passing missing required metric. Review-only
exception: unforced simultaneous cleanup filesystem/logger faults and immutable
source/read races have bounded control-flow/ownership review evidence alongside
normal genuine lifecycle tests; never add a production test hook or double.
