# KeyLoad.Query

## Purpose, entry points and ownership

- Owns query parsing, validation, execution, search and live-query behavior.
- Entry points/types: `QueryExecution/Execution/SqlParser.cs`, `QueryExecution/Validation/QuerySemantics/QueryValidation.cs`, `QueryExecution/Queries/QueryEngine.cs`, `Search/Queries/QueryExecution/SearchEngine.cs`, `ChangeFeeds/Execution/LiveQueries.cs`, and query AST definitions.
- Query feature work belongs under `<SliceName>/` with matching canonical feature documentation. Query code consumes abstractions and database capabilities; it does not own storage, command routing, or credentials.
- Search and query authorization must honor persisted database policy; callers cannot provide trusted roles.
- Apply the installed Orleans skill when choosing distributed query execution primitives; retain the query algorithms and bounds in their owning slice.
- Preserve resource budgets, cancellation, validation and error semantics. Local Aspire results are development evidence; qualification requires exact-source Linux GitHub Actions artifacts and every required gate.

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
