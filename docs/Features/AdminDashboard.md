# AdminDashboard

The owner-requested shared favicon export and noindex continuation is specified
by [SiteMetadata](BenchmarkComparisons/SiteMetadata.md), REQ-SEO-001/005 and
AC-SEO-001/006 under ADR053. It adds only finite public icon GET/HEAD assets;
all operational authorization and existing console contracts remain required.

This is the durable requirements and acceptance contract; the decision and implementation contract are in [ADR-051](../ADR/ADR-051-admin-dashboard.md). Goal: attractive runtime administration with sizes, throughput, tables/collections, queues and files.

| Requirement | Acceptance | Intended automated proof |
|---|---|---|
| REQ-AD-001 persisted administrator authorization and grain isolation | AC-AD-001 | AdminCatalogTests, AdminDashboardClusterTests |
| REQ-AD-002 honest bounded physical storage observation | AC-AD-002 | AdminStorageObservationTests, AdminDashboardClusterTests |
| REQ-AD-003 actual process HTTP throughput/duration counters | AC-AD-003 | AdminHttpMetricsTests, AdminDashboardBrowserTests |
| REQ-AD-004 bounded metadata/catalog and authorized document/blob browsing | AC-AD-004 | AdminCatalogTests, AdminDashboardClusterTests, browser |
| REQ-AD-005 non-consuming queue counters/metadata | AC-AD-005 | AdminQueueTests, AdminDashboardClusterTests |
| REQ-AD-006 accessible responsive usable administration console | AC-AD-006 | AdminDashboardBrowserTests and explicit subjective screenshot review |
| REQ-AD-007 integrated .NET/official MCP callers and honest qualification | AC-AD-007 | MCP inventory and cluster tests; canonical GitHub gates |
| REQ-AD-008 bounded failed-request log for operators | AC-AD-008 | AdminHttpMetricsTests, AdminHttpMetricsMiddlewareTests, AdminDashboardClusterTests |
| REQ-AD-009 cluster nodes as reported by Orleans membership for the node view | AC-AD-009 | AdminDashboardClusterTests (SDK and official MCP) |
| REQ-AD-010 light console in the shared KeyLoad identity (ADR-053) | AC-AD-010 | AdminDashboardBrowserTests, SiteBrandParityTests; desktop/mobile review |

```mermaid
flowchart LR
    Operator[Operator browser or SDK] --> API[Same origin admin API]
    API --> Auth[Persisted credential authentication]
    Auth --> Conn[Connection grain]
    Conn --> Authority[Fresh persisted administrator authorization]
    Authority --> Catalog[Bounded catalog and queue read through partition grains]
    Authority --> Node[Silo-local node observer]
    Node --> Files[Node local file lengths]
    Node --> Counters[Process HTTP observations]
    API --> Console[Static admin assets]
```

## Canonical slice map

- Public DTO/protocol: `src/KeyLoad.Abstractions/AdminDashboard/`.
- Gated catalog/queue behavior: `src/KeyLoad.Core/AdminDashboard/`.
- Grain composition: the caller's connection grain, the existing read enum and the silo-local node administration service; no new durable grain state or grain-owned filesystem.
- API, observations, exact static hosting and frontend: `src/KeyLoad.Server/AdminDashboard/`, with frontend `Assets/` fully colocated executable artifacts. Existing shared server configuration is composition only.
- .NET SDK: `src/KeyLoad.Client/AdminDashboard/`; official MCP/agent declarations consume these same typed capabilities.
- Tests: `tests/KeyLoad.UnitTests/AdminDashboard/`, `tests/KeyLoad.IntegrationTests/AdminDashboard/`.
- Infrastructure: the existing unit, recovery and Aspire cluster gates; public `site/` and Pages publication N/A because operational administration is hosted by the database server, not the public benchmark site.
- Persisted schema/migrations N/A: observation uses existing records and writes no new database state. Benchmark workload N/A: observation does not establish performance superiority.

Empty/loading/error, stale/disconnected observations, unauthorized/revoked callers, invalid scope/limits, bounded pagination, due-but-unswept queues, changed files, cancellation and reconnect resets are required flows. Physical bytes have separate canonical/journal/backups categories; published blob lengths are logical metadata. No sum across replica copies is labelled unique database size. Runtime values are actual current observations, independent of benchmark GitHub evidence.

## Scope, actors and boundaries

Goal: a polished same-origin database console at `/admin`, showing real cluster node metrics, database and file sizes, throughput, collections/tables, document rows, blob metadata and non-consuming queue inspection. Canonical feature: `AdminDashboard`.

In scope: read-only runtime administration, authenticated typed HTTP/.NET SDK/official MCP operations, bounded pages and live observation. Out of scope: schema/data edits, queue receive/ack/redrive, backup buttons, public Pages publication, new frontend frameworks/packages, SMID implementation, changes to durability, replication or routing ownership, and performance supremacy. Existing database operations remain their owning slices.

Actors: an operator with a current persisted administrator principal, an unauthorized/revoked/expired caller, and an unauthenticated visitor loading only the credential-free static shell. Authorization is reloaded freshly on the connection grain. Credentials exist only in browser memory; requests use same-origin Bearer headers. User enters tenant/database and atomic partition key; a resource supplies its persisted transaction-domain identity. A catalog resource is not a physical partition. Scope defaults are editable conveniences, not claims about all database tenants.


## Criteria and pass/fail


- **AC-AD-001 / REQ-AD-001**: `/admin` and a finite exact static-asset whitelist load without a key; every dashboard data operation requires current persisted administrator authority. Missing credential fails unauthenticated; nonadmin/revoked/expired keys fail with safe errors. No trusted client roles, broad `/admin` authentication exemption, secrets, absolute filesystem paths, localStorage/sessionStorage or credential-bearing URLs. Dashboard APIs execute on the caller's connection grain with current read cuts.
- **AC-AD-002 / REQ-AD-002**: snapshot identifies the actual executing node, incarnation, capture time, applied/read generation, configured copy count (`KeyLoad:ReplicaCount`) and admission occupancy. File lengths distinguish canonical database files, backups and the observed node total; these are physical observations, not unique logical/cluster bytes. Enumeration visits at most 2,048 entries, lasts at most a cooperative 250 ms budget, returns at most 200 file summaries, honors cancellation and never follows symlinks. Missing/changing/unreadable files or exceeded bounds are explicit incomplete/unavailable observations, never a fabricated complete zero. One concurrent filesystem observation per node; saturation fails promptly. These observational reads do not own files or mutate storage.
- **AC-AD-003 / REQ-AD-003**: process-local HTTP counters capture completed public API requests, failed requests and elapsed duration without high-cardinality labels/payloads; dashboard polling, admission/status observations, static, health and internal routes are excluded. UI computes HTTP requests/s and average response duration only from two samples of the same node/process and monotonic counters, retaining at most 120 samples. New process, failed observation and reconnect reset/mark history unavailable. Admission occupancy is not throughput; cluster throughput is not inferred from one node.
- **AC-AD-004 / REQ-AD-004**: catalog uses explicit tenant/database and exclusive resource-name continuation, 1..100 limit (default 50), gated read plus bounded byte/cancellation budget. It returns metadata only (name/kind/domain/schema/index count/paused), no keys or policies. Collection rows use existing authorized query AST/cursor, 25-row pages; blob pages use existing authorized metadata listing and expose actual logical published length. Empty/missing/unsupported kinds and invalid scope/limits produce clear states. Text renders via safe DOM APIs; stored HTML is text, not executable markup.
- **AC-AD-005 / REQ-AD-005**: queue page reads persisted counters and at most 100 metadata records (default 50), with exclusive message-ID continuation; messages expose lifecycle, attempts, versions and times without payload/header/lease-owner tokens. Inspection never receives, sweeps, claims, ACKs or changes ready sequence/counters/commit position. Due scheduled and expired leases remain their persisted states until ordinary queue work transitions them. Stored and in-flight counts are exact persisted counters; page state counts are not presented as total Ready/DLQ counts.
- **AC-AD-006 / REQ-AD-006**: console provides coherent grouped navigation in a floating command bar (overview, collections, queues, files, cluster and the ADR-053 additions), readable metric cards and measured throughput plot, scope controls, manual refresh and bounded automatic refresh, paginated tables, document JSON details, empty/loading/unauthorized/disconnected/error states. Desktop and 390 px mobile layouts fit without page overflow; keyboard navigation, visible focus, labels, status announcements and reduced-motion behavior work. Disconnect clears credential and all sensitive DOM/history; one in-flight refresh per session and abort/epoch handling prevent stale post-disconnect renders. Background tabs suspend polling; overlapping polls cannot accumulate.
- **AC-AD-007 / REQ-AD-007**: all three new reads are available through typed .NET SDK and explicit official MCP catalog/agent gateway with read-only/nondestructive hints and unchanged prior operation semantics. TUnit/Microsoft.Testing.Platform regressions use actual file-backed stores and actual Docker/Aspire cluster + real .NET/official MCP clients; browser tests use the real server/browser, no mocked API or doubles. Exact-SHA GitHub build/format/analyzer/governance/unit/recovery/cluster/browser receipts and artifacts must exist before qualification is claimed. Required coverage thresholds stay mandatory; absent collector/result is an open gate, not success.

- **AC-AD-008 / REQ-AD-008**: `AdminHttpSnapshot.RecentFailures` keeps the newest 50 failed `/v1` and `/mcp` requests (status ≥ 400, aborted or faulted), newest first. Each entry holds the completion time, normalised method (unknown methods become `OTHER`), matched route template (or `unmatched`), status, aborted flag and duration.
  - It never holds a raw path, query, payload, header or credential.
  - Successful and excluded (dashboard polling, admission, status, static, health) requests are never logged.
  - The log resets with the process. The Errors view filters 4xx/5xx/aborted and clears on disconnect.
- **AC-AD-009 / REQ-AD-009**: the snapshot carries the executing node identity and the cluster nodes reported by Orleans membership.
  - The Nodes view draws the executing node and the remaining nodes, and labels other nodes as not observed from this node. Membership never implies peer health.
- **AC-AD-010 / REQ-AD-010**: the console uses the shared brand (ADR-053) in a light theme with grouped navigation:
  - Monitoring: overview, performance, errors.
  - Data: catalog, collections, queues, blob storage.
  - Infrastructure: nodes, disk usage.
  - It also has KPI sparklines, stacked request activity with a crosshair tooltip, a storage donut, admission meters, catalog kind chips (page counts, never totals) and a disk category bar with largest files.
  - All AC-AD-006 hooks and behaviours stay intact. No horizontal overflow at 1440 and 390 px. A mobile menu toggle exposes `aria-expanded`.
    - Control-room layout:
    - the `<aside>` is a floating command bar with three segmented clusters; at ≤1320 px only icons show, plus the label of the active view; at ≤760 px `#menu-toggle` opens the clusters as a sheet;
    - an editorial masthead holds `#view-title`, `#refresh` and `#disconnect`;
    - a pulse strip shows the four KPIs;
    - graphite instrument panels hold activity, throughput, events and cluster nodes;
    - a fixed status bar holds `#connection-state`, `#session-node` and `#captured-at`;
    - the connect screen is split, and `#details-dialog` is a right-hand inspector sheet.
  - Every id, `data-view`/`data-goto` hook and pinned count is unchanged.

## Criterion-to-test matrix

| Criteria | Test/level | Assertions and verification |
|---|---|---|
| 001 | AdminCatalogTests / unit real store; AdminDashboardClusterTests / cluster; AdminDashboardBrowserTests / browser | admin vs member/revoked/missing credentials; exact static whitelist; request IDs; no secret or traversal; unit and cluster suites |
| 002 | AdminStorageObservationTests / real files; AdminDashboardClusterTests | actual known file lengths, limits, incomplete symlink/error/cancel, node identities, physical accounting; unit and cluster suites |
| 003 | AdminHttpMetricsTests / real counters; AdminDashboardBrowserTests / real API traffic | monotonic successes/errors/time, exclusion and reset, bounded chart samples; cluster browser suite |
| 004 | AdminCatalogTests / real store; AdminDashboardClusterTests; browser | pagination positive/zero/over-limit/cross-tenant scopes, real document/blob flow and hostile text; unit and cluster suites |
| 005 | AdminQueueTests / real store; AdminDashboardClusterTests | empty and populated lanes, metadata pagination, state/counter/read-position unchanged; unit and cluster suites |
| 006 | AdminDashboardBrowserTests / real cluster + actual headless browser | navigation, credential disconnect, empty/error/loading, document viewing, viewport overflow, keyboard; cluster browser suite; manual visual inspection adds design evidence only |
| 007 | AdminDashboardClusterTests with real SDK/official MCP operations | discovered operation metadata, real catalog/nonconsuming-queue agreement and persisted auth failure; all canonical gates |
| 008 | AdminHttpMetricsTests, AdminHttpMetricsMiddlewareTests / unit; AdminDashboardClusterTests / cluster | bound, order, exact fields, template-not-path, exclusions, real SDK failure appears; unit and cluster suites |
| 009 | AdminDashboardClusterTests / cluster SDK and official MCP | all cluster nodes appear, the executing node is identified, SDK and MCP agree |
| 010 | AdminDashboardBrowserTests / real Chrome; SiteBrandParityTests / pages | navigation across all nine views, overflow, reduced motion, brand parity; screenshots reviewed |

Manual exception: subjective visual polish is inspected via desktop/mobile screenshots; it does not replace functional browser qualification. Current dashboard reads use the existing persisted model and dependencies. ADR-051 defines the implementation contract. Exported telemetry privacy uses REQ/AC-AUTH-TELEMETRY-001 and [ADR-121](../ADR/ADR-121-exported-telemetry-privacy.md). Existing bounded dashboard counters/failure rows remain observable and never become raw log/trace payload. The actual Kestrel administration → denial → healthy catalog case supplements existing cluster administrator/counter cases; the export privacy gate is closed only by that case.
