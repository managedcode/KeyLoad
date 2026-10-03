# Isolated intensive TimeSeries acceptance

## TS009C-G guarded original-store contract

AC-SG009-001 (REQ-STORAGE-015, REQ-BC061/064, TSI003/004): internal
ZoneTreeExistingStore.Open(ZoneTreeStoreOptions options, Guid expectedNodeId)
requires an absolute existing canonical directory, nonempty expected node GUID
and configured incarnation, positive frame/snapshot budgets and no cache/fault
observer. Require original owner.lock, bounded checksummed identity format4,
exact captured NodeId/incarnation, original commands.wal and original tree/provider
metadata. Use existing identity validation/codec, FileMode.Open and provider
ZoneTreeMetaWAL.Exists plus Open, never OpenOrCreate. Order is owner lock,
identity, journal, provider metadata/Open, canonical journal recovery. Reject
missing/mismatched input before provider/recovery; create no replacement original
file or identity and do not promote/restore/configure/reclaim/start maintainer.
AC-SG009-002: guarded recovery explicitly may replay/truncate incomplete canonical
redo and modify/recreate provider-derived WAL, preserving original identity and
business records. Real-file TUnit tests create/commit/close an actual native store,
then guarded read/identity/position/dispose/reopen, missing original files/tree/
metadata, wrong node/incarnation/format, occupied ownership and invalid inputs.
Assertions verify rejection/no replacement and unchanged identity bytes; ordinary
public opens and existing recovery tests retain their complete contracts.
AC-SG009-003: guarded registered-handle cleanup attempts tree, journal, ownership
and gate independently, retains original failure as the same exception plus every
cleanup cause (including nested fatal causes), and is idempotent after successful
handoff. No provider partial-open settlement is inferred from null Tree. Every
native guarded call must execute in a separately owned inspector process; parent
holds existing node.owner.lock until actual original process exit/reap and both
readers settle, before restart. Guard-only source cannot unblock native copies.
AC-SG009-004: root reviews all candidate diffs, source compile/analyzers/format,
then exact-source GitHub real-file UnitTests/full recovery. Provider partial-open,
cleanup-failure and uncooperative child runtime tests belong to the later original
inspector process contract; no fake handle/fault observer substitutes. Until that
contract executes, explicit source/lifetime review is the only evidence for rare
cleanup/fatal/handoff failures, never a passed runtime or coverage criterion.

|Criterion|Verification|Join condition|
|---|---|---|
|SG009001|Real native-file positive/missing/mismatch/occupied/invalid TUnit|Exact-source GitHub normal/scalar unit reports, no skipped case|
|SG009002|Actual original identity bytes, committed data/position and successful normal reopen|Same actual source plus existing recovery suites|
|SG009003|Independent cleanup/source review; later genuine inspector process fault/kill/readers tests|Native proof forbidden before real process qualification|
|SG009004|Root diff/build/analyzer/format and original Actions reports|Source checks alone cannot certify native copies|

Root accepts this additive private guard stage. Public APIs/storage codecs/ordinary
open behavior and productionRF3 do not change. Rollback removes private guard
classes/branches/constructor only. Node lock/process/control/oracle/ACK/collector
joins remain required, separately approved stages.

## TS009H input joins (REQ-BC059/061/064, AC-TSI001/003/007/008)

- AC-TH009-001: NEW IsolatedTimeSeriesBenchmarkResources.Add validates the exact
  existing selection, explicit Enabled=true and unique cell in Current family
  plan before directories/runner/native allocation. One runner and only selected
  KeyLoad or Timescale physical1/2/3 resources. Bind the existing six selection
  keys (omit Scenario in Preflight), exact family CellId and ContractSha256,
  private absolute output/root and storage description. If CellId/ContractSha256
  inputs are supplied, require exact computed equality before allocation.
  Require Root/native to be absent (file or directory); reject existing native
  data without deleting/reusing it. Existing owned reports/control may remain.
  Newly created Unix root/output directories use0700. Validate existing paths as
  owner-RWX with no group/other permission before allocation; preserve shared
  directory modes on rejection instead of chmodding shared paths. The dedicated
  Unix permission assertion fails explicitly on Windows and executes on Linux.
  Preserve current native waits, images, ownership and default/legacy composition. Actual Aspire model
  tests cover6 native selections with both phases/all5 scenarios, binding identity
  and invalid/mixed/disabled selections without added resources/directories.
- AC-TH009-002: ComparisonHost typed settings reuse the existing provenance and
  immutable image-reference validator via a new nonnullable ReadTimeSeriesIntensive
  entry. Exact source/configured/GITHUB_SHA, positive actual run/attempt/job,
  nonempty repository/ref/workflow, family evidence profile, unique actual family
  CellId/ContractSha256, absolute output and exact fixed storage description are required. No local fallback,
  old270 worker envelope, copied fabricated provenance or private input echo.
- AC-TH009-003: typed native settings retain selected immutable image and exactly
  contiguous/distinct indexed native endpoints. KeyLoad requires HTTP(S) authority
  endpoints, exactly count HTTP(S) voter origins, nonempty canonical GUID incarnation
  and private admin; rejects Timescale ConnectionString. Timescale requires TCP
  authority endpoints with explicit1..65535 port and private nonempty single-host
  Npgsql connection (valid port/user/password); rejects KeyLoad admin/incarnation/
  voter inputs. Reject scalar Native root, unknown/opposite Native children and nested
  native scalar fields. Allowed Native keys: KeyLoad Image/Endpoints/VoterIds/
  Incarnation; Timescale Image/Endpoints/ConnectionString. Field keys use the
  existing IConfiguration case-insensitive semantics. Validation covers the merged IConfiguration view; pre-merge duplicate keys
  are not observable evidence. Reject nested indexed children, userinfo/path
  beyond /, query, fragment, sparse/alias indices,
  duplicate authorities, empty/extra values and unowned target credentials.
  Typed settings neither connect/allocate a target nor log secrets. Override
  ToString on both records to a fixed type label; no generated record diagnostic
  may expose private credentials. Normalize selection/identity/parser expected
  input errors to the fixed settings code without secret-bearing inner errors.
- AC-TH009-004: actual ConfigurationManager/typed reader TUnit cases prove both
  targets1/2/3, preflight/all intensive selections, source/job/image/cell mismatch,
  absent/malformed inputs and safe diagnostics. No service doubles. Root enables
  an internal UnitTests friend and the existing host ProjectReference output only;
  production public CLR and old CLI/process tests remain. Source build/format is
  distinct from exact-SHA normal/scalar TUnit and native6/30 qualification.

Root accepts these source stages; existing dispatches stay unchanged until the
original target/control/copy lifecycle is implemented and reviewed. Tests/reader
values are input fixtures, never provider evidence. Rollback removes new additive
classes/method/friend and restores the project-reference metadata; no data/API
migration. All remaining AC-TSI criteria remain required.

AC-TB009-001 (TSI001/003/006): for each native1/2/3 KeyLoad resource model, runner
Benchmarks__Native__Incarnation references the actual existing incarnation
ParameterResource, matching each node's KeyLoad__Incarnation. Each indexed
Benchmarks__Native__VoterIds__i equals the selected node's actual canonical voter
origin and each node's KeyLoad__Peers__i; exactly the selected count exists.
Preserve independent expected http://nodeN:8080 values, private storage/waits/
image/admin and every original model assertion. Updated existing three TUnit
cases execute only in exact-source GitHub; models never establish native copies.
No alternate profile, public API, secret value, port or default RF3 change.

## TS009J settled-run raw payload

TJ009003 pre-output refinement: reject undefined run stage/failure origin/nullable
ErrorCode enums and any nonfinite value among the six original measurement
doubles. These values cannot satisfy declared enum-name/JSON-number contracts;
reject before output rather than substituting names or values. Cover each with
unchanged-output assertions; preserve representable original facts unchanged.
An uninitialized repetition array or null repetition record also rejects before
output; initialized empty/partial arrays remain valid original failure evidence.
Do not require5 repetitions or infer ACK positivity/phase/native success here.

- AC-TJ009-001: explicit writer emits schemaVersion1, exact scenario/frequency,
  seed verification, original run/repetition/phase/measurement/failure facts and
  all50000 slots. Preserve original integer ticks and nullable facts without
  sampling, retries or rounding.
- AC-TJ009-002: NotStarted emits only planned slot/repetition/index/worker and
  observed=false. Observed slots retain outcome/ticks/nullable count/digest/
  receipt sequence/actual nullable ACK UUID and sequence/independent structured
  primary and cleanup origin/code/status/packed SQLSTATE. Failed/cancelled calls
  may retain an actual ACK/count and stay failed. No invented unstarted timings.
- AC-TJ009-003: reject invalid scenario/frequency, wrong50000 length, wrong
  planned ordinal/index/worker, undefined outcome and negative observed ticks
  before output. Do not serialize exception messages, tags, secrets or response
  bodies. Caller owns the Utf8JsonWriter/stream; helper neither flushes nor disposes.
- AC-TJ009-004: workloadSucceeded is exactly the original run predicate, never
  a native/publication verdict. Generate no source/run/job/image/copy facts here.
  Genuine ledger/DTO/writer/parser TUnit data covers default slots, observed
  success, cancelled ACK with independent cleanup, every repetition/measurement
  field, nulls, integer boundaries and every rejection with unchanged output.

REQ-BC-059/060/064 and AC-TSI-002/004/007/008 map to TJ009 under ADR059. NEW
TimeSeriesIntensiveRunJsonTests executes normal/scalar only in exact-source
GitHub. Independent expected JSON keys/numerics/UUIDs must not import writer
constants. Pure DTO/writer data is not a provider double or native6/30 proof.

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

TS006C clarification: name the independent groups gRange=k%224 and
gLatest=k%256. Seed readback chunk q0..15 has inclusive endpoints at group16q
minute0 and group16q+15 minute3, exactly256 samples. Measured append chunk q0..9
has inclusive offsets1000q..1000q+999 milliseconds, never a1001-row range.
Raw Limit1000 permits truncation, so complete readbacks also require separate
untimed whole-series aggregate/count proof: seed4096, warmup256, measured10000
with MaxSamples10000. Overflow fails. Append receipt sequences form the exact
1..10000 bijection independently of submission order. Oracle sums use integer
quarter-units; reject nonfinite actual statistics before the average tolerance.

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


## TASK-ISO-TS006C-R accepted validation-cost and independent-oracle correction

AC-TSI-002/005/008 keep exact v1 bytes, formulas, ordering, finite numeric
semantics, seed/window boundaries and all caps. Source review identified repeated
JSONB whitespace normalization (two validator parses plus one framing parse
per row); this is a source cost estimate, not a measured performance claim.

NEW TimeSeriesIntensiveTagScope.cs retains only one last successfully validated
raw spelling and its canonical string within one synchronous validation/digest
call. Exact Profile.Tags returns directly; equivalent repeated spelling parses
once. Invalid/changed/extra tags or parse failure never replace a valid memo.
No dictionary/global cache/parser delegate/counter/retained JSON document/DTO.
RowVerifier accepts the scope and returns validated canonical tags after full
identity/order/sequence/time/value/finite checks. ResultFrames writes that exact
canonical result; no second parse. ResultDigest adds ValidatedRaw(expected,actual)
and ValidatedLatest(expected,actual), checking cardinality before streaming
validate/frame of each actual row. Standalone Oracle/digest methods use a local
scope. No response array/materialization/sort; future runner uses combined API
after latency stop, includes validation in wall time and releases response before
next call. Maximum16 live responses remains unchanged.

Disjoint same worker owns these NEW/previously authored TimeSeriesIntensive*
production and test files only. Introduce cohesive named frame/domain/version/
label/error/recipe constants within this owned new unit, preserving every
existing byte/formula; root literal/magic policy applies even without diagnostics.
No runner/interface/target/native host/shared or old-family writes.

Tests first: repeated516 JSONB spellings, alternating equivalent spellings,
corruption after memo hit, malformed/extra/changed tags, full identity/cardinality/
tie/default/null/NaN/Infinity failures, independent-reference combined hashes,
warmed genuine synchronous CI allocation comparison. NEW ReferenceOracle and
OracleReferenceTests independently derive every raw row/order over224 ranges,
all54 window absolute bounds/all statistics, latest256 groups/tie/gap/afterseed,
and all10000 append receipt identity/value/sequence mappings; reference code
must not call production profile/corpus/plans/oracle/framer for expected values.

Ordered source join: tests, bounded correction, root full review/scoped build/
format, exact-SHA full normal/scalar GitHub suites, later actual all30 native
responses. No local tests/runtime/benchmarks/Git or invented allocation/speed
result. Additive source rollback removes this coherent owned unit; no persistence
migration. ADR remains Accepted; source and native gates stay distinct.


TS007B clarification (REQ-BC-060/062, AC-TSI-002/004/005/008): the exact accepted
compact runner/interface/error/ownership contract is ADR059 TASK-ISO-TS007B.
Every actual attempted failure keeps its latency and native observed error facts;
NotStarted has no latency. Missing/default/duplicate ledger publications fail.
No timeout may detach a still-live original client Task. Host lifetime includes
readiness; cancellation/drain/late-success rejection and max16 live decoded
responses require genuine native SDK/Npgsql evidence. Pure index/ledger/hash/
SQLSTATE/sequence/summary tests are a separate source gate and cannot replace it.


AC-TSI-004/008 TS007B failure boundary: fatal memory/stack/access violations
propagate after cancellation and observation of all owned loops; ordinary
failures retain exact compact attempts. Deadline state is captured at actual
observed call completion, before post-call verification. Pure filter tests
are algorithm proof; native cancellation/response-lifetime proof is mandatory.


TS007B-D AC-TSI-004/008: original setup and readback task faults must retain
actual native codes while recording own30s deadline versus caller cancellation
correctly at observed completion, including OCE and late native errors. Fatal
causes propagate. Pure closed outcome/error tests and real later native blocking/
cancel gates are required; no fake target/clock or detached task qualification.


TS007B-D AC-TSI-004 refinement: cancellation observed before invocation produces
NotStarted and zero latency with no target call; after the accepted entry
decision an actual caller attempt may fail/cancel. Pure entry-state proof and
later genuine native cancellation-boundary evidence are required. Percentile
goldens must distinguish all-attempt from successes-only ranks by failure
placement, including p50/p95/p99.


TS007B-D AC-TSI-004/008: nonfatal final-readback cancellation/failure must
retain completed/drained phase wall/peaks/attempts with FinalVerified=false and
actual failure. Unknown or uncompleted phases remain missing; native failure
does not permit fabricated success or additional readback allowance.
TS007B-D root review also requires preserving a successfully observed warmup
when measured-entry cancellation or empty-series readback fails. Return the
actual warmup and failure, with no invented measured phase. Pure phase evidence
tests plus source review cover the retention rule; actual SDK/Npgsql cancelled
entry/readback/healthy followup remains a native qualification requirement.


TS008K-S refinement of AC-TSI-002/003/004/005/006/008: actual SDK adapter
borrows1..3 clients, preserves all five native outputs and256-sample seed order,
validates actual command/matching Revision/token authority before success and
keeps only one validated ACK-cut scalar. Actual returned Problem code/status
remain nullable facts; unknown codes/status0 never acquire invented metadata.
Malformed reply may retain only unambiguous matching Revision, with no inferred
applied count/ACK cut/digest. Pure Receipt/Result/Seed/Topology TUnit cases cover
positive, missing/duplicate/wrong/late/error inputs; genuine SDK/MCP/native6/30
remain separate required proof. Direct original task/uncancellable MCP disposal
observation cannot be replaced by a30s detaching WaitAsync. See ADR059 accepted
TS008K-S for exact source ownership, timing, cleanup and root join contract.

TS008TR source refinement: root explicitly accepts typed seed arrays/scalar
append instead of the prior sample JSONB choice, with exact routine types and
ordered fields in ADR059 TS008TR. AC-TSI-002/005 require real exact-row/UUID/
ordinal/positive-sequence validation BEFORE commit, dedup/conflict/overflow full
rollback, single-snapshot bounded aggregate/windows and actual ordered tags.
AC-TSI-003 requires transactional CREATE SCHEMA/marker/installer, exact positive
owner confirmation, foreign/tampered marker preservation and one private borrowed
datasource. AC-TSI-004 requires shared overall lifetime, original held-lock cancel
observation/rollback and healthy followup; actual postcommit cleanup failure keeps
actual ACK facts separately but never passes the attempt. Native IDs1..256 UTF8
bytes, object tags<=4096 native-text bytes and finite stored microsecond domain
exclude the exact MinValue sentinel; query infinities remain native for raw/
aggregate, finite nonnull fixed UTC windows remain explicit native scope.
Pure actual-exception/code tests establish compact fact handling only; actual SQL
guards, array dimensions/nulls, caps, transactional DDL/dedup/overflow, native
blocking cancellation, scope/order/window/full-readback and1/2/3 ACK/copies require
real GitHub native tests. No mock target, local execution, inferred durability,
source-only qualification or performance result satisfies these criteria.


TS008N-S AC-TSI-002/003/004/005/008: failed calls after an actual scalar commit
retain the actual echoed UUID/positive sequence separately from outcome/native
primary and cleanup facts, without a decoded Count or digest. Failed/unknown
commit, pending row, input UUID or invalid authority never acquire an ACK. Preserve
primary native facts before nonfatal cleanup; original fatal wins and remains
unwrapped. Cooperative disposal joins all original tasks. Pure actual-input TUnit
completion/fact tests plus native transaction/connection/cancellation and healthy
followup tests prove these separate contracts. Seed requires every exact original
ordinal sequence1..4096 before commit and advances state only after joined success.
The future TS30 JSON must encode these compact independent facts; source changes
alone never certify performance or cleanup bounds.
TS009S refines AC-TSI-001: exactly six valid preflight selections have no scenario; exactly thirty valid intensive selections carry one named scenario. Strict route/engine/node/phase/evidence profile and rejection of simultaneous old selection are checked using real configuration input data before resource allocation. Pure invalid cases include null/empty/case/whitespace/numeric/undeclared node/engine/phase/scenario/profile and preflight scenario presence. Configuration inputs are acceptance data, never native proof.

AC-TP009-001 refines TSI001/007: one additive embedded timeseries-contract.json has schema1, exact family/profile, ordered targets KeyLoad/TimescaleDB, nodes1/2/3, five existing named scenarios and the frozen runtime/timing/ACK/copy values in ADR059 TS009P. Its actual UTF8 bytes have a retained SHA256. Strict managed parsing rejects unknown, duplicate, missing, null, wrong-type, oversized/deep and changed values before any native allocation. This is an executable plan contract, not a native result or source/run/job receipt.

AC-TP009-002: typed plan creation returns exactly six unique Preflight cells with null Scenario and thirty unique Intensive cells, each target/node/scenario combination exactly once. IDs are ts-keyload-n1-preflight or ts-timescaledb-n3-Append with invariant node decimals and existing exact scenario names. Every selection passes the existing strict validator. Read/create/plan calls allocate no database/container/client and preserve the old270 contract and production default.

AC-TP009-003: acceptance-derived TUnit uses the actual embedded contract and real JSON input data. Independent literal expected targets/counts/scenarios/values/IDs prove the positive matrix; changes to each field, target/node/scenario array order/duplicates/unknown members, unknown/duplicate/missing/null/wrong-type/malformed/deep/oversized JSON fail. Tests execute only in GitHub normal/scalar suites. Pure plan data cannot satisfy native6/30, numerical coverage or publication.

AC-TP009-004: root owns the exact JSON, csproj embedding and all shared integration. A bounded worker may prepare only the five approved NEW C# source/test candidates under a temporary review directory; no repository/Git/build/test/native/workflow change. Root reviews every candidate, integrates only complete frozen files, then full source/format/governance/delivered-SHA checks. Compatibility is additive; rollback removes only new family plan files/embedding, preserving existing routes, data, APIs and all tests. Genuine native/fault/raw/provider/site gates remain open.

TS007R resource model AC-TSI-001/003/008: actual Aspire models for each1/2/3 selection contain precisely one primary and0/1/2 physical standby nodes, all exact Timescale image/digest, fresh explicit names/data mounts, canonical aliases, single shared secret, all physical endpoints and an explicit Timescale primary bootstrap host. Reject unsupported counts before adding any resources. Test models without starting containers; actual GitHub native readiness/extension/role/quorum/cut/copied-data/public flows remain separately required. Test assertions retain exact literals independent of production constants.

## SG009P process-only test join (Accepted source contract)

AC-SG009P-001 refines SG009001: canonical means lexical ordinal equality of
Path.GetFullPath(options.Directory) and options.Directory, plus fully qualified
and existing directory. Reject dot/dotdot variants before ownership; this makes
no symlink, rename or hostile filesystem resistance claim.
AC-SG009P-002 refines SG009002/003: EVERY guarded call, including all valid,
missing, corrupt and invalid-option tests, executes in the genuine Release
KeyLoad.CrashHost process. Parent creates the fixture's original node.owner.lock
once and opens that existing file exclusively before child start. Child owns
canonical owner.lock. Preserve all original 16 cases and their file/data/failure
assertions; add lexical-path and actual cancellation/kill/reader settlement cases.
No fake exception object is reconstructed in the parent.
AC-SG009P-003: additive private existing-store-inspect mode uses bounded JSON
stdin (4096 chars); CLI contains only original assembly and mode. New internal
ExistingStoreInspectionVariant enum: Normal, NullOptions, EmptyNodeId,
MissingIncarnation, EmptyIncarnation, RelativeDirectory, EmptyDirectory,
NonCanonicalDirectory, ZeroFrameBudget, ZeroSnapshotBudget, Cache, Observer,
WaitBeforeOpen. Request(string Directory, Guid ExpectedNodeId, Guid? Incarnation,
Variant). Variant changes only actual guard inputs, not provider faults. Normal
reads fixed fixture key guard/committed, actual identity without signing key,
position and <=256-byte owned value, then double-disposes. Child constructs real
cache budget/recording observer only for their rejection cases. Receipt schema1
contains Success, NodeId, Incarnation, FormatVersion, Position, Value, bounded
FailureTypes (original top/nested types <=64), ErrorCode (enum name or null),
ObservedStages and RetainedBytes. No exception messages/stacks/path/keys/credentials
are echoed. Guard and cleanup failures preserve original exception objects in
child; bounded type receipt is evidence about them, never their replacement.
Malformed/oversized protocol fails without guard and no exception-secret output.
AC-SG009P-004: parent independently drains stdout/stderr retaining <=8192 chars
per pipe, keeps original PID/executable assembly SHA256/exit, and admits one
child per fixture. Execution deadline30s; cancellation/deadline kills actual
original child tree. Await actual exit/reap and BOTH original readers before
return, releasing owner, deleting files or normal reopen. Cleanup warning bound10s
cannot be mistaken for settlement: on overrun ownership remains retained while
actual exit/readers are awaited; the GitHub job timeout is the final external
bound, and no detached task/result claims successful cleanup. WaitBeforeOpen emits
only fixed stderr readiness then waits; parent cancellation test cancels after
that genuine readiness, verifies original process exit/readers/lock, and normal
reopen/data. Cancellation is an expected harness outcome with no successful guard
receipt. No local execution, mocks, provider corruption injection or weakened
ordinary recovery. These tests are not actual native node stop/restart/copy proof.

|Criterion|Automated proof|Required evidence|
|---|---|---|
|SG009P001|Lexical dot/dotdot rejected with original files retained|Exact-source normal/scalar TUnit|
|SG009P002|All original positive/missing/corrupt/invalid cases through owned real child|Original actual cases, no skipped/direct parent guard call|
|SG009P003|Actual identity/data/errors/cache/observer and bounded pipe protocol|Original Release child assembly and returned safe facts|
|SG009P004|Actual readiness/cancel/kill/exit/readers then ordinary reopen|Normal/scalar plus full recovery; no power-loss/native-copy claim|

Root accepts only this test infrastructure/private guard integration stage;
physical native inspector, node control, applied ACK/copy oracle and benchmark
lifecycle remain separately required. Rollback removes additive mode/helper/ref/
friend/guard branches; existing CrashHost protocols and public storage stay intact.

SG009P frozen wire clarification: internal ExistingStoreInspectorProtocol.JsonOptions
is the shared getter. System.Text.Json camelCase names, case-sensitive properties,
RespectRequiredConstructorParameters=true, RespectNullableAnnotations=true,
UnmappedMemberHandling.Disallow and JsonStringEnumConverter(allowIntegerValues:false).
Require each of directory/expectedNodeId/incarnation/variant exactly once; reject
unknown/duplicate/missing fields and undefined variants before guard. Nullable
incarnation is allowed only as explicit null to exercise the actual input guard.
Valid requests, including expected guard rejection, emit exactly one <=8192-char
JSON line after cleanup with process exit0; Success=false and original failure
facts distinguish rejection. Malformed/oversized protocol exits2 with no stdout
receipt and no exception-secret diagnostics. Stdout has no other text; fixed
stderr ready marker exists only for WaitBeforeOpen. Larger than256-byte read values
fail rather than truncating a successful record; malformed protocol tests retain
original identity/journal bytes and then genuine ordinary reopen.

SG009P type/path clarification: FailureTypes uses each original exception type's
FullName, original top first then actual nested/aggregate causes; ErrorCode is the
actual first KeyLoadException code name. A graph over64 cannot become a truncated
complete receipt: exit2 after available cleanup, no successful protocol evidence.
NonCanonicalDirectory constructs Path.Combine(request.Directory, "."); parent
also exercises dotdot as actual request Directory with Normal. This does not
change filesystem identity or introduce a provider fault callback.

SG009P console ownership: recognized dedicated inspector process captures its
original stdout/stderr writers once, then directs Console.Out/Error to
TextWriter.Null for its entire inspector mode through process exit. It emits
only the safe protocol JSON/fixed readiness directly through those captured
original writers. Do not restore console writers before exit, since native
background diagnostics could race the receipt. Actual exception objects/failure
facts and cleanup remain preserved. This changes only the dedicated inspector
mode, never native factory logging or other CrashHost modes; source-only logging
hygiene does not certify native process settlement.

SG009P stdin settlement clarification: child drains original stdin to EOF while
retaining at most4096 chars and an overflow flag. Overflow rejects before parsing
or native guard; no unbounded buffer or early pipe-close rejection. Parent's
original30s deadline/kill/exit/readers owns the entire read. Parent ready-or-exit
selection must not leave a faulting WaitAsync wrapper detached from actual owned
process/input/readers. A failed post-start handoff retains the started original
process until kill/reap and every initialized original stream task settle.

## SG009P-C2 owned cleanup classification (Accepted)

The source build exposed CA1031 in guarded gate cleanup and generic capture.
Preserve every diagnostic and all original faults: no suppression, severity
change or tautological catch filter. Use the existing source-owned throwing
callback boundary pattern, with a private OwnedCleanupFailureException holding
the same original reference/InnerException. Invoke catches and rethrows that
wrapper; Capture catches ONLY the private wrapper and records its Original,
without Flatten. Guarded gate acquisition runs through this synchronous capture;
ordinary open/dispose remains literal unchanged. Child operation uses that same
private capture for its synchronous actual work and cleanup. Its async dedicated
mode has an analogous private thrown mode-failure wrapper and specific outer
handler for protocol exit2, preserving original exception cause without diagnostics.
These internal wrapper types never replace actual failures in returned type facts.
No operation moves thread, detaches or skips cleanup; no public contract changes.
Allocation exhaustion during wrapper/list construction cannot certify complete
fatal cleanup continuation; original inspector process remains the final owner.
Source/lifetime review and actual corrupt-original process cases are the evidence;
no fake fatal objects/handles or numeric coverage claim. C1 packet remains sealed.
C2 worker owns only revised temp guard cleanup/runtime and child mode/operation
helpers (private nested wrappers allowed); root reviews original identity/flow,
limits/build/format and joins all C1 unchanged files plus parent12/shared5. Actual
normal/scalar/full recovery/RF3 qualification stays required before completion.

## SG009P-T2 process settlement corrections (Accepted)

Independent source review identified readiness matching after complete marker,
kill errors escaping before original joins, and two lexical-case assertions using
an exception type as an ErrorCode. Preserve T1 sealed packet. T2 temp revisions
may touch only parent pipe/session/start-failure/lifetime helpers and the lexical
case test as required. Once ready completes, stop further marker indexing.
Capture original kill failure but ALWAYS await original exit/input/stdout/stderr
under retained outer ownership before propagating all original causes; kill
failure cannot release a live child's owner. Original cleanup10s is warning only,
job timeout final external bound, no detached/fake settlement. Use specific owned
throw/catch boundaries if necessary; keep every analyzer enabled. Lexical failures
have actual ArgumentException type and null ErrorCode; never invented type-as-code.
Review every revised source/hash and actual flow tests in GitHub; source-only
rare kill failure review cannot be labelled a runtime or coverage gate.

SG009P-T2 bound/quality refinement: name the two fixed parent diagnostics, preserve
all configured analyzers and use legitimate owned throw/specific-catch boundaries
for captured cleanup failures rather than unfiltered nonrethrow generic catches.
AC-SG009P-003 includes two actual native-record edges: ordinary fixture commits a
256-byte value and original child returns all256 bytes; commits257 bytes and child
returns actual InvalidDataException/no ErrorCode/no value, without truncation.
After actual exit/readers/outer release, ordinary reopen must retain complete
original256/257 bytes, committed position and identity/journal bytes. These new
positive/negative cases live only in the existing parent data-test file; actual
GitHub normal/scalar execution is required, no fake store/provider or local run.

## SG009P-C3 framework-owned single wrapper (Accepted)

Complete C2 projection build reports CA1064 and three CA1032 diagnostics for the
private custom exception. Preserve all diagnostics/public API boundaries: replace
only those private custom wrappers with framework AggregateException containing
exactly one original exception, as in the existing throwing callback pattern.
Specific outer AggregateException catch extracts exactly its sole immediate
InnerExceptions[0], preserving the same original aggregate/fatal reference and
all nested causes; never Flatten, unwrap recursive native aggregates, or invent
public exception constructors. Private Invoke always creates that one outer
wrapper before its handler, including async dedicated mode. Ordinary runtime and
returned actual failure graph remain unchanged. C1/C2 sealed history remains;
C3 temp changes only guard cleanup and child mode files. Wrapper/list allocation
exhaustion limitation and original process final ownership remain explicit.
Parent T2 uses the same legitimate framework owned-callback classification
pattern where generic nonrethrow catches would violate CA1031, while actual exit/
input/readers remain independently joined before owner release/propagation.

## SG009P-T3 authentic exit and original-error join (Accepted)

AC-SG009P-004 requires positive native exit authority: the original registered
WaitForExitAsync must succeed, or its protected synchronous Process.WaitForExit
fallback must succeed. Faulted/canceled tasks and pipe EOF alone are not exit
proof. ObserveProcessExitAsync(Process,Task?,List<Exception>) returns Task<bool>;
all available original input/stdout/stderr tasks and the original WhenAll task
are independently observed, with reference-deduplicated original causes. Only
positive exit authority plus these joins permits originalTasksJoined and release.
If both authorized native exit observers fail, retain the actual Process, original
failures and outer owner in an explicit awaited fail-stop retention latch until
external GitHub job termination. The infinite delay is expressly UNSETTLED and
never substitutes for an exit task, success receipt or reaping evidence; no retry
or polling loop is authorized. Wrapper/list allocation exhaustion stays an
explicit limitation, and rare OS observer/release failures have source-review
exceptions rather than fabricated fault tests or numerical coverage claims.

Session.SettleAsync(List<Exception>,bool) retains the same original tasks and
records independently captured kill/exit/input/readers. Session.Release(List<Exception>)
releases the actual Process only after authentic joined settlement and preserves
its actual disposal cause. Existing fallback DisposeAsync collects settle/release
causes; no finally may replace a prior primary. Lifetime.RunAsync must explicitly
join its actual primary operation failure, session settlement/release and actual
outer owner release before throwing or returning. Its session async-using is
replaced by this owned join. Startup cleanup follows the same positive-exit rule
and never fills absent task slots with fake completions. WriteRequestAsync joins
actual WriteAsync/FlushAsync and independently captured stdin.Close, preserving
both original causes and actual EOF delivery.

One NEW private ExistingStoreInspectorFailureJoin helper owns Capture(Action,List<Exception>),
ObserveAsync(Task,List<Exception>)->Task<bool> and Throw(List<Exception>). Closed
Invoke/Await boundaries throw one framework AggregateException with the same
original; the outer specific handler extracts only that immediate object. Use
reference identity to deduplicate repeated observations, preserve a sole original
with EDI, and aggregate distinct originals without Flatten. No custom/public
exception type, suppressions, changed analyzer severity, provider doubles or
fake-process fault fixtures. Source-only review binds every rare error branch.

Compiler/style repairs are accepted in the same private parent boundary:
CancellationToken last in all parent/fixture signatures and call sites;
Convert.ToHexStringLower for the actual assembly digest; awaited CancelAsync;
auto-property pipe character count; actual instance-owned Process setup with
awaited startup cleanup and explicit ownership transfer/disposal, preserving all
original registrations; braces and method-group fixes; static fixture directory
helper with updated genuine calls; remove the unused using. These remove no
resource operation or diagnostic. Type/file/function/depth limits remain200/400/50/3.

Exact write ownership: effective T1+T2 Session, SessionStartFailure, Lifetime,
PipeCapture, Process, Fixture, MissingTests and ValidationTests, plus at most the
one NEW FailureJoin helper. All original22 TUnit cases, eight malformed IPC
payloads, lexical corrections and genuine256/257 record/identity/journal/reopen
assertions remain unchanged. Child, storage, shared projects, native benchmark,
workflow, dependencies and Git belong to root or other owners. Worker writes only
a separate temporary T3 packet; final all-source review/build/format/governance
and delivered-SHA Tests normal/scalar/full recovery/RF3 remain required.

## TS009W actual warmup receipts retained (Accepted source contract)

REQ-BC060/061/062 and AC-TSI003/004/005/008 require actual warmup receipt facts
for the later direct native final oracle. AC-TW009-001: create one fixed1280-slot
warmup backing array (5repetitions*256operations) before clocks, initialized with
correct repetition/index/worker and NotStarted. Preserve the same five warmup
workflows and real validated attempt/ACK/sequence/error/latency facts; never infer
successful receipt sequences from operation ordinals or discard completed warmup
ledgers. Each executor writes only its original dedicated256-slot slice using
the existing ledger's publication/duplicate/identity/concurrency contract.

AC-TW009-002: measured50000-slot storage and its original boundaries are unchanged
and separate from warmup1280; later repetition setup cannot overwrite earlier
warmup slots. Unexecuted/failed/canceled warmup slots stay explicit actual outcomes.
A completed RunState.Finish returns the same live-owned ReadOnlyMemory view as a
NEW internal init-only RunResult.WarmupAttempts property. All original workers
have settled before publication. Existing constructor/RunAsync/result schema1
writer behavior remains compatible; no raw wire/host/native/site claim changes.

Exact library edits: TimeSeriesIntensiveAttemptLedger adds CreateWarmupStorage()
and factors its existing canonical initializer into a private bounded helper,
without changing CreateMeasuredStorage/Publish semantics; RunState adds one
WarmupStorage array and binds it on Finish; RepetitionExecutor receives the same
array (before final CancellationToken) and uses repetition*WarmupCount offset;
Runner passes that array. RunResult adds only the init-only property. No target,
callback, mock/fake, native adapter, failure classifier, timer or operation change.

AC-TW009-003 source-level TUnit tests use real ledger/value contracts as pure
acceptance data: all1280 initial identities/not-started positions; five disjoint
warmup slices retaining deliberately non-ordinal supplied sequences/ACKs; measured
storage byte/value equality and separation; missing/duplicate/wrong-identity
rejection and retained failed latency; RunState.Finish exposes exactly the owned
view without fabricating success. Pure inputs are not native executed receipts.
Actual end-to-end warmup receipt validation remains required in all30 later native
cells; no doubles or source tests count as that evidence.

Worker ownership is only the five library files named above and NEW
TimeSeriesIntensiveWarmupStorageTests.cs plus at most one NEW prefixed pure test
helper under the matching UnitTests Intensive slice. Temporary packet only;
original baseline hashes and delta preserved, tests first,200/400/50/3 limits.
Root owns shared docs, all source/build/format/Git and native host/proof/wire joins.
Exact-source Tests normal/scalar and later Benchmarks native6/30 remain gates.

SG009P-T3 original handle binding: borrow and retain the actual outer owner's
SafeFileHandle immediately after the live FileStream is assigned to its owner;
no additional handle ownership. After actual FileStream.DisposeAsync, read only
that retained handle's IsClosed. Never obtain FileStream.SafeFileHandle after
closing: [the .NET10 native strategy getter](https://raw.githubusercontent.com/dotnet/runtime/v10.0.0/src/libraries/System.Private.CoreLib/src/System/IO/Strategies/OSFileStreamStrategy.cs)
performs seek before returning the handle. An initial getter failure remains a
primary original and still joins actual FileStream disposal independently. Root
accepted this normal-path source correction before final T3 seal; no local probe.

AC-TSM009-001 refines TSI001/003/008: actual Timescale Aspire models for1/2/3
must retain the exact registry/image/SHA256 and rendered digest-qualified image,
with Tag null after digest pinning. Preserve the independently expected
tag-plus-digest runner binding, all placement/credential/bootstrap assertions and
all four original failing model cases. Aspire13.6.0 deliberately clears Tag in
[ContainerImageAnnotation.SHA256](https://raw.githubusercontent.com/dotnet/aspire/v13.6.0/src/Aspire.Hosting/ApplicationModel/ContainerImageAnnotation.cs);
the existing tag assertion is an incorrect test oracle, not a dependency defect.
Only the ComparisonTests Timescale resource assertion file may change; no
production/pin/version/native topology or contract change. Pass requires exact-SHA
Benchmarks original model reports with all cases passing; source review/build
alone fails qualification. Existing ADR059 is sufficient; no new ADR is required.
