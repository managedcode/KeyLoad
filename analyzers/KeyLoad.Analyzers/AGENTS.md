# KeyLoad.Analyzers

## Purpose, entry points and ownership

- Own the source-editable compiler rules in the root `Analyzers/`, `Analysis/`, `Contracts/`, `Models/` and `Validation/` role folders; project entry is `KeyLoad.Analyzers.csproj`. Feature contract: `docs/Features/CodeQuality.md`; implementation decision: ADR-033.
- Compile against Roslyn shipped by the pinned .NET SDK. Attach centrally as an analyzer, never as a runtime product dependency. All rule behavior belongs to the CodeQuality slice.
- Preserve imported semantics while adapting KeyLoad assembly/method ownership. No sibling reference, fake framework symbols, silent severity reduction or runtime contract migration.
- Use Roslyn's generated-code API for each rule's actual contract. KLD0038 and KLD0039 analyze generated code without blanket exceptions; retain only their explicit semantic ownership permissions. Keep semantic analysis cancellable and concurrent.
- Keep diagnostic IDs stable and unique. Compiler references must use the pinned SDK, with no private dependency payload attached to consumer runtime outputs. Preserve all unrelated dirty checkout changes.

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
