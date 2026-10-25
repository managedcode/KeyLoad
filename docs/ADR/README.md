# Architecture decisions

These documents describe the current design. Acceptance of a decision does not
prove implementation or runtime qualification. Feature specifications and exact
source artifacts own those checks.

| Decision |
|---|
| [ADR-001: Atomic partition identity and affinity](ADR-001-partition-identity-affinity.md) |
| [ADR-002: Command identity and persisted idempotency](ADR-002-command-idempotency.md) |
| [ADR-003: Durability profile and acknowledgement barrier](ADR-003-durability-ack-barrier.md) |
| [ADR-004: Committed read views and pagination cuts](ADR-004-committed-read-views.md) |
| [ADR-005: Canonical namespaces and ordered key codec](ADR-005-canonical-keyspace-codec.md) |
| [ADR-006: Strict synchronous indexes and derived projections](ADR-006-strict-derived-indexes.md) |
| [ADR-008: Backup cuts and independent log retention](ADR-008-backup-log-retention.md) |
| [ADR-009: Search provider and projection boundaries](ADR-009-search-provider-boundaries.md) |
| [ADR-010: Bounded query execution and security barriers](ADR-010-query-budgets-security.md) |
| [ADR-011: Current format and restore authority](ADR-011-current-format.md) |
| [ADR-012: Versioned KeyLoad SQL subset](ADR-012-sql-dialect.md) |
| [ADR-013: One authorized query AST across interfaces](ADR-013-authorized-query-ast.md) |
| [ADR-014: Persisted principals, scoped RBAC, and row policy](ADR-014-principals-rbac-row-policy.md) |
| [ADR-015: Sensitive-data omission, use, and lineage](ADR-015-sensitive-data-lineage.md) |
| [ADR-018: Global per-modality windows and weighted rank fusion](ADR-018-global-rank-fusion.md) |
| [ADR-019: Managed-first ANN provider qualification](ADR-019-managed-ann.md) |
| [ADR-020: Independent bounded query contexts](ADR-020-independent-query-contexts.md) |
| [ADR-021: PostgreSQL comparison baseline](ADR-021-comparable-postgres-baseline.md) |
| [ADR-022: Policy epoch reauthorization at request and page boundaries](ADR-022-policy-epoch-revocation.md) |
| [ADR-023: Separate lifecycle and authority for journals, events, CDC, and queues](ADR-023-journal-authority.md) |
| [ADR-024: Catalog-bound shared TransactionDomain](ADR-024-transaction-domain-binding.md) |
| [ADR-025: Stream revisions and stable per-atomic event-feed positions](ADR-025-event-revision-feed-positions.md) |
| [ADR-026: At-least-once delivery with fenced leases and inbox effects](ADR-026-fenced-delivery-inbox.md) |
| [ADR-027: Durable consumer groups with contiguous checkpoints](ADR-027-contiguous-subscription-checkpoints.md) |
| [ADR-028: Persisted scheduling with logged evaluation time](ADR-028-persisted-scheduling-time.md) |
| [ADR-029: Event and message classification with safe diagnostics](ADR-029-event-message-classification.md) |
| [ADR-030: Retention pins and paused restore of an older cut](ADR-030-retention-paused-restore.md) |
| [ADR-031: Modular all-in-one deployment and capability manifest](ADR-031-modular-all-in-one-resource-isolation.md) |
| [ADR-032: Current repository governance](ADR-032-mcaf-governance.md) |
| [ADR-033: source-owned code quality and evidence gates](ADR-033-code-quality.md) |
| [ADR-034: database comparisons and authenticated results](ADR-034-cluster-comparisons.md) |
| [ADR-035: bounded operation work and resource lifetime](ADR-035-memory-performance.md) |
| [ADR-036: Orleans foundation](ADR-036-orleans-foundation.md) |
| [ADR-037: Complete catalogue of features and architecture decisions](ADR-037-documentation-coverage.md) |
| [ADR-038: Canonical chunked BlobStorage and bounded reads](ADR-038-chunked-blob-storage.md) |
| [ADR-039: Official MCP SDK and agent access to canonical operations](ADR-039-official-mcp-agent-api.md) |
| [ADR-040: static product website, conceptual Three.js and qualified evidence](ADR-040-static-site-threejs-evidence.md) |
| [ADR-041: read-only public collections with stable wire bytes](ADR-041-read-only-public-collections.md) |
| [ADR-043: Comparison library and executable host](ADR-043-comparison-library-host.md) |
| [ADR-044: immutable comparative harness contracts](ADR-044-benchmark-immutable-contracts.md) |
| [ADR-045: Owned PostgreSQL comparison schemas and constant commands](ADR-045-postgres-schema-ownership.md) |
| [ADR-046: Cohesive node-local storage owners under numeric gates](ADR-046-storage-private-owners.md) |
| [ADR-047: public embedded benchmark scenarios and typed executable](ADR-047-embedded-benchmark-host.md) |
| [ADR-048: bounded owned storage metadata](ADR-048-bounded-storage-metadata.md) |
| [ADR-049: Genuine Neo4j harness and acknowledged cleanup ownership](ADR-049-genuine-neo4j-harness.md) |
| [ADR-050: TimeSeries and Timescale comparison](ADR-050-timeseries-timescale-comparison.md) |
| [ADR-051: same-origin read-only administration console](ADR-051-admin-dashboard.md) |
| [ADR-052: bounded TimeSeries latest and raw aggregates](ADR-052-timeseries-bounded-aggregates.md) |
| [ADR-053: one light KeyLoad visual identity for the console and the site](ADR-053-unified-visual-identity.md) |
| [ADR-054: Central SQL over canonical database operations](ADR-054-central-sql.md) |
| [ADR-055: Typed relational rows in canonical entity storage](ADR-055-typed-relational-rows.md) |
| [ADR-056: Isolated Linux comparison cells and complete evidence aggregation](ADR-056-isolated-linux-comparison-cells.md) |
| [ADR-057: Orleans binary atomic WAL](ADR-057-orleans-atomic-wal.md) |
| [ADR-058: Orleans-coordinated disposable cache memory](ADR-058-orleans-coordinated-cache-memory.md) |
| [ADR-059: Isolated intensive TimeSeries comparison](ADR-059-isolated-intensive-timeseries.md) |
| [ADR-060: generated internal serialization](ADR-060-internal-serialization.md) |
| [ADR-062: Workflow responsibility and qualification boundaries](ADR-062-workflow-separation.md) |
| [ADR-063: Bounded database phase profiling](ADR-063-bounded-database-phase-profiling.md) |
| [ADR-064: Four workflows and source-bound release delivery](ADR-064-workflow-release-delivery.md) |
| [ADR-065: Full SQL syntax and client protocol](ADR-065-full-sql-client-compatibility.md) |
| [ADR-067: One composable database for AI agents](ADR-067-composable-agent-database.md) |
| [ADR-068: Current benchmark gate and evidence repair](ADR-068-benchmark-gate-repair.md) |
| [ADR-069: representative scaled workload qualification](ADR-069-representative-scaled-workloads.md) |
| [ADR-071: Canonical ZoneTree storage and full-text providers](ADR-071-canonical-zonetree-providers.md) |
| [ADR-072: Authorized SQL event and queue read views](ADR-072-authorized-sql-model-views.md) |
| [ADR-073: Logged bounded time-series retention](ADR-073-logged-series-retention.md) |
| [ADR-075: Bounded aggregate snapshots and pure replay](ADR-075-bounded-aggregate-snapshot-replay.md) |
| [ADR-076: Publish only the complete current comparison cohort](ADR-076-current-cohort-publication.md) |
| [ADR-078: Bounded full-text candidate generations](ADR-078-full-text-projection.md) |
| [ADR-079 — Bounded lossless time-series chunk codec qualification](ADR-079-lossless-series-chunk-codecs.md) |
| [ADR-080: Isolate benchmark failures from optional website publication](ADR-080-benchmark-failure-isolation.md) |
| [ADR-081 — Awaited bounded search execution](ADR-081-awaited-search-execution.md) |
| [ADR-082: CQRS streams through ManagedCode.Communication](ADR-082-cqrs-streams.md) |
| [ADR-083 — Bounded CQRS HTTP consumption](ADR-083-cqrs-http-transport-bounds.md) |
| [ADR-085: Visible bounded benchmark progress](ADR-085-live-benchmark-progress.md) |
| [ADR-086: Stop owned test workloads on terminal Aspire failure](ADR-086-aspire-terminal-failure.md) |
| [ADR-087: Filtered retrieval in the authorized search cut](ADR-087-filtered-retrieval.md) |
| [ADR-088: Durable remote queue intent and receipt](ADR-088-remote-queue-transfers.md) |
| [ADR-089: Canonical guarded vector projection lineage](ADR-089-event-projection-lineage.md) |
| [ADR-090: Same-cut graph scope, retrieval and expansion](ADR-090-graph-search.md) |
| [ADR-092: Logged-time recurring occurrences and saga timeout CAS](ADR-092-recurring-saga.md) |
| [ADR-093: Compare-and-set resource field policies](ADR-093-resource-policy-updates.md) |
| [ADR-094: Orleans StatelessWorker coordination of due queue, recurring and saga work](ADR-094-orleans-due-coordination.md) |
| [ADR-095: Bounded index generation leases and retirement](ADR-095-online-generation-leases.md) |
| [ADR-096: Bounded comparable global modality windows before fusion](ADR-096-global-branch-windows.md) |
| [ADR-097: Provider-owned read-cut leases](ADR-097-read-cut-leases.md) |
| [ADR-098: Bounded authorized shortest paths](ADR-098-bounded-graph-paths.md) |
| [ADR-100: Internal bounded Q1 merge across local atomic partitions](ADR-100-local-partition-query-merge.md) |
| [ADR-102: Cross-partition graph edge delivery](ADR-102-cross-partition-graph-edges.md) |
| [ADR-103: Bounded scaled database comparison stage](ADR-103-scaled-fair-comparisons.md) |
| [ADR-104: ManagedCode gateway and graph-based MCP discovery](ADR-104-mcp-gateway-tool-discovery.md) |
| [ADR-105: Published NuGet dependency refresh](ADR-105-published-nuget-upgrade.md) |
| [ADR-107: Elastic License 2.0](ADR-107-elastic-license.md) |
| [ADR-108: Typed synchronization and Orleans state ownership](ADR-108-typed-synchronization.md) |
| [ADR-109: vector qualification and additional comparison databases](ADR-109-vector-comparisons.md) |
| [ADR-110: Orleans primitives used by KeyLoad](ADR-110-orleans-execution-primitives.md) |
| [ADR-111: Semantic rules for magic runtime values](ADR-111-magic-runtime-values.md) |
| [ADR-112: Independent Website publication with optional benchmarks](ADR-112-independent-website-publication.md) |
| [ADR-113: General runtime literals and centralized typed options](ADR-113-centralized-runtime-options.md) |
| [ADR-114: verified backup artifact publication](ADR-114-verified-artifact-publication.md) |
| [ADR-115: TimeProvider ownership](ADR-115-time-provider.md) |
| [ADR-116: one current format before the first release](ADR-116-first-release-current-format.md) |
| [ADR-117: TUnit CI entry](ADR-117-tunit-ci-entry.md) |
| [ADR-118: bounded typed-row INNER JOIN](ADR-118-bounded-relational-inner-join.md) |
| [ADR-120: bounded persisted time-series rollups](ADR-120-bounded-persisted-series-rollups.md) |
| [ADR-121: exported telemetry privacy at the shared service boundary](ADR-121-exported-telemetry-privacy.md) |
| [ADR-122: single and three-node comparable benchmark methodology](ADR-122-benchmark-methodology.md) |
| [ADR-124: TimeSeries persisted chunk lifecycle](ADR-124-time-series-chunks.md) |
| [ADR-125: One Orleans grain per client connection](ADR-125-connection-owned-execution.md) |
| [ADR-126: Current ZoneTree checksum profile](ADR-126-current-zonetree-checksums.md) |
| [ADR-127: bounded current BM25 corpus statistics](ADR-127-persisted-bm25-corpus-statistics.md) |
| [ADR-130: Microsoft ScriptDOM owns T-SQL syntax](ADR-130-microsoft-scriptdom-tsql.md) |
| [ADR-131: Explicit queue dispatch-state compare-and-set](ADR-131-queue-dispatch-state-cas.md) |
| [ADR-132: CREATE TABLE and caller-stable SQL command identity](ADR-132-create-table-command-identity.md) |
| [ADR-135: write-through journal admission](ADR-135-write-through-journal-admission.md) |
| [ADR-136: Orleans is the cluster; every silo owns its own ZoneTree](ADR-136-orleans-partition-ownership.md) |
| [ADR-137: Schema-visible safe projection and restricted field binding](ADR-137-schema-visible-projection.md) |
| [ADR-140: Deterministic first issuance of new Analytics command identity](ADR-140-new-analytics-command-identity.md) |
| [ADR-142: One live benchmark session per database and topology](ADR-142-shared-live-benchmark-sessions.md) |
| [ADR-143: Silo count and replica count](ADR-143-independent-silo-replica-topology.md) |
| [ADR-144: Typed Orleans request envelopes](ADR-144-typed-request-envelopes.md) |
| [ADR-145: No request throttling](ADR-145-no-request-throttling.md) |
| [ADR-146: Group commit](ADR-146-group-commit.md) |
| [ADR-147: Id-ordered query continuation](ADR-147-id-ordered-query-continuation.md) |
| [ADR-148: Parallel CI shards](ADR-148-parallel-ci-shards.md) |
| [ADR-149: No operation limits](ADR-149-no-operation-limits.md) |
