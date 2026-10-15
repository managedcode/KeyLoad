# KeyLoad.Storage.IO

## Purpose and entry points
- Owns the shared regular-file opening primitive used by current StorageRecovery, native index metadata and scoped process/RF3 inspection under ADR-011/116 and the CurrentFormat contract. Do not introduce storage conversion or prior-format admission.
- Internal entry point: `Features/StorageRecovery/Storage/OfflineRegularFile.cs`; project: `KeyLoad.Storage.IO.csproj`.

## Ownership and boundaries
- Keep filesystem implementation in the canonical StorageRecovery slice. This project references only BCL and existing KeyLoad.Abstractions error contracts; it owns no database state, engine, authorization, routing, placement, receipt codec or replication.
- Use documented public operating-system interfaces. Do not bind internal System.Native exports, invoke external utilities in product code or guess platform ABI layouts.
- The frozen ABI scope is little-endian Linux/macOS x64/arm64. Other platforms and unavailable metadata capabilities fail closed; only genuine Linux evidence qualifies delivery.
- Open existing inputs without create/truncate flags, using no-follow and nonblocking flags, and validate regular-file type and identity on the retained handle. Preserve nonblocking flock interoperability with the existing .NET owner locks.
- Root owns signatures, project references, platform/error contract and integration. Workers must stop and escalate architecture changes, unsupported ABI assumptions or ownership conflicts.
- ADR-116 permits an internal read-only observation handle for the CurrentFormat test's complete-file no-mutation oracle while that test controls all writers. Retain no-follow/nonblocking regular-file and identity validation, validated buffer options and full actual bytes; never acquire or release the live owner's advisory lock. This is not a concurrent-writer snapshot API or production ownership bypass. Production opens preserve their existing lock contract; root owns the API/platform integration and actual Aspire regression.

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

## Root-approved KL092 read-only kernel clock observation, 2026-10-15

- Narrow additional responsibility: `Features/ResourceExecution/Interop/` may own ONLY the read-only Linux kernel clock-health observation approved for KL092. This exception does not admit generic native dispatch, clock setting, filesystem expansion, a daemon/client process, NTP configuration, a replacement storage provider or new dependencies.
- Supported native candidate is little-endian Linux LP64 x64/arm64 with verified GNU libc runtime/ABI. Unsupported/missing/musl/unverified ABI is unavailable; it MUST NOT become Ready. Pin authoritative platform headers and retain a matching real Linux runtime ABI probe separately from source layout calculations.
- Native `adjtimex` input modes is private and always zero. Expose only a typed bounded observation/error/unavailable result. Preserve existing `SafeDirectories`, documented public libc entry points and scoped unsafe generation ONLY in this project. No method may expose mutable modes or a clock-changing operation.
- Report status/return state and exact maximum/estimated errors; their units remain microseconds. Respect STA_NANO when interpreting timeval subsecond units, validate ranges and checked conversions. Bound the observation with original TimeProvider monotonic captures before/after the actual call. Neither kernel status nor NTP configuration alone qualifies physical UTC accuracy.
- This primitive owns no database state, store view, authorization, committed floor, provider readiness/binding, policy default or scheduler. Server's reviewed adapter combines the snapshot with explicit validated policy, same original clock, actual owning-view committed floor and observation age. No nested Store.Read or source-caller-supplied floor.
- Root owns the exact native signatures/ABI and integration. SourceN implementation may proceed only under ADR-133 after this owning policy is joined; production adoption requires the complete reviewed provider/current-caller integration and matching executed Linux ABI proof. Existing regular-file APIs and all original refusal/ownership contracts remain unchanged.
- A private read-only `gnu_get_libc_version` verification may inspect the returned static bytes only through a named maximum32-byte bounded NUL search. Retain `SafeDirectories`; null/missing symbol/missing terminator/unsupported version shape is unavailable. Never use unbounded `strlen` or `PtrToString`, and never treat a GNU name/version alone as proof of the loaded timex ABI.
