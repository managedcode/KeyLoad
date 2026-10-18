# KeyLoad.Benchmarks

## Purpose, entry points and ownership

- Owns the BenchmarkDotNet executable for KeyLoad-focused microbenchmarks.
- Executable entry point: `Program.cs`; project definition: `KeyLoad.Benchmarks.csproj`.
- New benchmark behavior belongs under `<SliceName>/`; use the same canonical slice name as the corresponding product feature and `docs/Features/<SliceName>.md`.
- This project measures code paths; it does not own database behavior, comparison corpus/report contracts, cluster orchestration, or qualification claims.
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Do not turn microbenchmark output into cluster, durability, endurance, or production-readiness evidence. Preserve centrally pinned packages and the no-lock-file policy.

## Boundaries and protected risks

Follow the [root policy](../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use native Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet build KeyLoad.slnx --no-restore --configuration Release -warnaserror
```

Use the root's complete solution, native TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
