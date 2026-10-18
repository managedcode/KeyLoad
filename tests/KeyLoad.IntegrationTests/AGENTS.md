# KeyLoad.IntegrationTests

## Purpose, entry points and ownership

- Owns caller-visible cluster and service integration tests.
- Main suite files: `ClusterReplication/Cases/Cluster/ClusterTests.cs`, `ResourceExecution/Cases/AdmissionClusterTests.cs`; topology fixture: `ClusterFixture.cs`.
- Test cases belong under canonical `<SliceName>/` paths; shared native cluster fixtures and required infrastructure containers remain test infrastructure. Keep each feature's tests aligned with its docs and public contracts.
- Preserve node-local ZoneTree ownership (files never move with activations); use real Orleans routing and membership (ADR-136). Do not replace external dependencies with mocks, fakes or in-memory/single-node proofs.
- Apply the installed Orleans skill for runtime decisions and the Aspire skill for fixture resource composition and lifecycle.
- This suite crosses process, network, authorization and cluster boundaries. Process-kill results are not power-loss proof; do not report production readiness without all required fault and endurance gates.
- Native process startup, pre-shutdown and joined-shutdown observations are stored under the original TUnit results qualification directory. They retain actual executable, assembly, symbols and compilation identities; they do not prove native server-process coverage collection.

## Boundaries and protected risks

Follow the [root policy](../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use native Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet test --project tests/KeyLoad.IntegrationTests/KeyLoad.IntegrationTests.csproj --no-build --no-restore --output Detailed
```

Use the root's complete solution, native TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
