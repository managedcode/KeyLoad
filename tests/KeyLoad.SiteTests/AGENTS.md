# KeyLoad.SiteTests

## Purpose, entry points and ownership

- Owns the independently buildable static website qualification suite. Entry: KeyLoad.SiteTests.csproj; implementation uses populated role folders directly under this project. Read root/site policy, BenchmarkComparisons design/acceptance/execution contracts and ADR-040 before implementation.
- TUnit/Microsoft.Testing.Platform, net10.0, centrally pinned packages and all central analyzers are mandatory. No Core/AppHost dependency, alternate runner, mocks/fakes/stubs or browser automation package.
- Exercise actual production JavaScript through a bounded real Node child process and authentic successful GitHub report files. C# owns independent assertions/oracles. Controlled corrupt inputs are test data and must never be published as measurements.
- Tests preserve existing median/repetition/failure/unsupported/order intentions and cover validators, confined paths, raw hashes, clean builds and complete emitted assets. Visual/device/lifecycle exceptions require real browser evidence as specified in ADR-040; do not manufacture device-loss or coverage proof.
- Worker ownership is the website qualification implementation in these role folders. Root owns this policy, project, solution, workflow, shared contracts and governance inventory.

## Boundaries and protected risks

Follow the [root policy](../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use native Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet test --project tests/KeyLoad.SiteTests/KeyLoad.SiteTests.csproj --no-build --no-restore --output Detailed
```

Use the root's complete solution, native TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
