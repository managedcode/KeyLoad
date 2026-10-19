# Representative scaled workloads

## Execution contract

[ADR-142](../../ADR/ADR-142-shared-live-benchmark-sessions.md) and
[SessionExecution](SessionExecution.md) define the measurement shape: current
measurement jobs share one live database/topology across separate TUnit cases,
measure the initial load, and perform no per-case reset or reseeding. The dataset,
operation-count, capability, correctness, privacy, cancellation, resource,
topology and provenance requirements below are mandatory. The seven document cases do
not complete the required vector, graph, queue, event, time-series or physical
100k-corpus coverage; family integration and exact-source Linux qualification
are separate gates.

Active benchmark datasets are exactly 100k and 1m records for every database. The
active fixture bound is 1m, and each checked raw read invocation consumes 1m
operations. Canonical slice: BenchmarkComparisons. ADR:
[ADR-069](../../ADR/ADR-069-representative-scaled-workloads.md). The workloads use
actual 100K/1M records and at least 100K measured operations per applicable workload
cell, covering sequential/random/ordered/range reads, indexing and complex queries. All
stages remain unqualified until originals exist. The 4096-record raw control and
the database control stay separate. Runtime repairs are specified in
[ScaledRuntimeRepairs](ScaledRuntimeRepairs.md), REQ/AC-SCALE-RT-001..003.

|Requirement|Acceptance|Caller-visible behavior and proof|
|---|---|---|
|REQ-SCALE-001|AC-SCALE-001|Compact deterministic full-index binary inputs, sorted BE keys and full shuffled permutation; independent literal TUnit goldens|
|REQ-SCALE-002|AC-SCALE-002|ExactlyN terminal seed writes and full distinct payload verification, real copy/borrow and scratch lifetime; normal/scalar ZoneTree provider|
|REQ-SCALE-003|AC-SCALE-003|Bounded residency/page/index/process/preparation/cancel/close owners; actual100K/1M generated qualification|
|REQ-SCALE-004|AC-SCALE-004|Every read invocation really performs1M operations over allN keys and consumes exact record identities/checksum; real BDNmetadata and runtimeProcessCore|
|REQ-SCALE-005|AC-SCALE-005|Separate records/calls/iterations, load/oracle/resources/digest, complete8cell/profile/duration gates and original failures|
|REQ-SCALE-006|AC-SCALE-006|Local internal scale originals bound to source/machine; full-database GitHub comparisons use isolated runners and authenticated comparable originals; local evidence never publishes|
|REQ-SCALE-007|AC-SCALE-007|Mandatory subsequent genuine SDK/officialMCP/cluster ordered/index/complex-query supported-peer cohort, exact results/ACK/resources; currently OPEN|
|REQ-SCALE-008|AC-SCALE-008|All current source preserved/periodic main delivery, full real quality/fault/coverage/endurance gates, no fake/skipped/invented success|

```mermaid
flowchart LR
    Rule[Actual scale contract] --> Corpus[Compact generator and pinned keys]
    Corpus --> Z[ZoneTree immutable value arena]
    Z --> Oracle[Read all N distinct full values]
    Oracle --> Calls[1M sequential or shuffled reads per invocation]
    Calls --> Check[Full postverification and resource bounds]
    Check --> Originals[Original BDN and cold manifests]
    Originals --> Review[Local code optimization]
    Query --> Provider[Authenticated GitHub database cohort]
    Rule --> Query[Separate real SDK MCP index and complex queries]
    Provider --> Assessment[Matched hardware and contract assessment]
```

The exact binary, permutation, immutable arena, tier settings, deadlines, execution
observations, 12GiB ceiling/headroom, body/close preservation and BDN job contract are
frozen in ADR-069 and this document. Tests are real and are followed by independent
full-source review and actual normal/scalar+gates. No new dependency or product
API is introduced. Phase A internal raw measurements run locally for optimization.
Ordinary genuine correctness checks belong in CI; internal benchmark jobs, modes and
dependencies are prohibited in benchmarks.yml. Only full-database comparisons and their
preparation/aggregation/website stages use that pipeline.

Backend/public contracts are N/A for Phase A because production APIs/storage remain.
Frontend is N/A until complete genuine scale metrics have an accepted separate
projection; the site schema cannot accept these diagnostics. Library and tests use the
BenchmarkComparisons slice; workflow/helper tooling use the same slice. Phase B covers
actual backend/public-client/query/storage/index behavior and requires its exact
contract before implementation. No surface is silently omitted.

Q1 supports bounded
read-only predicates/equality-prefix indexes, not unrestricted SQL or joins.
Explain must prove real index use; actual results must be independently verified.
Keep authorization, public bounds, cluster ACK, cancellation and original fault rules.

all-record validation and actual residency/peak-memory decide each size's
eligibility. Source capacities are planning arithmetic, not measured fit/RSS. The
profile eliminates duplicate value-corpus retention. Process-kill/cache/local BDN
evidence never proves power-loss durability, full database leadership or production
readiness. Numeric coverage collection is still unavailable and open.

## profile contracts

- Reuse internal RawStorageEngineKind only. ScaledRawStorageCorpus(count,payload)
  accepts1..1,000,000 and32/1024B; qualification datasets are EXACT100K/1M.
  RecordCount/ValueBytes/Key(index)/WriteValue(index,Span<byte>) expose stable
  input metadata, indexed pinned Memory keys and cold deterministic value fill.
  Valid index0..count inclusive; count is unseeded reserved miss. Invalid bounds
  fail before acquisition/allocation. One pinned16*(count+1) key slab; no
  per-record object/Memory tables or retained full/alternate value corpus.
  Corpus, ReadOrder and ValueArena constructors additionally accept an optional
  original CancellationToken. Validate arguments then reject pre-cancellation
  before allocation and check at least every256 fill/shuffle operations. The
  fixture passes one owned linked token and original preparation deadline across
  all stages; never reset that deadline.
- Key format scale-v1: BE64(seed1729) then BE64(index). Value first32 bytes:
  BE64(index), BE64(seed1729), BE32(version1), BE32(payloadBytes), BE64(~index).
  Extra offset j>=32: unchecked byte(1729+index*31L+j*17L+(index>>8)+(index>>16)).
  Exact target span length is required. Full-index identity prevents256-aliasing.
  Literal independent header goldens include0,1,255,256,65535,65536,N-1.
- ScaledRawStorageReadOrder(count) keeps one int[count] shuffled permutation;
  initialize0..N-1, descend i=N-1..1, SplitMix64 state initially1729, add
  0x9e3779b97f4a7c15; xor>>30/multiply0xbf58476d1ce4e5b9; xor>>27/multiply
  0x94d049bb133111eb; xor>>31; j=next%(i+1), swap. Version
  splitmix64-fisher-yates-mod-v1. Golden10-order [3,5,0,1,2,4,8,9,7,6].
  NextSequential/NextRandom separate cursors start0/wrapN. Retain exact digest
  of LE32 permutation bytes; no timed shuffle/RNG/full-corpus allocation.
- Exact helper join API: Corpus.RetainedKeyBytes long; ReadOrder.NextSequential()
  and NextRandom() int, RetainedOrderBytes long and PermutationDigest string;
  ScaledRawStorageValueArena(corpus).Value(index) Memory<byte> for seeded0..N-1
  and RetainedValueBytes long. Arena constructor fills each immutable chunk once;
  all bytes remain unchanged through owner close. Headers/order are separate
  from the control types and never expand their bounds.
- ScaledRawStorageFixture(engine,count,payload,cancellationToken=default) owns
  descriptor/order/engine, one pinned seed/output scratch and one separate
  expected-value scratch. ExactlyN charged terminal successful seed writes;
  further writes are forbidden. The constructor then performs one actual full
  VerifyAll before returning: VerifiedRecords=N, VerificationPasses=1,
  RuntimeReadCalls=N+1 and FullValueDigest is the lowercase SHA256 of complete
  actually read values in ascending record-index order. It never substitutes
  the expected generator digest. Explicit VerifyAll adds a complete N+1-read
  pass; initial load and verification use separate actual untimed durations.
  Read(index) returns the actual BE64 record ID
  after exact size/terminal/found/output-copy checks and equality to requested ID.
  VerifyAll() cold reads all distinctN/full payload bytes plus reserved miss.
  SuccessfulSeedWrites/VerifiedRecords/RetainedInputBytes and actual execution
  residence/resource snapshot are truthful separate counts. ReadNextSequential/
  ReadNextRandom use the corresponding real order. No per-call allocation,
  copied vendor implementation, arbitrary retry, hidden reseed or unsafe epoch.
- Exact fixture join API additionally has Corpus/ReadOrder, TryRead(index,out
  ReadOnlyMemory<byte>) bool including reserved miss, Read(index) ulong and
  ReadNextSequential()/ReadNextRandom() ulong. Capture() returns an immutable
  ScaledRawStorageSnapshot with RecordCount, PayloadBytes, SeedAttempts,
  SuccessfulSeedWrites, VerifiedRecords, VerificationPasses, RuntimeReadCalls,
  RetainedKeyBytes, RetainedValueBytes, RetainedOrderBytes, FullValueDigest,
  PermutationDigest, ProcessPeakBytes and nullable genuine RuntimeResidentRecords,
  Actual resident counts are observed values; no log
  counters or invented unavailable measurements remain. Write/load and full validation duration are
  actual untimed stopwatch observations. ScaledRawStorageSettings validates
  the actual ZoneTree input and process-memory capacity. Root authorizes no public product API. Additional immutable cold
  snapshot fields: SeedElapsedTicks, VerificationElapsedTicks, StopwatchFrequency,
  EffectiveMemoryCapacityBytes, ProcessMemoryCeilingBytes, ProcessWorkingSetBytes
  Effective capacity
  is the actual BCL GC-reported available memory limit, explicitly not free RAM.
- ZoneTree owns immutable value slices in4096-record chunks (max4MiB/chunk),
  kept through tree close. Never retain the reusable generator scratch as a value.
  RetainedValueBytes=N*payload for this owned arena.
  WALNone/compressionNone, mutable boundmax(1000,N+2), no maintainer or timed disk
  path. Require actual mutable/in-memory countN and no frozen/disk records through
  measurement.1000 is the minimum for mini fixtures; qualification usesN+2.
  Full point/value oracle is mandatory even with count.
- Whole child process peak ceiling12GiB; minimum total/cgroup memory eligibility
  ceiling+2GiB headroom. Report real total availability separately from actual
  free/RSS; never call total memory free memory. Smaller direct-unit fixtures
  use a computed bounded projection plus headroom, not mandatory12GiB allocation.
  Their bound adds the actual process baseline peak, keys/order/ZoneTree arena,
  log/index projection and 2GiB headroom, capped at12GiB. Capture and
  compare actual complete-process peak outside timing; the planning projection
  is not measured usage or additional permission to allocate that amount.
  Observe actual peak/RSS outside timing and during bounded cold validation,
  fail capacity/retention mismatch; no automatic quota/profile/count adjustment.
  Preparation20minute monotonic deadline and incoming cancellation checked in
  chunks<=256 operations; do not detach or preempt an original synchronous call.
  Body+independent close errors remain visible; dispose session/output/
  store/settings before arenas/pins/directory. Repeat close retries unreleased owners.

## Acceptance and testing methodology

- AC-SCALE-001 / REQ-SCALE-001: compact exact binary corpus, independent goldens,
  BE ordering, i versus i+256 distinction, bounded invalid spans/indices/counts,
  deterministic full unique shuffle and wrap/digest. Real TUnit corpus/order tests;
  inspect no full-value retention. Wrong goldens/aliases/omissions fail.
- AC-SCALE-002 / REQ-SCALE-002: real engine loads exactlyN, full distinct
  value oracle before/after, missing key, owned scratch and disposal; copied seed
  immutable borrowed ZoneTree values survive every read. Real mini+100K fixtures for both payloads, normal/scalar.
  Any nonterminal/lost/swapped/corrupt result, reset seed quota or released owner fails.
- AC-SCALE-003 / REQ-SCALE-003: frozen tier settings, actual no-eviction/resident
  records/process-memory/resource capture, capacity/cancellation/deadline failure
  before invalid acquisition, finite outer120minute job and owned cleanup. Real
  counter/pre-cancel/disposed tests plus manual vendor close limitation; execution
 100K/1M generated runners prove actual fit. Authored source is not proof.
- AC-SCALE-004 / REQ-SCALE-004: public unsealed ScaledStorageReadBenchmarks,
  separate class name from the RawStorageBenchmarks wildcard, one selected engine/
  count per process. Each SequentialRead/RandomRead invocation executes EXACT1M
  actual reads; every call checks returned full record ID and consumed sum equals
  (1M/N)*N*(N-1)/2. OperationsPerInvoke1M, invocation1/unroll1, .NET10,
  launch1/warmup8/Actual10, retainedResults8..10. Params payload32/1024 and
  selected exact100K/1M (env KEYLOAD_SCALED_STORAGE_RECORD_COUNT, default100K).
  Engine ParamsSource uses KEYLOAD_RAW_STORAGE_ENGINE and accepts only the exact
  lowercase value zonetree (unset also selects zonetree); every other label is
  rejected before acquisition. RecordCount selects only100K or1M, with the
  documented100K default; never silently substitute an engine or count.
  Real metadata and batch/checksum tests, real generated full JSON.
  INTERNAL benchmark Capture() forwards the active genuine fixture snapshot for
  cold correctness tests. Each method must increase actual RuntimeReadCalls by
  exactly1M; checksum/attributes alone are not proof. Generated runtime is.NET10.
  Public Setup validates the selected engine/exact qualification count before
  acquisition; GlobalCleanup performs actual full postverification then owned
  close. Both a verification/body failure and independent close failure remain
  visible. Direct benchmark configuration rejects any other count.
- AC-SCALE-005 / REQ-SCALE-005: report requested, charged seed, distinct verified,
  actual measured operations, warmup/sample/retained counts separately; retain
  untimed load/validation duration/digest/resources/residence. Every actual row
  must have1M operations and>=100ms. Exactly8 one-engine×two-required-record-scale×two-payload×
  two-method cells across the completed scaled cohort; no write rows, mixed/tiny
  profiles, missing/failed/short/default/duplicate cells. Real strict Node parser
  schema vectors are test data, never producer measurements; actual originals are
  the runtime proof. failures stay visible and invalidate that cell/cohort.
- AC-SCALE-006 / REQ-SCALE-006: local PhaseA originals bind the actual source,
  engine/count/payload, machine/runtime/settings, full JSON/CSV/stdout/cold
  manifests and original-file hashes. Sequential owned local processes stage
  100K->1M on the single ZoneTree provider only after the preceding resource/value stage passes.
  Reconcile the full8-cell local profile without publishing it. PhaseB full
  database comparisons require same-source/run/attempt authenticated GitHub
  executor/job/artifact/ZIP/file hashes and matched actual hardware/resources,
  topology, durability and workloads on isolated runners. Existing270/site
  contracts stay unchanged; no raw/internal jobs or modes in benchmarks.yml and
  no local measurements in site figures.
- AC-SCALE-007 / REQ-SCALE-007: actual product/full-service ordered/range, index
  preparation/index lookup verified by Explain and bounded complex Q1 queries
  through authorized SDK/officialMCP; exact ordered result/count/digest;100K+
  measured calls per supported cell at both required record scales (100K and1M). Real cluster process/container and
  peer tests, authorization/negative/cancel/error/recovery cases, equivalent
  index/schema/ACK/topology/resource settings. Unsupported APIs are explicit,
  source limitations are never relabelled successes. This PhaseB criterion is OPEN.
- AC-SCALE-008 / REQ-SCALE-008: entire current eligible source preserved and
  periodically main-committed/pushed; full relevant build/unit/scalar/format/
  governance, actual recovery/cluster and required numeric coverage qualified without
  skip/fake/invented evidence. Baseline actual failures tracked one-by-one. Raw
  cache success does not close full database endurance/powerloss/performance goal.

## Automated acceptance mapping

|AC|Automated evidence and verification|Explicit exception/remaining gate|
|---|---|---|
|001|ScaledRawStorageCorpusTests/ReadOrderTests literal vectors, full uniqueness and actual buffer/span errors, normal/scalar TUnit|Managed/execution/RSS model is only planning arithmetic|
|002/003|ScaledRawStorageFixtureTests/SettingsTests real mini+100K ZoneTree fixtures/copy/pins/resources/owner/cancel; generated mini,100K and1M fixtures; mini remains a control and only100K/1M qualify|Unobserved vendor constructor/pending/finite close faults remain manual limitation, not invented injected failures|
|004|ScaledRawStorageBenchmarkTests real metadata and1M checksum methods plus genuine BDN subprocess originals|Per-operation bounds/oracle overhead remains included and labelled|
|005|ScaledRawStorageReportTests real bounded Node pure parser success/error vectors; genuine generated reports|Parser fixture values never publish or authenticate a provider|
|006|Root local original8cell scale receipt; PhaseB authenticated full-database provider originals in Benchmarks|Manual source/machine/config/statistical and hardware reconciliation; no local website substitution or internal Benchmarks jobs|
|007|Future frozen PhaseB real SDK/MCP/public query/container tests and supported peer cohorts|Not yet implemented/qualified; no raw point scan surrogate|
|008|Full solution Release; real TUnit normal/scalar/recovery/cluster; dotnet format verify; node governance; exact-source Actions originals|Coverage collector/baseline still unavailable, no numeric pass claim|

Rollback removes only the scale fixtures/profile/helpers/tests/docs coherently; no
persisted data/public API migration is involved. Original 4096/provider receipts remain
immutable.

## Construction and close ownership (AC-SCALE-003)

A constructor that throws while close also fails must not lose retryable
owned resources. Missing vendor-fault evidence is not an
exception to keeping our successfully acquired handles, pins and arenas alive.
Before joining, construct managed owners without acquiring resources;
assign the core to the fixture and each engine to the core before invoking its
one-shot Initialize. Assign each successfully returned vendor handle before the
next acquisition. A vendor constructor's own inaccessible partial internals
remain an explicit unobserved vendor limitation; our returned handles do not.

Each scaled diagnostic process admits exactly one active fixture. A bounded
static ownership slot roots that exact fixture before acquisition and remains
charged until actual dependency-ordered close succeeds. Admission rejects a
second fixture before its acquisition. Constructor failure performs one
owned close attempt, preserves the primary and every independent close failure,
and retains the charged fixture if close fails. An explicit repeat-close entry
may retry only that failed owner; it never closes a healthy active fixture,
reseeds, retries a measured operation or silently admits a replacement. The
outer120minute process owner remains mandatory for an unreturned vendor call.
Retained ownership must be observable as actual state, not an invented
recovery success. No unbounded retired-owner list or fire-and-forget cleanup.
The exact friend-only retry API is ScaledRawStorageFixture.HasRetainedFailedOwner
and ScaledRawStorageFixture.RetryFailedOwnerClose(): bool. The property reports
only an actually retained failed-construction owner. Retry returns false and does
no close when there is no such owner, when the current owner is healthy, or while
an original close attempt is already running; it returns true only after one
eligible original close returns successfully. Actual close exceptions propagate
and retain the owner for a later explicit attempt. A bounded synchronized slot
selects/charges the attempt, then releases its lock before the close; it
never admits concurrent closes or holds a global lock across a vendor call.
Healthy-owner tests assert both APIs leave the real active store readable and
preserve its operation counters. This does not fabricate a vendor-failure flow.

Core construction only prepares managed buffers. The seed scratch is pinned.
Initialize covers original seeding, the initial full oracle and residence
checks in the existing20minute scope. Successful cleanup performs the full
postoracle once before closing owners; repeated close does not read a
partially closed engine. Failed initialization closes without inventing a full
postoracle. Release the linked lifetime and ownership slot only after core close.
All actual timed methods verify their returned checksum after the complete1M
calls. read counts increase only for a valid admitted point call.

ScaledRawStorageOwnerTests prove: the genuine live owner remains readable after a
rejected second acquisition, original seed/read counts remain truthful, actual close
admits one new owner, and repeat close remains safe for the ZoneTree fixture.
The invalid/pre-cancel/metadata/full-value tests remain. Real vendor close-failure
proof is unavailable; no injected failure or fabricated fault closes that
exception. Independent source review checks every ownership path.

Complete initial, explicit and final full-value verification also captures the
actual engine's residence/address/page/index bounds before successful oracle
metadata is published. Nonfatal oracle and independent close errors remain
visible. Test cleanup runs after a completed body or caught nonfatal failure;
excluded fatal unwind does not allocate an aggregate or call owned cleanup.
Actual fatal termination and vendor close failures remain manual, unobserved
evidence gaps; a throwing test double is not acceptable proof.

The independent pure parser contract is
[ScaledReportQualification](ScaledReportQualification.md): REQ/AC-SCALE-RPT-001..004
map the strict four-cell report and two-report cohort to the intended bounded Node
tests. Parser vectors are controlled data. This stage does not authenticate cold
manifests, process ownership, machine identity or measured observations.

## Final gates

Release solution build with real analyzers and numeric limits; focused normal/scalar
then full relevant regressions; formatter; governance validator. Actual process
recovery and Docker/Aspire replicated-cluster tests via .NET/official MCP, genuine
database comparison, numeric coverage and endurance/fault qualification remain
required. Short raw control rows are unqualified speed evidence, and quotas are never
raised without diagnosis.
