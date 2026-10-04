# KeyLoad.Storage.IO

## Purpose and entry points
- Owns the shared regular-file opening primitive for stopped StorageRecovery conversion under ADR-077, REQ-STORAGE-025/026 and AC-EPOCH-012.
- Internal entry point: `Features/StorageRecovery/Storage/OfflineRegularFile.cs`; project: `KeyLoad.Storage.IO.csproj`.

## Ownership and boundaries
- Keep filesystem implementation in the canonical StorageRecovery slice. This project references only BCL and existing KeyLoad.Abstractions error contracts; it owns no database state, engine, authorization, routing, placement, receipt codec or replication.
- Use documented public operating-system interfaces. Do not bind internal System.Native exports, invoke external utilities in product code or guess platform ABI layouts.
- The frozen ABI scope is little-endian Linux/macOS x64/arm64. Other platforms and unavailable metadata capabilities fail closed; only genuine Linux evidence qualifies delivery.
- Open existing inputs without create/truncate flags, using no-follow and nonblocking flags, and validate regular-file type and identity on the retained handle. Preserve nonblocking flock interoperability with the existing .NET owner locks.
- Root owns signatures, project references, platform/error contract and integration. Workers must stop and escalate architecture changes, unsupported ABI assumptions or ownership conflicts.

## Commands and verification
- Required development checks: full Release solution build, formatter and repository governance. Root owns the serialized verification sequence.
- TUnit unit/scalar and bounded process regressions run through KeyLoad.AppHost, using the unified Aspire test entry point. RF3 remains genuine Docker/Aspire with .NET and official MCP clients.
- No skipped test or local result counts as delivered-source Linux qualification; retain exact source and original reports.

## Skills and protected risks
- No applicable project-local skill is installed; do not install tools, packages or skills.
- Preserve root complexity limits and all compiler/style diagnostics. `AllowUnsafeBlocks` is scoped solely to this project for generated LibraryImport code, with no analyzer suppression.
- FIFO/device/socket input must be rejected before reading data; bind the checked object to its open handle and retain primary/cleanup failures. Files created privately by an owned retained CreateNew handle remain under their existing creation contract.
## Feature-local responsibility folders

- Every populated `Features/<SliceName>/` source area MUST group feature-owned C# files in populated child folders by their actual responsibility (for example `Models/`, `Contracts/`, `Commands/`, `Queries/`, `Serialization/`, `Validation/`, `Recovery/`, `Admission/`, `Lifecycle/`, `Storage/`, or `Execution/` where applicable). Do not leave a flat mix of roles or create empty placeholders. Keep each role local to its owning feature; preserve namespaces, public signatures, serialization aliases/IDs, and source bytes during structural moves. Keep genuine project composition roots and shared cross-feature building blocks outside feature slices.
