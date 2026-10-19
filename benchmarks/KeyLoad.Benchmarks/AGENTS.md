# KeyLoad.Benchmarks

## Purpose, entry points and ownership

- Owns the BenchmarkDotNet executable for KeyLoad-focused microbenchmarks.
- Executable entry point: `Program.cs`; project definition: `KeyLoad.Benchmarks.csproj`.
- This single-feature executable keeps behavior in populated root role folders; do not add a wrapper directory that repeats its feature name. Keep references to the owning product feature specification.
- This project measures code paths; it does not own database behavior, comparison corpus/report contracts, cluster orchestration, or qualification claims.
- Apply the installed Orleans and Aspire skills when their runtime or composition boundaries are involved.
- Do not turn microbenchmark output into cluster, durability, endurance, or production-readiness evidence. Preserve centrally pinned packages and the no-lock-file policy.

## Boundaries and protected risks

Follow the [root policy](../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet build KeyLoad.slnx --no-restore -warnaserror
```

Use the root's complete solution, TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
