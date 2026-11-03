# Analytics read-work investigation

The slow path was in KeyLoad's capture: `AnalyticsStoreScan.Read` traversed and
deserialized every event in the store before applying the timestamp predicate.
Each aggregate also ingests its authorized capture into a disposable native
analytics generation. The source repair seeks a timestamp index and resolves
matching IDs through canonical records. The engine, authorization, complete
result checks and fresh publication validation remain shared.

## Matched local read comparison

Both variants used published ZoneTree 2.1.1, the same physical 200,000-event store,
the same 10,000-event window, one caller and 200 complete aggregate operations.
The existing diagnostic entry invokes the real Core capture and native analytics
generation directly, without HTTP, Orleans or Aspire. It uses the comparison
dataset and verifies all ten aggregate groups and unchanged canonical position.
The control temporarily restored only the previous full-scan read method in the
primary checkout. The final indexed method was restored afterward. Neither a
product switch nor a second runtime implementation was added.

| Per capture | Full-scan control | Timestamp seek |
| --- | ---: | ---: |
| Examined events | 200,000 | 10,000 |
| Mean allocated bytes | 321,314,655 | 20,535,957 |
| Complete verified operations | 200 | 200 |

Examined work fell 95%; capture allocations fell 93.61%. This is a local
operation-allocation result. It is not a KeyLoad network-throughput multiplier.
One control interval overlapped a read-only filesystem metadata walk. Original
wall times and individual measurements are retained as diagnostic data; they
are not claimed as a controlled clean timing benchmark. Public comparisons still
require the released-image Linux workflow.

The source/DLL/PDB identities and original JSON are under the executing diagnostic
assembly's output:
`tests/KeyLoad.FunctionalTests/bin/Debug/net10.0/performance-investigation/documents-20261101/bin/Debug/net10.0/analytics-index-20261102T2143/`.
`indexed-clean.json` and `full-scan-control.json` retain all 200 observations;
their identity files include hashes, PIDs and the timing limitation. Original
control/indexed Core binaries and symbols are retained there.

## Allocation follow-up

A separate 500-operation indexed run retained exact complete-group and canonical
position checks. A five-second counter window and one-second allocation capture
targeted PID 30223 on .NET 10.0.14/macOS Arm64. All 3,353 allocation events were
accepted and persisted, without rejected or pending events. This instrumented
run is separate from the two 200-operation observations.

The retained allocation call tree resolves `AnalyticsStoreMapping.Event` to
per-event `Where`/`Select`/`Distinct` setup and repeated native-key construction.
The final implementation prepares selected field mappings once per query, before
projecting rows. Its focused correctness checks and reprofile belong to the
combined current-source qualification; the table above measures the timestamp
seek before this additional preparation change.

The native `EnrichmentPipeline.Enrich` path also accounts for a substantial
allocation site because KeyLoad currently creates and populates a fresh derived
generation for each aggregate. That observation concerns KeyLoad's usage; it is
not evidence of an engine defect. Native defensive event copies remain in use.

The disposable generation's raw-event, rollup and session stores also select the
public `WriteAheadLogMode.None` option. The generation is rebuilt from canonical
records and supplies no persistence acknowledgement. Canonical storage keeps its
own WAL contract. This removes redundant WAL work from temporary query materialization;
its full operation tests and measurements belong to the combined batch.

The diagnostic server classified the managed process as NativeAOT after an empty
CPU-sampling probe. The actual target runs a managed DLL through `dotnet`, and
its counters include positive JIT activity. Allocation and counter captures
completed; no CPU self-time conclusion is drawn from that capability inference.
An earlier capture attempt ended after its short-lived target exited and remains
explicitly interrupted in the local evidence.

## Qualification

The initial 2.1.1/index batch passed 34 focused TUnit cases: timestamp seeks in
both storage modes, CAS/update/delete/cold reads, current row/field policy,
complete aggregates, native MVCC, WAL admission without metadata rewrite and
joined async-snapshot disposal. Later current-contract cleanup and field-mapping
preparation require the combined build and focused tests. Complete functional,
process recovery, real cluster, coverage, release and benchmark gates remain
distinct. The native engine baseline is documented in
[Native ZoneTree measurements](zonetree-native-measurements.md).

## Final combined-source check

After current-contract cleanup, prepared event mappings and disposable-generation
WAL changes, a fresh 200,000-event store completed 200 full operations. Each read
examined exactly 10,000 events and verified all ten groups and unchanged canonical
position. Mean capture allocation was 20,376,154 bytes and generation allocation
was 23,107,246 bytes. This is a combined-source observation, not an isolated
mapper or WAL comparison; concurrent validation also prevents a clean timing claim.

A separate 500-operation run completed the same correctness checks. Its one-second
allocation collection returned 3,036 events. The durable queue retained 936 and
rejected 2,100, so that persisted capture is incomplete. The original returned
summary and live mapping subtree are retained with that limitation. A later repeat
found the target had exited and is explicitly recorded as interrupted. Counters
and allocation collection overlapped; no CPU self-time conclusion is made.

Current results, compiled identities and original diagnostic responses are beside
the diagnostic binaries in `analytics-final-20261103/`, including
`analytics-complete-200.json`, `analytics-profiled-500.json` and
`execution-identity.json`. The process/source identity is separate from the earlier
full-scan comparison. All 154 local process-recovery cases and 568 analyzer cases
passed on the combined storage/contract batch; the complete functional execution
and exact-source Linux release qualification remain separate.
