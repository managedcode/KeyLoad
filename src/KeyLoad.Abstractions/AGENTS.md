# KeyLoad.Abstractions

## Purpose and entry points
- Owns public, implementation-independent contracts shared by KeyLoad clients and services.
- Main contract sources: `Contracts.cs`, `Queries.cs`, `QueryAst.cs`, `ChangeFeeds.cs`, `Subscriptions.cs`, `Admission.cs`, `HttpAdmission.cs`; storage contracts and key encoding are under `Storage/StorageContracts.cs` and `Storage/KeyCodec.cs`.

## Ownership and boundaries
- Place feature-owned public contracts under `Features/<SliceName>/` using the canonical name in `docs/Features/<SliceName>.md`; keep truly shared primitives here only when multiple slices require them.
- Contracts define caller-visible shapes, not persistence, authorization decisions, physical placement, Orleans routing, or server implementation.
- Current root-level contracts are legacy layout debt. Public contract changes require the owning feature specification and ADR where required; do not add compatibility shims or client-supplied trusted roles.

## Commands and evidence
- GitHub Actions solution build: `dotnet build KeyLoad.slnx --no-restore --configuration Release`.
- Contract behavior is exercised by the invoking TUnit projects: `tests/KeyLoad.UnitTests`, `tests/KeyLoad.IntegrationTests`, and `tests/KeyLoad.RecoveryTests`, each run only through `.github/workflows/ci.yml`.
- Tests and qualification run only in GitHub Actions. Do not claim a local build or static review as runtime contract proof.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Preserve net10.0/C# 14 and central package pins. Security, serialization, compatibility and trust-boundary changes require explicit contract review.

## Read-first and canonical slice ownership
- Shared disposable cache limits, reservation interfaces and modeled snapshots belong to `Features/ResourceExecution/` under ADR-058. They grant no database authority and contain no private store signing material; the integration lead alone owns these shared contracts.
- Owns `Features/RelationalStorage/` immutable schema metadata under ADR-055 and `Features/QueryExecution/` versioned SQL operation-envelope contracts under ADR-054. Null schema metadata preserves existing resource JSON; these contracts do not grant roles or introduce a second storage engine.
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned contract slices: `DocumentStorage`, `EventStreams`, `Messaging`, `Authorization`, `Search`, and `GraphTraversal`; target paths are `Features/<SliceName>/` for each named slice.
- Keep only genuinely shared abstractions at the project root; contract ownership and canonical docs follow the named slice.
- Shared strict collection/buffer JSON converters belong to `Features/ResourceExecution/` under ADR-041. Delegate array and base64 serialization to System.Text.Json, preserve nullable tombstones and reject invalid required defaults; only the integration lead owns shared JsonDefaults registration.
- Owns the frozen public `BlobStorage` DTO and integrity format under `Features/BlobStorage/` and ADR-038. Shared enum/capability/resource-definition additions have one integration owner; preserve nonblob wire JSON and existing numeric values.
- Owns bounded `TimeSeries` read contracts under `Features/TimeSeries/` and ADR-052: latest uses an inclusive timestamp cut, aggregates use half-open UTC ranges, and windows expose an explicit nullable exclusive end at the maximum timestamp. These contracts do not transfer storage ownership or trust caller-supplied roles.
