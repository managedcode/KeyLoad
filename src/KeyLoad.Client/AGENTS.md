# KeyLoad.Client

## Purpose, entry points and ownership

- Owns the .NET SDK client surface used by applications connecting to KeyLoad.
- Main entry points: `ClientApi/Transport/KeyLoadClient.cs` and `QueryExecution/Queries/KeyLoadQuery.cs`.
- Client capabilities belong under `<SliceName>/` and the same-name `docs/Features/<SliceName>.md`; keep shared client composition separate from feature behavior.
- The SDK owns request construction and response mapping, not trusted authorization, database persistence, replication, or server-side validation. Credentials and authorization policy are database-owned; callers cannot assert roles.
- Use the installed formatting and semantic C# tools for SDK changes; apply Orleans guidance when reviewing its server boundary.
- Preserve cancellation, disposal, transport errors and typed contracts. Keep secrets out of source and diagnostics; maintain central package pins and the no-lock-file policy.

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
