# KeyLoad.IntegrationTests

## Purpose and entry points
- Owns caller-visible cluster and service integration tests.
- Main suite files: `Features/ClusterReplication/Cases/ClusterTests.cs`, `Features/ResourceExecution/Cases/AdmissionClusterTests.cs`; topology fixture: `ClusterFixture.cs`.

## Ownership and boundaries
- Test cases belong under canonical `Features/<SliceName>/` paths; shared cluster/container fixtures remain test infrastructure. Keep each feature's tests aligned with its docs and public contracts.
- Required qualification is Docker RF3 owned by the actual Aspire AppHost through the real .NET SDK and official MCP C# SDK clients. AppHost owns membership resources, readiness, test-runner dependencies, execution and shutdown. Any retained host-process or independently launched Docker fixture is migration debt and does not satisfy the target topology.
- Preserve atomic partition versus physical placement and node-local `PartitionHost` ownership; use real Orleans routing. Do not replace external dependencies with mocks, fakes or in-memory/single-node proofs.

## Commands and evidence
- Local and GitHub Actions caller entry after solution restore/build: `dotnet run --project src/KeyLoad.AppHost --no-build --no-restore --configuration Release -- --KeyLoadTests:Suite=rf3`. The native TUnit child is composed by AppHost; direct test execution is not an alternative caller entry.
- Owner-authorized local tests are development evidence. Delivered qualification runs on exact-source Linux GitHub Actions. Capture the exact workflow run, SHA, job URL and original artifacts; no skipped suite counts as passing.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- This suite crosses process, network, authorization and cluster boundaries. Process-kill results are not power-loss proof; do not report production readiness without all required fault and endurance gates.

## Read-first and canonical slice ownership
- Owns `Features/RelationalStorage/` and QueryExecution unified SQL RF3 differential cases under ADR-054/055, through actual .NET/official MCP clients. Separate calls can observe different committed cuts; verify each contract and exact logical output rather than asserting cross-node cut equality.
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Shared cluster and SDK transport tests use `Features/ClusterReplication/` and `Features/ClientApi/`. Business integration cases MUST mirror their owning business slice under `Features/<same-business-SliceName>/`, including `DocumentStorage`, `EventStreams`, `Messaging`, `GraphTraversal`, `TimeSeries`, `Search`, `QueryExecution`, `Authorization` and `ChangeFeeds`.
- `ClusterFixture.cs` is shared test infrastructure; `Features/ClusterReplication/Cases/ClusterTests.cs` and `Features/ResourceExecution/Cases/AdmissionClusterTests.cs` are current feature test entry points.
- `Features/CodeQuality/` owns scoped functional contributor selection and native server coverage exports under ADR-033 and CodeQuality AC-CQ-039..042. Root alone joins the existing ClusterFixture lifecycle. Collector-enabled cases use the same three Aspire Docker nodes, discovered SDK/official MCP endpoints and persisted authorization. Settle all original nodes/collectors/readers before reading exports, retain reports before deleting owned roots, and never infer a flush after a kill or count load/comparison cases as contributors.

## Current-format StorageRecovery qualification
- StorageRecovery integration cases exercise the supported current format only through the actual Aspire-owned recovery and Docker RF3 entry points. Preserve real SDK/official MCP operations, persisted authorization, original process boundaries, current backup/restore authority, bounded cancellation, joined cleanup and every current-format rejection oracle.
- AppHost owns test-runner dependencies, resources, execution and shutdown. Tests exercise the current-format server and actual homogeneous AppHost cohort through the declared entry points. A process-kill result is not power-loss evidence; exact-source Linux artifacts and every required suite remain necessary.
## Feature slice responsibility folders
- Place every feature-owned source file under `Features/<SliceName>/<Role>/`, using populated feature-local roles such as `Cases/`, `Fixtures/`, `Assertions/`, `Models/`, `Processes/`, `Contracts/`, `Serialization/` or `Helpers/` according to the file's actual responsibility. Preserve an existing nested scenario/domain folder and add the role beneath it. Create only roles that own files.
- Keep genuinely shared test infrastructure and project composition entry points at their existing shared ownership paths; do not duplicate them into a feature.
- Structural moves preserve every source byte, namespace, type/serializer identity and test assertion. Do not change behavior, test logic or path references as part of a layout-only move; report source-path-sensitive joins to the solution integrator.

## Native TUnit entry, owner correction 2026-10-07
- ADR-117 supersedes the earlier outer AppHost caller requirements: CI starts TUnit directly after build with Detailed output. Test fixtures own Aspire infrastructure startup, readiness, client operations and cleanup. scripts/Features/TestInfrastructure/run-tests.mjs only selects native test arguments/environment; it cannot execute database workloads. RF3 coverage preparation belongs to the TUnit session lifecycle. Preserve every original qualification/artifact gate and separate Benchmarks ownership.
