# KeyLoad compiler analyzers

## Purpose, entry points and ownership

- Own compiler analyzers. Entry points: KeyLoad.Analyzers/KeyLoad.Analyzers.csproj.
- Own build-time Roslyn rules, not runtime database implementations.
- Keep existing namespaces, diagnostics, persistence identities and behavior stable. Follow root AGENTS.md and project-local policies.
- Preserve unrelated working-tree changes, warning enforcement, source identities, real cluster topology and honest qualification evidence.

## Boundaries and protected risks

Follow the [root policy](../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use native Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet build KeyLoad.slnx --no-restore -warnaserror
```

Use the root's complete solution, native TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
