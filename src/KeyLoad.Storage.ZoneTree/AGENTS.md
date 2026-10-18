# KeyLoad.Storage.ZoneTree

## Purpose, entry points and ownership

- Owns the ZoneTree-backed storage adapter and checkpoint operations.
- Main public entry point: `ZoneTreeStore.cs`; the replaced `StorageRecovery/Recovery/ZoneTreeCheckpointManager.cs` behavior is owned by `StorageRecovery/Recovery/ZoneTreeCheckpointManager.cs` and the checkpoint codec/generation components.
- Storage feature behavior belongs under `<SliceName>/` and its matching feature spec; keep provider-specific code behind storage abstractions.
- Physical ownership is node-local: every silo has its own ZoneTree owner (`PartitionHost`/Grain Service) that owns its storage, journals, file locks and apply gate. Orleans grains route commands, and activation migration must not migrate storage ownership; files never move with activations. See [ADR-136](../../docs/ADR/ADR-136-native-orleans-partition-ownership.md).
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Preserve atomicity, ordering, crash-recovery and disposal invariants. Do not add fake storage proofs, present local development evidence as delivered-source qualification, or use unlocked dependency versions.
- Owner direction 2026-10-09 requires immediately reporting every confirmed upstream ZoneTree defect through a detailed public issue in https://github.com/ZoneTree/ZoneTree, with exact version/source, minimal real-operation reproduction, expected/actual behavior, original bounded evidence and KeyLoad impact. Reuse a matching issue and publish additional evidence instead of duplicating it; keep secrets and private caller data out. Record the issue URL in the owning feature/task and continue independent KeyLoad work while upstream maintainers decide whether and when to fix it. Public reporting is explicitly authorized, but it does not satisfy the affected acceptance gate or permit a workaround, replacement storage engine, unpublished package or compatibility fallback; ManagedCode dependencies retain their separate owning-repository repair/release requirements.
- Owner correction 2026-10-13: batch all known confirmed defects in the same owned library/repository within the authorized task before releasing. Inspect the affected scope, collect and fix the defects together, add their focused regressions, self-review the complete change, and run the required checks against the combined batch. One defect may produce one release; several defects in that repository MUST share one release, not a version/release per fix.
- Increment the version once through the canonical mechanism only when the complete repair batch is ready. Push release-triggering changes and publish once per owning repository for that batch; do not trigger intermediate releases while other known fixes are pending. Verify successful GitHub publication and actual NuGet availability, then update each affected consumer once to that published version and run its focused regressions. Preserve unrelated work and all existing correctness, delivery and repository-protection gates.

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
