# KeyLoad.Comparisons

## Purpose, entry points and ownership

- Owns the reusable comparative benchmark harness library and its database targets under ADR-043; the sole executable belongs to KeyLoad.ComparisonHost in this repository.
- Library entry point: `Session/Execution/BenchmarkSession.cs`; native target adapters and shared corpora use root responsibility folders. Native TUnit test composition belongs to KeyLoad.ComparisonHost.
- Own comparative workload definitions, target adapters, sampling and report serialization. Product semantics remain owned by their product slices; CI callers own qualification and publication decisions.
- Keep the corpus and measurement contract stable and reproducible. Do not substitute an in-memory/single-node KeyLoad target for the required cluster topology.
- Apply the installed Orleans and Aspire skills when their runtime or composition boundaries are involved.
- Never infer durability or production readiness from benchmark results. Keep all target credentials external and preserve centrally pinned packages and no `packages.lock.json` files.

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
