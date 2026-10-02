# KeyLoad.Cli

## Purpose and entry points
- Owns the command-line client executable.
- Entry point: `Program.cs`; project definition: `KeyLoad.Cli.csproj`.

## Ownership and boundaries
- CLI workflows belong under their canonical `Features/<SliceName>/` paths and matching feature specs. Keep `Program.cs` as composition/dispatch only.
- The CLI is an untrusted client: it cannot supply trusted roles or bypass persisted database credentials and authorization policies. It does not own server persistence, replication, or physical placement.
- Existing root-level executable layout is migration debt; do not add duplicate SDK behavior or undocumented fallback paths.

## Commands and evidence
- GitHub Actions solution build: `dotnet build KeyLoad.slnx --no-restore --configuration Release`.
- Public client flows are invoked by `.github/workflows/ci.yml` suites, especially `dotnet test --project tests/KeyLoad.IntegrationTests --no-build --no-restore --configuration Release`; tests run only in GitHub Actions.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Preserve centrally pinned packages and net10.0/C# 14. Never put credentials in source, logs, sample arguments or committed configuration.

## Read-first and canonical slice ownership
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned slices: `ClientApi` and `BackupRestore`; target paths: `Features/ClientApi/` and `Features/BackupRestore/`.
- Keep `Program.cs` as the CLI composition/dispatch entry point; feature command behavior belongs in its named slice.
