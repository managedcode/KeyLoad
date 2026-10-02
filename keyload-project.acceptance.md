# KeyLoad project delivery acceptance

## Goal and user-visible outcome

Deliver the v0.3 KeyLoad database as one coherent repository implementation with an honest capability ledger and evidence-backed release. Users can run the RF3 server, use the real .NET SDK and official MCP C# SDK, and exercise the implemented document, query/search, graph/time-series, event, messaging, security, backup, and chunked blob APIs under the published contracts.

## Scope and assumptions

The product contract is `docs/design/architecture-v0.3.uk.md`; attached Markdown is identical. The v0.3 backlog KL-001…KL-104, current accepted ADRs, root `AGENTS.md`, and `docs/implementation/status.json` define sequencing and claims. The checkout already contains broad changes; preserve all unrelated work. Delivery to stable `main` is authorized by root policy. GitHub Actions remains the only qualification environment for TUnit, recovery, and integration suites.

The owner has identified SMID as a highest-priority system workstream. No matching
definition appears in the checked-in or attached v0.3 specification, so its exact
scope is an open owner clarification under AC-011. Do not guess the acronym or
count the general performance requirement under AC-010 as implementation of SMID.

In scope: all features promised by the current capability manifest and release slice; Orleans-only RF3 topology; node-local storage authority; persisted credentials/policies; typed .NET and official MCP C# public flows; simple agent API; public chunked blob upload, range reads, authorization and restore behavior; centrally pinned .NET 10 dependencies without `packages.lock.json`; README/site/status, build/analyzer/format/governance and exact GitHub evidence; system-wide operation efficiency and scale evidence under AC-010; approved ManagedCode dependency releases when required; and the separately qualified TimescaleDB/ManagedCode.TimeSeries comparison profile in [its acceptance](timeseries-comparison.acceptance.md).

Out of scope: claiming every optional backlog item is production-qualified before its gate; using an in-memory/single-node replacement; client-supplied trusted roles; local tests/recovery/load runs; package substitution or a consumer workaround for an owned dependency defect; power-loss or production-readiness claims without the explicit gates.

## Actors and boundaries

- SDK/MCP/agent callers authenticate as public principals; server-side persisted credentials and policies define roles and resource access.
- ASP.NET Core validates bounded requests and forwards one request to its own Orleans request grain.
- Orleans directory and activation migration locate routing activations. Commands go to replica/partition grains and then node-local `PartitionHost` owners.
- `PartitionHost` owns storage and deterministic ordered application. Atomic partition identity remains distinct from physical host/replica placement.
- Aspire starts the actual Docker RF3 resources. GitHub Actions owns all qualification and evidence artifacts.
- ManagedCode dependency fixes belong to the sibling source repository and require its tests, canonical patch release, successful publication, and verified NuGet availability before KeyLoad updates.

## Acceptance criteria

### AC-001 — Architecture and traceability

**Pass:** the delivered source follows v0.3; every advertised feature maps through stable REQ/AC, ADR, backlog task, owning slice and evidence; the inventory and `docs/implementation/status.json` identify unfinished and blocked work accurately. Atomic/physical identities and node-local `PartitionHost` ownership are documented and represented in source.

**Fail:** a feature is implemented outside its owning slice without an accepted migration contract, an unverified feature is marked complete, or the architecture is replaced by a single-node/in-memory demonstration.

**Evidence:** `docs/Architecture.md`, `docs/Features/`, `docs/ADR/`, `docs/implementation/documentation-coverage.json`, `docs/implementation/status.json`; GitHub governance and complete solution build.

### AC-002 — Orleans RF3 authority and request routing

**Pass:** Orleans is the only cluster and RPC foundation; each public request has an independent request grain; the distributed grain directory and activation repartitioning/migration are enabled; RF3 command outcomes remain durable across leader loss and process restart; storage ownership remains node-local when activations move. There are no DotNext cluster references or primary-silo-only runtime paths.

**Fail:** Orleans activation state becomes the only copy of storage, client retries can duplicate committed effects, routing has no persisted authority/fencing, or a required real-cluster suite is skipped.

**Evidence:** source/package inventory; GitHub unit, real-process recovery, and Docker/Aspire RF3 tests through the .NET SDK and official MCP SDK; preserve exact SHA, run/job URLs, and artifacts.

### AC-003 — Public data, query, event, messaging, and search contracts

**Pass:** every capability advertised by the release manifest has typed .NET and official MCP operations with equivalent validation, authorization, success, denial, retry, cancellation, and error outcomes. Atomic document/event/queue work and persisted delivery/idempotency contracts follow their accepted feature specs.

**Fail:** MCP tools or SDK methods are placeholders, trust roles supplied by callers, bypass canonical authorization, or advertise semantics that the owner test evidence has not proved.

**Evidence:** feature-mapped TUnit cases plus GitHub RF3 caller flows using real SDKs; feature docs, capability manifest, and comparison records.

### AC-004 — Chunked public blob storage and agent API

**Pass:** public blobs support the specified ordered/retried chunk upload lifecycle, completion integrity checks, partial/range reads, metadata, listing, deletion/reclamation, bounded resource quotas, persisted authority and restore normalization. The simple agent API and official MCP server expose the accepted operations through normal request authorization.

**Fail:** private snapshot chunks stand in for public blob APIs; partial uploads become visible; quota/ACL errors leak data or mutate state; or only the .NET client is covered while official MCP behavior is absent.

**Evidence:** BlobStorage TUnit tests with real ZoneTree state and GitHub Docker/Aspire RF3 SDK/MCP tests. No mocks/fakes.

### AC-005 — Runtime, package, and owned dependency policy

**Pass:** all projects target .NET 10/C# 14, packages are centrally pinned, no `packages.lock.json` is generated/committed, and current build/style/analyzer rules remain enabled. ManagedCode packages are reused where their contracts fit. An owned dependency defect is repaired and published through its owner repository before the consuming pin changes.

**Fail:** a lock file is introduced, analyzer severity is weakened, package versions are scattered, or an unpublished/local package/workaround is treated as delivery.

**Evidence:** project/package inventory, source-owner release and feed verification records, exact-SHA solution build.

### AC-006 — Repository quality gates

**Pass:** the exact delivered SHA has a successful Release solution build with warnings-as-errors, required format verification, real governance/analyzer fixtures, TUnit unit suites, and no skips or weakened assertions.

**Fail:** compile/analyzer/format failures remain, local runs are substituted for required CI, coverage or complexity is claimed without its configured collector/fixture and exact evidence, or failures are hidden by suppressions.

**Evidence:** canonical `.github/workflows/ci.yml` run and uploaded compiler/test artifacts; static Node governance output is labeled static validation only.

### AC-007 — Recovery, cluster, and capability qualification

**Pass:** GitHub Actions runs real process-recovery tests and Docker/Aspire RF3 tests through the real .NET SDK and official MCP C# SDK; failures, leader loss, retries, blob reads, authorization, and required operation parity are asserted. Each public capability is marked qualified only to the fault model actually exercised.

**Fail:** any required suite is skipped or only host-process/in-memory/single-node execution is shown; `kill -9` is described as power-loss proof; production readiness is claimed without endurance, total-reset, restore, and fault gates.

**Evidence:** exact SHA/run/job/artifacts and honest capability status; remaining production gates are explicit.

### AC-008 — Documentation and remote delivery

**Pass:** README, site, architecture map, feature specs, ADRs, and status ledger agree about implemented behavior and qualification. Scoped changes are committed and pushed to protected `main` under the standing authorization; GitHub Actions and any package publication complete successfully.

**Fail:** a commit/push is reported as completion without CI/publication/feed verification; unrelated changes are lost; branch protections are bypassed; or local artifacts are claimed as remote evidence.

**Evidence:** commit SHA, GitHub PR/workflow URLs or protected-main commit reference, published NuGet version where applicable, and artifact links.

### AC-009 — Persistent time-series comparison and ManagedCode library use

**Pass:** benchmark-mode Aspire starts a digest-pinned TimescaleDB service; the same deterministic UTC samples are exercised against KeyLoad's actual RF3 .NET SDK and the Timescale hypertable through Npgsql; the published ManagedCode.TimeSeries library is used for a separately labeled in-memory aggregation arm. Exact outputs match the oracle and the report states each arm's persistence/replication/acknowledgement guarantees. The existing nine-engine schema3 matrix and support counts remain unchanged.

**Fail:** Timescale is only a declared-but-never-exercised container; test data/workloads differ; the memory library is presented as persistent storage; a performance winner or guarantee equivalence is inferred; required CI is skipped; or the existing comparison schema/counts change to fit this arm.

**Evidence:** REQ-TSC-001..006 / AC-TSC-001..006, REQ-BC-026 / AC-BC-026, REQ-SERIES-007 / AC-SERIES-007, [ADR-050](docs/ADR/ADR-050-timeseries-timescale-comparison.md), real GitHub Aspire comparison artifact at the delivered SHA.

### AC-010 — System-wide operation efficiency and cluster scalability

**Pass:** every advertised public operation maps from the capability manifest to its owning feature's correctness, resource and fault budgets. Each operation family has an explicit representative workload and measurable latency, throughput, allocation, memory, contention and backlog limits where applicable; Orleans request isolation and RF3 routing/failover are measured under the real topology. Flow control is measured for the actual stream contract in use, with persisted KeyLoad EventStreams kept distinct from any Orleans runtime Streams provider. Optimization preserves results, authorization, atomic outcomes and recovery. Performance changes are compared with repeated identical inputs, topology and acknowledgement/read guarantees, and the exact delivered SHA's GitHub artifacts contain the measurements. Feature-specific numeric targets are set in their own acceptance criteria rather than inferred from one aggregate benchmark.

**Fail:** an operation family has no resource/performance boundary, throughput is reported without completed-work or acknowledgement semantics, client-process metrics are labeled as server metrics, an isolated benchmark is used to imply whole-system scalability, or performance improvements are claimed without matched exact-SHA evidence.

**Evidence:** [AC-MP-001..012](memory-performance.acceptance.md), each owning feature's performance and failure tests, real Docker/Aspire RF3 comparison profiles, and immutable GitHub JSON artifacts with source SHA, workload, topology and guarantee metadata. See [ADR-035](docs/ADR/ADR-035-memory-performance.md); endurance and power-loss evidence remain separate gates.

### AC-011 — Owner-priority SMID scope and delivery

**Pass:** the owner-defined meaning and boundary of SMID are captured in one
canonical feature slice with stable REQ/AC criteria, an ADR or documented reason
why existing decisions suffice, ordered implementation tasks, and a real test
matrix. The SMID implementation preserves the one-request Orleans grain and RF3
ownership rules, and its operation paths satisfy the per-feature resource,
correctness and measured-scale contracts in AC-010. The exact delivered SHA has
the required GitHub build and feature qualification.

**Fail:** the acronym is expanded by guesswork, the work remains absent from the
feature/status map, generic performance work is presented as SMID completion, or
secondary work is counted as satisfying this owner-priority requirement.

**Evidence:** owner clarification and reviewed feature/ADR/task/test traceability,
then the exact-SHA GitHub TUnit/recovery/RF3 evidence defined by that feature.
Automated test details are intentionally not invented before the subsystem is
identified; AC-011 remains open until that clarification and its real test plan
exist.

## Criterion-to-test and evidence matrix

| Criterion | Automated evidence | Level and required assertions | Verification |
|---|---|---|---|
| AC-001 | Governance inventory and analyzer fixtures; all feature-specific tests | Repository/static; one canonical slice, complete task map, truthful status | `node scripts/Features/RepositoryGovernance/verify.mjs`; full CI build/tests |
| AC-002 | Cluster routing/membership tests, real process recovery, RF3 request/migration/failover tests | TUnit unit, child-process, Docker/Aspire multi-silo; no lost acknowledged outcome or storage ownership drift | GitHub `ci.yml`; actual .NET and MCP clients |
| AC-003 | Owning feature unit/API tests and RF3 parity scenarios | TUnit unit and real client integration; success/denial/error/retry invariants | GitHub `ci.yml`; compare .NET and MCP outcomes |
| AC-004 | BlobStorage real-store lifecycle/range/quota/ACL/restore tests | TUnit unit/recovery/RF3; exact bytes, no partial visibility, bounded quotas, identical SDK/MCP authority | GitHub `ci.yml`; Docker/Aspire only for integration |
| AC-005 | Package/source inventory and build assertions | Static/build; no lock files or unpinned refs; feed publication check for owner package | Solution build and remote package feed evidence |
| AC-006 | Format, build, analyzer fixtures, TUnit suites | Static, compile, unit; zero errors/skips and complete analyzer fixture behavior | Exact SHA GitHub Actions; run format before final qualification |
| AC-007 | Process-recovery and Docker/Aspire RF3 suites | Real process and cluster; lifecycle/failure matrix and MCP/.NET parity | GitHub `ci.yml`; preserve SHA, jobs, logs and artifacts |
| AC-008 | Documentation/governance checks and workflow status | Static plus remote delivery/publication evidence | GitHub Actions, protected remote state, release/feed verification |
| AC-009 | TimeSeriesComparison target/oracle/resource tests in [timeseries-comparison.acceptance.md](timeseries-comparison.acceptance.md) | Real Docker/Aspire RF3 .NET SDK, Timescale Npgsql and published ManagedCode.TimeSeries; shared timestamps, IDs and exact aggregation oracle | GitHub `ci.yml` comparison job; retain delivered SHA, run/job URLs and raw profile artifact |
| AC-010 | Operation inventory, feature-owned resource/fault tests, and repeated RF3 comparison profiles | Every public operation family has explicit measurable budgets; exact result/authorization/fault semantics remain unchanged; same inputs/topology/acknowledgement contract | GitHub `ci.yml` exact-SHA artifacts under [memory-performance acceptance](memory-performance.acceptance.md); no local benchmark or performance claim |
| AC-011 | Owner scope review first; after mapping, feature-owned TUnit/recovery/RF3 tests derived from the new feature acceptance | No guessed acronym; one canonical slice; correct Orleans/RF3 boundaries and scale budgets | Owner clarification plus reviewed feature/ADR/task/test map, then exact-SHA GitHub evidence; open until defined |

Manual/review evidence: endurance, power-loss/total-reset, independent failure domains, broad compatibility/upgrade and production-readiness gates have no established automated collector/run in this checkout. They must remain explicitly pending until their actual environment and artifact exist.
