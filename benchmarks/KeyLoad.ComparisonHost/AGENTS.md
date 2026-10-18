# KeyLoad.ComparisonHost

## Purpose, entry points and ownership

- Owns the native TUnit comparison executable and helpers in root responsibility folders.
- Read root AGENTS.md, docs/Architecture.md, BenchmarkComparisons feature, ADR-033/034/043 and comparison-host acceptance/plan before implementation.
- Aspire owns resource lifecycle; the comparisons resource keeps its environment, waits and image pins. Never replace the required KeyLoad cluster topology.
- Keep Program composition-only and feature behavior under the canonical slice. No new package, duplicate entry, compatibility shim, suppression or hardcoded machine keys.
- Apply the installed Orleans and Aspire skills when their runtime or composition boundaries are involved.
- Track each client/target ownership transfer and partial construction cleanup; detach console handlers before lifetime disposal. Keep credentials external and error output safe.
- Preserve every unrelated shared-checkout change. Workers own only this new project; the lead alone changes library, solution, AppHost, inventory, docs and tests references.

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
