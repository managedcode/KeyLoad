# KeyLoad.AnalyzerTests

## Purpose, entry points and ownership

- Own real Roslyn diagnostic checks in the root `Cases/`, `Fixtures/`, `Assertions/`, `Contracts/` and `Processes/` role folders; entry point is `KeyLoad.AnalyzerTests.csproj`. Follow CodeQuality acceptance and ADR-033.
- TUnit on Microsoft.Testing.Platform is the only runner. Compile valid product examples with real SDK Roslyn and real Orleans metadata. Source snippets are compiler inputs, never substitutes for external framework types.
- No mocks, stubs, fake Orleans attributes/interfaces or package implementation copies. Assert semantic fixture compiler success before asserting analyzer findings.
- Test all applicable positive, negative and edge paths, precise IDs, severities and locations. Do not weaken tests to hide analyzer faults.
- Test-only normal analyzer assembly reference is distinct from central Analyzer attachment. Prevent recursive analyzer references and retain TUnit execution evidence at the delivered SHA.

## Boundaries and protected risks

Follow the [root policy](../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet test --project tests/KeyLoad.AnalyzerTests/KeyLoad.AnalyzerTests.csproj --no-build --no-restore --output Detailed
```

Use the root's complete solution, TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
