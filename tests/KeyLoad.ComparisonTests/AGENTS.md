# KeyLoad.ComparisonTests

## Purpose, entry points and ownership

- Owns TUnit comparison-harness checks and the CI comparison workload entry point.
- Native session entry: `BenchmarkComparisons/Session/Cases/BenchmarkSessionTests.cs`; project: `KeyLoad.ComparisonTests.csproj`.
- Tests and comparison artifacts belong to the `BenchmarkComparisons` slice and matching `docs/Features/BenchmarkComparisons.md`.
- This project validates the comparative harness and emits controlled CI evidence; it does not establish production readiness or own product runtime behavior.
- Keep positive, negative and error assertions meaningful. Never weaken checks or replace the real cluster KeyLoad target with a fake/in-memory substitute.
- Apply the installed Orleans and Aspire skills for native runtime and resource-lifecycle decisions.
- CI comparison data is distinct from durability, endurance and production qualification. Use native TUnit and Microsoft.Testing.Platform for current test execution.

## Boundaries and protected risks

Follow the [root policy](../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use native Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet test --project tests/KeyLoad.ComparisonTests/KeyLoad.ComparisonTests.csproj --no-build --no-restore --output Detailed
```

Use the root's complete solution, native TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
