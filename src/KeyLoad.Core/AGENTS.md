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
- Core behavior is exercised by `dotnet test --project tests/KeyLoad.UnitTests --no-build --no-restore --configuration Release` and the related integration/recovery suites, all dispatched through `.github/workflows/ci.yml` only.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Transactions, command ordering, admission, projections and recovery contracts are high-risk. Runtime claims require the exact GitHub Actions run/SHA/artifacts; process-kill evidence does not establish power-loss durability.

## Read-first and canonical slice ownership
- Owns `Features/RelationalStorage/` final-image/schema/raw-patch validation under ADR-055; typed rows reuse canonical Collection entity/index storage. Preserve mixed-model atomicity, graph/vector identity, exact raw numeric checks and per-operation bounds.
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned slices: `DocumentStorage`, `EventStreams`, `Messaging`, `GraphTraversal`, and `TimeSeries`; target paths: `Features/DocumentStorage/`, `Features/EventStreams/`, `Features/Messaging/`, `Features/GraphTraversal/`, and `Features/TimeSeries/`.
- `DatabaseEngine.cs` and shared atomic transaction primitives remain shared building blocks; feature behavior goes in its named slice.
- Also owns the `Search` visible-vector read capability under `Features/Search/`, consumed by Query's matching Search slice. It preserves node-local read-cut ownership and persisted authorization; see `docs/Features/Search.md` and ADR-035. Shared document-key construction belongs to `Features/DocumentStorage/`.
- Shared canonical JSON writing/hash transport belongs to `Features/ResourceExecution/` under ADR-035; preserve exact validation, fingerprints and signatures across its DocumentStorage, QueryExecution and ChangeFeeds callers.
- Shared commit/outcome, canonical-key and authorization interfaces remain genuinely cross-feature building blocks. Admission/inbox lifecycle belongs to `Features/ResourceExecution/` under ADR-042; the node-local store and Orleans hosting clocks keep their existing owners.
- Owns `Features/BlobStorage/` under ADR-038: canonical bounded raw parts/manifests/quotas, current persisted authority and gated range reads. No separate provider/outcome log, full-payload outbox or grain-owned files; the integration lead owns existing engine dispatch and resource/bootstrap joins.
