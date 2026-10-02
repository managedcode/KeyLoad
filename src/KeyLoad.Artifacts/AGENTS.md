# KeyLoad.Artifacts

## Purpose and entry points
- Owns artifact transfer and backup artifact representations.
- Entry points and principal types: `ArtifactTransfer.cs`, `BackupArtifact.cs`; project: `KeyLoad.Artifacts.csproj`.

## Ownership and boundaries
- Artifact feature behavior belongs under `Features/<SliceName>/` and its matching `docs/Features/<SliceName>.md`.
- This project owns artifact-format/transfer primitives only. It does not own database command routing, authorization policy, physical storage placement, or cluster replication.
- Current files at project root are migration debt; preserve stable format contracts and do not add consumer-side workarounds for defects owned by dependencies.

## Commands and evidence
- GitHub Actions solution build: `dotnet build KeyLoad.slnx --no-restore --configuration Release`.
- Artifact behavior is covered by the invoking suite `dotnet test --project tests/KeyLoad.UnitTests --no-build --no-restore --configuration Release` in `.github/workflows/ci.yml`; tests run only in GitHub Actions.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Treat imported artifact content as untrusted and preserve validation, bounded transfer and error behavior. Do not represent process-kill checks as power-loss durability proof.

## Read-first and canonical slice ownership
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned slice: `BackupRestore`; target feature path: `Features/BackupRestore/`, matching `docs/Features/BackupRestore.md`.
- `ArtifactTransfer.cs` and `BackupArtifact.cs` are current entry points; feature behavior belongs under that path.
