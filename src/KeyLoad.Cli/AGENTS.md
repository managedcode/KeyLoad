# KeyLoad.Cli

## Purpose, entry points and ownership

- Owns the command-line client executable.
- Entry point: `Program.cs`; project definition: `KeyLoad.Cli.csproj`.
- CLI workflows belong under their canonical `<SliceName>/` paths and matching feature specs. Keep `Program.cs` as composition/dispatch only.
- The CLI is an untrusted client: it cannot supply trusted roles or bypass persisted database credentials and authorization policies. It does not own server persistence or replication.
- Use the installed formatting and semantic C# tools for CLI changes; keep distributed execution in the server's Orleans integration.
- Preserve centrally pinned packages and net10.0/C# 14. Never put credentials in source, logs, sample arguments or committed configuration.

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
