# KeyLoad.Core

## Purpose and entry points
- Owns core database domain behavior and orchestration primitives.
- Current principal types include `DatabaseEngine`, `KeySpace`, document/event/messaging/graph models, admission governors, queues, subscriptions and projection outbox; see the named source files at this project root.

## Ownership and boundaries
- New feature behavior belongs under `Features/<SliceName>/` with the canonical `docs/Features/<SliceName>.md`; shared primitives outside a slice require genuine cross-feature use.
- Core logic must not take ownership of physical node storage. A node-local `PartitionHost` owns storage, journals, file locks and the apply gate; Orleans grains route commands. Keep atomic partition identity distinct from physical placement.
- Root-level feature files are migration debt. Preserve caller-visible validation and authorization boundaries; no DotNext cluster expansion, fake runtime proof or hidden fallback behavior.

## Commands and evidence
- GitHub Actions solution build: `dotnet build KeyLoad.slnx --no-restore --configuration Release`.
- Owner correction2026-10-03 requires the actual Aspire entry after solution restore/build: `dotnet run --project src/KeyLoad.AppHost --no-build --no-restore --configuration Release -- --KeyLoadTests:Suite=unit`; related process and RF3 checks use `recovery` and `rf3` through that same entry. AppHost owns child execution, dependencies, artifacts and shutdown. Local development is authorized; exact-source Linux GitHub qualification remains mandatory.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Transactions, command ordering, admission, projections and recovery contracts are high-risk. Runtime claims require the exact GitHub Actions run/SHA/artifacts; process-kill evidence does not establish power-loss durability.

## Read-first and canonical slice ownership
- Shared cache retention admission belongs to `Features/ResourceExecution/` under ADR-058. One externally composed node budget owns atomic modeled byte/entry reservations; closing admission keeps outstanding charges until idempotent release. It does not own physical storage or establish an RSS limit.
- Owns `Features/RelationalStorage/` final-image/schema/raw-patch validation under ADR-055; typed rows reuse canonical Collection entity/index storage. Preserve mixed-model atomicity, graph/vector identity, exact raw numeric checks and per-operation bounds.
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned slices: `DocumentStorage`, `EventStreams`, `Messaging`, `GraphTraversal`, and `TimeSeries`; target paths: `Features/DocumentStorage/`, `Features/EventStreams/`, `Features/Messaging/`, `Features/GraphTraversal/`, and `Features/TimeSeries/`.
- `DatabaseEngine.cs` and shared atomic transaction primitives remain shared building blocks; feature behavior goes in its named slice.
- Also owns the `Search` visible-vector read capability under `Features/Search/`, consumed by Query's matching Search slice. It preserves node-local read-cut ownership and persisted authorization; see `docs/Features/Search.md` and ADR-035. Shared document-key construction belongs to `Features/DocumentStorage/`.
- Shared canonical JSON writing/hash transport belongs to `Features/ResourceExecution/` under ADR-035; preserve exact validation, fingerprints and signatures across its DocumentStorage, QueryExecution and ChangeFeeds callers.
- Shared commit/outcome, canonical-key and authorization interfaces remain genuinely cross-feature building blocks. Admission/inbox lifecycle belongs to `Features/ResourceExecution/` under ADR-042; the node-local store and Orleans hosting clocks keep their existing owners.
- Owns `Features/BlobStorage/` under ADR-038: canonical bounded raw parts/manifests/quotas, current persisted authority and gated range reads. No separate provider/outcome log, full-payload outbox or grain-owned files; the integration lead owns existing engine dispatch and resource/bootstrap joins.
- Owns `Features/Authorization/Commands/ResourcePolicyUpdates.cs` under ADR-093: pure policy-only metadata CAS validation. The existing canonical ResourceConfiguration apply gate and persisted administrator authorization own the write; this helper owns no storage, routing or physical migration.
## Feature-local responsibility folders

- Every populated `Features/<SliceName>/` source area MUST group feature-owned C# files in populated child folders by their actual responsibility (for example `Models/`, `Contracts/`, `Commands/`, `Queries/`, `Serialization/`, `Validation/`, `Recovery/`, `Admission/`, `Lifecycle/`, `Storage/`, or `Execution/` where applicable). Do not leave a flat mix of roles or create empty placeholders. Keep each role local to its owning feature; preserve namespaces, public signatures, serialization aliases/IDs, and source bytes during structural moves. Keep genuine project composition roots and shared cross-feature building blocks outside feature slices.
