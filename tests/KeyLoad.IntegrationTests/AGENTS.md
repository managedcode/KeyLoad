# KeyLoad.IntegrationTests

## Purpose and entry points
- Owns caller-visible cluster and service integration tests.
- Main suite files: `ClusterTests.cs`, `AdmissionClusterTests.cs`; topology fixture: `ClusterFixture.cs`.

## Ownership and boundaries
- Test cases belong under canonical `Features/<SliceName>/` paths; shared cluster/container fixtures remain test infrastructure. Keep each feature's tests aligned with its docs and public contracts.
- Required qualification is Docker/Aspire RF3 through the real .NET SDK and official MCP C# SDK clients. The current host-process RF3 implementation is migration debt and does not satisfy the target topology.
- Preserve atomic partition versus physical placement and node-local `PartitionHost` ownership; use real Orleans routing. Do not replace external dependencies with mocks, fakes or in-memory/single-node proofs.

## Commands and evidence
- GitHub Actions invoking command: `dotnet test --project tests/KeyLoad.IntegrationTests --no-build --no-restore --configuration Release` in `.github/workflows/ci.yml` after solution restore/build.
- Tests and qualification run only in GitHub Actions. Capture the exact workflow run, SHA, job URL and artifacts; no skipped suite counts as passing.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- This suite crosses process, network, authorization and cluster boundaries. Process-kill results are not power-loss proof; do not report production readiness without all required fault and endurance gates.

## Read-first and canonical slice ownership
- Owns `Features/RelationalStorage/` and QueryExecution unified SQL RF3 differential cases under ADR-054/055, through actual .NET/official MCP clients. Separate calls can observe different committed cuts; verify each contract and exact logical output rather than asserting cross-node cut equality.
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Shared cluster and SDK transport tests use `Features/ClusterReplication/` and `Features/ClientApi/`. Business integration cases MUST mirror their owning business slice under `Features/<same-business-SliceName>/`, including `DocumentStorage`, `EventStreams`, `Messaging`, `GraphTraversal`, `TimeSeries`, `Search`, `QueryExecution`, `Authorization` and `ChangeFeeds`.
- `ClusterFixture.cs` is shared test infrastructure; `ClusterTests.cs` and `AdmissionClusterTests.cs` are current feature test entry points.
