# Memory and read-work repair evidence

Status: in progress. The complete scope is [ResourceExecution](../Features/ResourceExecution.md),
[ADR-035](../ADR/ADR-035-memory-performance.md) and [AC-MP-001..012](../ADR/ADR-035-memory-performance.md).
This inventory distinguishes authored repairs from compiled source and runtime qualification.
No row is closed by a worker report, an old-main CI result or a development build.

Latest completed candidate: [run37005805424](https://github.com/managedcode/KeyLoad/actions/runs/37005805424)
at `6949fa0c3099443c6f34f91245ab8064ef22c63c`. Full solution build, formatter,
governance and all88 analyzer cases passed on all three OSes. Unit770 cases had
51/53/50 failures on Ubuntu/macOS/Windows; RF3 passed6/26 and comparisons2/4.
Recovery and measured profiles did not execute. The [runtime ledger](runtime-qualification-20261002.md)
retains each actual case and artifact receipt. Later tombstone, MCP, real fixture,
bounded problem-body and progressive report repairs remain unqualified; this
current record supersedes older source-cut descriptions as a status summary.

## Located inventory

| Issue | Owning path / slice | Required result and present state |
|---|---|---|
| MP-001 | `Storage/StorageContracts.cs`, `ZoneTreeStore.cs`, `Features/StorageRecovery/` | Charge borrowed point/range work before copying; ordered staged/baseline merge and examined-entry bounds. Source and real-store regressions authored; integrated compile and CI pending. |
| MP-002 | `Core/ReadExecutionBudget.cs`, `Features/ResourceExecution/` | Avoid complete result-byte arrays and reject excess reads before owned copies. Counting stream and allocation/boundary cases authored; serializer token buffers remain; CI pending. |
| MP-003 | `QueryEngine.cs`, `Features/QueryExecution/` | Charge index plus referenced document bytes; avoid full raw/eligible pages. Source and point/index/full-scan regressions authored. SQL tokenization and parsing still have no mid-operation budget/deadline/cancellation checks; current cancellation case is pre-cancel only. Compile and CI pending. |
| MP-004 | `QueryAst.cs`, `PreparedQuery.cs`, `JsonData.cs` | Prepare JSON paths/order keys once and retain only the requested ordered prefix. Source and ordering/cursor regressions authored; CI pending. |
| MP-005 | `LiveQueries.cs`, `Server/ApiEndpoints.cs` | Propagate cancellation through SQL/AST/live work and HTTP callers; preserve cursor/cut authority. Query and HTTP forwarding authored; native routing join and RF3 qualification pending. |
| MP-006 | `SearchEngine.cs`, `Core/Documents.cs` | Selected document projection is bounded, but each text/vector branch still constructs and sorts a full score array and hybrid fusion retains a full-union score map. Exact BM25/RRF, authorization and ranking tests exist; branch metadata remains O(matches), so a semantics-preserving bounded-rank design and measurement are open. Compile/CI and in-flight cancellation proof pending. |
| MP-007 | `SearchEngine.Similarity` | Validate/prepare query vector once and compute only the selected metric, preserving floating-point accumulation and exact ranks. Source and metric/rank regressions authored; compile/CI pending. |
| MP-008 | `SearchEngine.cs`, query admission | Bound simultaneous SQL/AST/live/search work through one DatabaseEngine-owned atomic fail-fast allowance. Source and real-store saturation/cancellation/release/independent-engine regressions authored and reviewed; combined compile and CI pending. |
| MP-009 | `Events.cs`, `Features/EventStreams/` | Bound head/range/projected output and complete page metadata before retaining excess events. Source and seven real-store regressions authored; CI pending. |
| MP-010 | `GraphAndSeries.cs`, `Features/TimeSeries/` | Seek the inclusive UTC range end; avoid reading/decoding later samples; bound projected output. Source and range/allocation/authorization cases authored; CI pending. |
| MP-011 | `GraphAndSeries.cs`, GraphTraversal | Reuse visibility decisions inside one read cut, including hidden vertices; count every examined edge and never expand hidden vertices. Source and real-store convergence/hidden/cycle/result-boundary regressions authored; targeted new-file formatter passes; compile/CI pending. |
| MP-012 | `AtomicMutationApplication.cs`, `Features/DocumentStorage/DocumentMutationCommands.cs` | One prior-image lookup and direct constructed final image replace two redundant reads/decodes; exact-capacity immutable receipt builder. Source joined/reviewed and Core builds cleanly. Real-store count/byte, exact outbox/sequential-image and failure/rollback cases authored; CI runtime proof pending. |
| MP-013 | `Messaging.cs`, `EventSources.cs` | Counters, stored body lengths and validated processing leases are reused, but queue receive first copies a provider page of up to 256 entries before applying `MaxMessages`; duplicate topic publication also makes an owned lookup solely to decode its sequence. Preserve FIFO/expired-item skipping/lease/quota/atomic failure while reducing examined/copied records. Add deterministic logical-read assertions; compile/CI pending. |
| MP-014 | `ProjectionOutbox.cs` | `StoredOutboxEntry.StoredBytes` is available but discarded; accepted entries are serialized again only to size the returned batch. Carry the observed raw length, stop purge at the requested cut and prepare partition identity once. Add real-store read/byte proof while preserving contiguous progress, checksum, corruption, rollback and retention authority. Integrated compile/CI pending. |
| MP-015 | `JsonData.cs`, `Features/ResourceExecution/` | Stream exact canonical JSON to the fingerprint hash, share pooled input serialization and avoid Validate's ToArray clone. Golden Unicode/raw-number/null/error, real replay and allocation cases authored; integration/new-file targeted formatter passes; compile/CI pending. Largest token/property-sort allocations remain. |
| MP-016 | `Features/StorageRecovery/ZoneTreeCheckpointManager.cs` | Snapshot validation currently reads/verifies the source, copies and rereads it under the write gate, then generation open/recovery scans and validates it again. Keep checksum, format and replay guarantees; first establish immutable file identity/lifetime and recovery cut before removing any full pass or moving I/O outside the gate. No read-pass counter test yet. Backup hashing is already stream-based; a former whole-journal-array concern is obsolete. |
| MP-017 | `Features/ClusterReplication/ReplicaProtocolCodec.cs`, `DurableReplicaLog.cs`, `ReplicaOperationJsonConverter.cs` | Span-based decode exists, but log reads still own encoded arrays and operation decode materializes base64 bytes plus a UTF-16 payload string. Preserve exact replay/corruption behavior; add large-payload replay allocation proof. Current 8 MiB test covers round-trip/limits only. |
| MP-018 | `Features/ClusterReplication/DurableReplicaLog.cs` | Snapshot publication updates durable metadata but does not physically remove covered log entries; obsolete suffixes are only hidden. Design crash-safe deletion around the authoritative published cut and update tests that assert stale keys remain. Recovery/fault proof pending. |
| MP-019 | `Features/ClusterReplication/ReplicaSnapshotFiles.cs`, `ReplicaSnapshotStore.cs` | Prior published snapshots and interrupted temporary files are not swept. Define publication ownership, in-flight reader leases and bounded retention; add cleanup/crash fault cases without deleting the authoritative image. Existing tests currently expect old images to remain. |
| MP-020 | `Features/ClusterReplication/`, `Features/ClusterRouting/` | Per-message limits exist (16 MiB append, 256 KiB chunks, 4 GiB snapshot) and transfers serialize per receiver, but no aggregate in-flight bytes/concurrency admission runs before Orleans request materialization. Keep independent control-plane progress through the protocol gate; real RF3 saturation/cancellation proof pending. |
| MP-021 | `Features/ClusterReplication/DurableReplicaLog.cs`, `ReplicaAppendReceiver.cs`, `ReplicaAppendCompiler.cs` | Append validation serializes entries, log append encodes them again, loaded records are reserialized, and sizing reads serialize each record. Preserve committed-prefix/conflict/append limits while reusing raw bytes and lengths. No full-path allocation/serialization-count regression yet. |
| MP-022 | `Features/ClusterReplication/ReplicaMaterializer.cs` | Snapshot capture holds the apply gate through file creation and verification; install holds it through checksum, verification, canonical install and publication. Move work only after establishing an immutable exact cut and revalidating that cut under the gate. Concurrent-apply/gate-residency evidence pending. |
| MP-023 | `Features/ClusterReplication/ReplicaRpcClient.cs`, Orleans replica service and dispatcher | Active Orleans transport converts payload bytes to strings and back in both request and response paths. Remove the duplicate byte/UTF-16 copy while preserving authenticated exact bytes and append bounds; current exact-byte tests are narrow, with no end-to-end transfer allocation proof. |
| MP-024 | `Features/ClusterRouting/`, `ServerApplication.cs`, `PartitionHost.cs` | Orleans is source-wired: the server starts node-local hosts and the silo; separate request grains, distributed directory, activation repartitioner and three Docker Aspire nodes are configured. No RF3 integration scenario forces activation migration and verifies host ownership plus durable outcome. The retained-replica scenario already waits for `*.snapshot`, which matches published GUID-based snapshot names; the earlier `*-*` concern was stale inventory text. |
| MP-025 | official MCP surface and RF3 public operations | Exercise all affected flows through the official MCP C# client and .NET SDK in Docker/Aspire. Required product/qualification surface is incomplete. |
| MP-026 | `Client/KeyLoadClient.cs`, `Features/ClientApi/` | Stream successful JSON after headers and cap problem bodies. Source and real-Kestrel transport/cancellation/error cases authored; Client passes a normal dependency-enabled strict build; CI runtime proof pending. |
| MP-027 | `Features/BenchmarkComparisons/ComparisonRunner.cs`, dataset and host profile loading | Individual settings allow 1M documents/operations, 20 repetitions and concurrency 128 without a combined checked corpus/attempt budget. Dataset construction eagerly materializes documents and graph caches; every case retains operation-sized samples/results. Bound aggregate corpus bytes, targets, attempts and input before allocation while preserving allowed CI profiles. |
| MP-028 | `ReportWriter.cs`, `ReportCsvWriter.cs`, Markdown/error helpers | Native SerializeAsync was prevented from progressive Cases/Samples output by synchronous immutable converters. Private async write views and stronger early real-file cancellation/schema regressions are authored under TASK-MP-008C, awaiting full CI. CSV has fixed buffers; Markdown still materializes a full string, general report/error bounds and interrupted CSV proof remain open. |
| MP-029 | `ClientResourceSampler.cs`, database-node instrumentation | Current sampler measures load-generator CPU/allocations/working set only; the Markdown label `RSS` is inaccurate for working set. Add database-node allocations/GC/working set and export logical ZoneTree counters; label physical I/O only if measured. No server export/resource proof yet. |
| MP-030 | comparison profiles and RF3 suites | Current short 128-document/60-operation profiles do not establish over-RAM behavior, server resource ceilings, admission/fault behavior, or matched-topology baseline/candidate performance. Add repeated controlled profiles with exact workload, source SHA, topology and report provenance before any speed claim. |
| MP-031 | `.github/workflows/ci.yml`, CodeQuality | Preserve strict analyzer severities and complete build/formatter gates. Abstractions/Core/ZoneTree/Client/Query/Security/CLI/Comparisons pass ordinary numeric-enabled source builds; full solution and formatter remain open. Four source-owned numeric complexity rules are configured and compiled, with complete-graph/fixture CI pending. Coverage collection, container export and matched numeric baseline remain unconfigured. |
| MP-032 | existing comparison/clock/HTTP-handler test doubles | Replace existing non-real qualification paths with actual contracts; do not use those cases as runtime evidence. Migration and CI pending. |
| MP-033 | comparison workload/report provenance | Requested/effective graph size is correctly labeled (256 requested, 32 effective vertices, 96 edges for 32 documents). Current runner emits schema 3 while the real comparison test still asserts schema 2; `ComparisonReport.Provenance` is declared but not assigned, and source SHA has an `unrecorded` fallback. Deterministic input identities are reconstructable but not stored in samples. Resolve schema/provenance and keep current-source qualification and physical node metrics separate. |
| MP-034 | `EventSources.cs`, source-read callers | Borrowed source records, one authority/head cut, operation-wide budget/cancellation and complete result-envelope counting are authored with real-store identity/cursor/raw-limit/error cases. Lead review added observer-cancellation-before-consumer coverage. Core compile passed; native forwarding and CI pending. |
| MP-035 | `Abstractions/JsonDefaults`, `Core/OperationProtocol.cs`, `CommandOutcomes.cs` | Bounded configured UTF8 pool and cached-receive request reuse are source-joined; exact replacement/strict errors/fingerprints, parse order and full-loan clearing reviewed. Ten public protocol/allocation/real-store replay cases are authored; actual Core dependency build has zero warnings/errors and scoped formatter verification passes. Test-project compilation and exact-SHA GitHub qualification remain pending. Fresh authorization-to-dispatch duplicate DTO parsing remains open, with no opaque cache authorized. |

The inventory includes active and pending migration code deliberately. A defect in
pending source is not a measured live bottleneck; an authored repair in active
source is not verified improvement. Owned storage copies remain necessary where
callers may retain or mutate arrays. No ManagedCode dependency defect has been
established by this review.

## Evidence and qualification boundary

The historical dirty-checkout development build below is
[strict-solution-dirty-refresh-20261002.log](../../artifacts/memory-performance/strict-solution-dirty-refresh-20261002.log),
SHA256 `531721ea2d7f42f598dbf4814e0830fcb744bd4ce0086bc5bdad052ca2db0d5d`.
`dotnet build KeyLoad.slnx --no-restore --configuration Release` failed with
271 errors and zero warnings: UnitTests92, ComparisonTests90, SiteTests80,
IntegrationTests7 and CrashHost2. The production libraries and comparison host
compiled in this cut. The errors are primarily strict analyzer/style findings and
test-source/compiler defects; no tests executed. Later all25-project builds and
the actual candidate CI above supersede this build cut as current status, while
preserving its historical evidence. Neither establishes measured memory behavior.

The post-plan baseline is [GitHub Actions run 36936319423](https://github.com/managedcode/KeyLoad/actions/runs/36936319423),
SHA `9c570f8c33a7a9667507a8e1c0ca68860de3be45`. All four jobs succeeded:
[comparison](https://github.com/managedcode/KeyLoad/actions/runs/36936319423/job/110617318251),
[macOS](https://github.com/managedcode/KeyLoad/actions/runs/36936319423/job/110617318434),
[Ubuntu](https://github.com/managedcode/KeyLoad/actions/runs/36936319423/job/110617318473),
[Windows](https://github.com/managedcode/KeyLoad/actions/runs/36936319423/job/110617318485).
Logs and the smoke/1-KiB/16-KiB JSON/Markdown/CSV comparison artifacts were
downloaded under ignored `artifacts/memory-performance/baseline-36936319423/`.
Each OS passed 112 unit, 6 integration and 70 recovery tests with zero failures or
skips. Three comparison invocations passed; their matrix is asymmetric and metrics
are sampled client allocations/CPU/RSS. The smoke metadata discrepancy is MP-033.
The ignored `baseline-summary.json` records exact test/result details.
That SHA predates the dirty analyzer, TUnit, Orleans and resource repairs. It is
the existing-main baseline only and cannot qualify these changes.

ADR-041 and tests-first strict serialization govern the collection/consumer
prerequisite migration. Earlier clean prerequisite builds and the459-error cut
`strict-solution-after-core-join.log` predate the numeric rules and later repairs.
The current full solution cut `strict-solution-private-storage-join.log` contains
54errors/0warnings across SiteTests36/CLI13/Query3/Security1/Comparisons1; dependency
failures leave later graphs uncompiled, so it is not an exhaustive inventory.
Subsequent ordinary numeric-enabled Storage, Security, Query, CLI and Comparisons
builds are0errors/0warnings, alongside the earlier Abstractions/Core/Client joins.
The actual comparison host build has8ownership/cleanup errors and zero warnings;
its accepted private-owner repair and first-authored real process regression are
in progress. All24projects have verified strict central properties;22consumers
attach the custom analyzer. Exact logs and historical stage boundaries are linked
from [CodeQuality evidence](code-quality.md). Concurrent source is preserved;
dependency/reference-disabled compilations never prove the combined source.
Targeted formatter checks passed for joined scopes; full formatter, actual
coverage/export, complete numeric-rule execution and runtime qualification remain
open. No local tests, recovery qualifications or load benchmarks were executed.

The integration owner must close each row with exact candidate SHA/run/job/test
and resource-artifact evidence, then update acceptance, feature/ADR and this record.
Coverage thresholds, complexity, genuine RF3/MCP behavior, endurance and fault
gates remain mandatory. Process-kill proof cannot establish power-loss durability;
no performance improvement or production-readiness claim is made here.
