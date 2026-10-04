# KeyLoad.IntegrationTests

## Purpose and entry points
- Owns caller-visible cluster and service integration tests.
- Main suite files: `ClusterTests.cs`, `AdmissionClusterTests.cs`; topology fixture: `ClusterFixture.cs`.

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
- `ClusterFixture.cs` is shared test infrastructure; `ClusterTests.cs` and `AdmissionClusterTests.cs` are current feature test entry points.

## StorageRecovery cold RF3 qualification
- New NodeEpochRf3 cases under Features/StorageRecovery implement ADR-077 through sequential genuine prior/current Aspire Docker RF3 waves, preserved private profile and all3-prepare-before-publish barrier. Ordinary ClusterFixture and its current image proof stay unchanged; root owns shared image/CLI/MCP joins. Offline converter children run within the Aspire-owned TUnit runner and never serve a standalone database. Preserve primary/cleanup failures and retain bind roots until all owned resources/processes/handles have settled.
