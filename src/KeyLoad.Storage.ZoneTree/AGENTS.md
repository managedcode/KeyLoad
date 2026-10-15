# KeyLoad.Storage.ZoneTree

## Purpose and entry points
- Owns the ZoneTree-backed storage adapter and checkpoint operations.
- Main public entry point: `ZoneTreeStore.cs`; the replaced `Features/StorageRecovery/Recovery/ZoneTreeCheckpointManager.cs` behavior is owned by `Features/StorageRecovery/Recovery/ZoneTreeCheckpointManager.cs` and the checkpoint codec/generation components.

## Ownership and boundaries
- Storage feature behavior belongs under `Features/<SliceName>/` and its matching feature spec; keep provider-specific code behind storage abstractions.
- Physical ownership is node-local: `PartitionHost` owns storage, journals, file locks and apply gate. Atomic partition identity is distinct from physical placement; Orleans grains route commands and activation migration must not migrate storage ownership.
- Current root-level adapter files are migration debt. Do not claim power-loss durability from process-kill evidence or alter storage formats without an ADR and recovery contract.

## Commands and evidence
- GitHub Actions solution build: `dotnet build KeyLoad.slnx --no-restore --configuration Release`.
- Owner correction2026-10-03 supersedes the former direct caller and local-execution prohibition: after restore/build, storage and checkpoint development tests use `dotnet run --project src/KeyLoad.AppHost --no-build --no-restore --configuration Release -- --KeyLoadTests:Suite=unit` and the same caller with `Suite=recovery`. Native `dotnet test` is only the AppHost-owned child process. Exact-source Linux GitHub qualification remains mandatory through `.github/workflows/ci.yml`.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Preserve atomicity, ordering, crash-recovery and disposal invariants. Do not add fake storage proofs, present local development evidence as delivered-source qualification, or use unlocked dependency versions.
- Owner direction 2026-10-09 requires immediately reporting every confirmed upstream ZoneTree defect through a detailed public issue in https://github.com/ZoneTree/ZoneTree, with exact version/source, minimal real-operation reproduction, expected/actual behavior, original bounded evidence and KeyLoad impact. Reuse a matching issue and publish additional evidence instead of duplicating it; keep secrets and private caller data out. Record the issue URL in the owning feature/task and continue independent KeyLoad work while upstream maintainers decide whether and when to fix it. Public reporting is explicitly authorized, but it does not satisfy the affected acceptance gate or permit a workaround, replacement storage engine, unpublished package or compatibility fallback; ManagedCode dependencies retain their separate owning-repository repair/release requirements.
- Owner correction 2026-10-13: batch all known confirmed defects in the same owned library/repository within the authorized task before releasing. Inspect the affected scope, collect and fix the defects together, add their focused regressions, self-review the complete change, and run the required checks against the combined batch. One defect may produce one release; several defects in that repository MUST share one release, not a version/release per fix.
- Increment the version once through the canonical mechanism only when the complete repair batch is ready. Push release-triggering changes and publish once per owning repository for that batch; do not trigger intermediate releases while other known fixes are pending. Verify successful GitHub publication and actual NuGet availability, then update each affected consumer once to that published version and run its focused regressions. Preserve unrelated work and all existing correctness, delivery and repository-protection gates.

## Read-first and canonical slice ownership
- Disposable point-cache infrastructure belongs to `Features/ResourceExecution/` under ADR-058. StorageRecovery still owns the real gate, Apply, snapshots and recovery joins; the cache cannot replace native records/WAL, scoped reads or authorization. Explicit embedded opt-in uses an externally shared pool; RF3 admission remains cold until the authenticated Orleans control contract is delivered and qualified. Index, fills and retired pinned bytes remain charged for their actual owned lifetime.
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned slice: `StorageRecovery`; target feature path: `Features/StorageRecovery/`, matching `docs/Features/StorageRecovery.md`.
- `ZoneTreeStore.cs` is the public provider entry point; private behavior, including the replaced `Features/StorageRecovery/Recovery/ZoneTreeCheckpointManager.cs`, MUST remain under the named slice. Local backup/restore is owned by the matching `Features/BackupRestore/` slice.
- ADR-046 accepts source-only replacement of private facade partial behavior with cohesive `Features/StorageRecovery/` runtime/journal/read/checkpoint owners and `Features/BackupRestore/` local backup helpers. The same public facade remains the physical owner; preserve formats, one gate/lock/tree/WAL, distinct cleanup orders and all required real qualification. Exact scope and migration join are in the `docs/Features/StorageRecovery.md` acceptance/execution contract and ADR-046; no new exception or ownership transfer is authorized.
## ZoneTree family upstream defects, owner correction 2026-10-15

- The root reporting rule covers ZoneTree, ZoneTree.FullTextSearch and owner-approved ZoneTree.Analytics. Publish every confirmed native dependency defect immediately in its owning upstream repository, after checking duplicates, with exact version/source, minimal complete reproduction, expected/actual behavior and bounded original evidence. Exclude credentials/private data; record the public issue URL and continue independent work. Reporting is authorized, but does not qualify the failing gate or permit a hidden workaround.

## Feature-local responsibility folders

- Every populated `Features/<SliceName>/` source area MUST group feature-owned C# files in populated child folders by their actual responsibility (for example `Models/`, `Contracts/`, `Commands/`, `Queries/`, `Serialization/`, `Validation/`, `Recovery/`, `Admission/`, `Lifecycle/`, `Storage/`, or `Execution/` where applicable). Do not leave a flat mix of roles or create empty placeholders. Keep each role local to its owning feature; preserve namespaces, public signatures, serialization aliases/IDs, and source bytes during structural moves. Keep genuine project composition roots and shared cross-feature building blocks outside feature slices.
