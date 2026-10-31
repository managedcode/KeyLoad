# TestInfrastructure tooling

## Purpose, entry points and ownership

- Owns `run-tests.mjs`, the bounded argument selector for the TUnit entry.
- `KEYLOAD_TEST_CONFIGURATION` selects Debug or Release consistently for binaries and framework IPC. Local execution defaults to Debug; functional CI selects Release explicitly.
- C# test fixtures own Aspire startup, readiness, discovered endpoints and joined teardown.
- `KeyLoad.AppHost` starts KeyLoad project processes with the published in-memory RapidMembership provider.
- `KeyLoad.LoadTests.AppHost` owns selected comparison infrastructure. Client code executes workloads and measurements outside the resource graph.
- Normal builds and test startup never publish a KeyLoad container. The canonical Release workflow publishes the server image with the .NET SDK `PublishContainer` target; full Benchmarks calls its server-image-only entry and consumes the exact-source image by digest after all functional gates.
- AppHost cleanup removes only resources created for its session, including after failures and cancellation.
- Preserve original test, process and coverage evidence; do not claim container identities for processes.

## Boundaries and protected risks

Follow the [root policy](../../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
node scripts/Features/RepositoryGovernance/verify.mjs
```

Use the root's complete solution, TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
