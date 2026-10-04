# KeyLoad.Client

## Purpose and entry points
- Owns the .NET SDK client surface used by applications connecting to KeyLoad.
- Main entry points: `KeyLoadClient.cs` and `Features/QueryExecution/Queries/KeyLoadQuery.cs`.

## Ownership and boundaries
- Client capabilities belong under `Features/<SliceName>/` and the same-name `docs/Features/<SliceName>.md`; keep shared client composition separate from feature behavior.
- The SDK owns request construction and response mapping, not trusted authorization, database persistence, replication, partition placement, or server-side validation. Credentials and authorization policy are database-owned; callers cannot assert roles.
- Current flat client files are migration debt. Public API changes require contract review and caller-visible tests; do not hide dependency defects with SDK workarounds.

## Commands and evidence
- GitHub Actions solution build: `dotnet build KeyLoad.slnx --no-restore --configuration Release`.
- Real client flow is invoked by `dotnet test --project tests/KeyLoad.IntegrationTests --no-build --no-restore --configuration Release` in `.github/workflows/ci.yml`; execute tests only in GitHub Actions.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Preserve cancellation, disposal, transport errors and typed contracts. Keep secrets out of source and diagnostics; maintain central package pins and the no-lock-file policy.

## Read-first and canonical slice ownership
- Owns `Features/QueryExecution/Transport/SqlClient.cs` unified SELECT/CALL transport under ADR-054 and `Features/BlobStorage/` typed canonical lifecycle/range adapters under ADR-038. Preserve stable command IDs and classify interrupted CALL as potentially unknown writes.
- Feature-owned `BlobClientExtensions` use the single assembly-internal Send transport and validate public client/request arguments before HTTP work; do not duplicate transport or evade aggregate type-size limits through partial declarations.
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- `ClientApi` owns shared SDK transport and request/response infrastructure under `Features/ClientApi/`, matching `docs/Features/ClientApi.md`.
- Business SDK operations MUST mirror their owning business slice, including `DocumentStorage`, `EventStreams`, `Messaging`, `GraphTraversal`, `TimeSeries`, `Search`, `QueryExecution`, `Authorization` and `ChangeFeeds`, under `Features/<same-business-SliceName>/` and its matching feature doc.
- `KeyLoadClient.cs` and `Features/QueryExecution/Queries/KeyLoadQuery.cs` are current entry points; shared transport belongs to `ClientApi`, while business behavior keeps the same canonical name as core, contracts, API and tests.

## Vertical-slice responsibility folders
- Keep feature-owned implementation inside its canonical `Features/<SliceName>/` and organize it in populated, feature-local responsibility folders (such as `Models/`, `Contracts/`, `Commands/`, `Queries/`, `Transport/`, `Hosting/`, `Serialization/`, or `Validation/`). Do not leave a flat dump of unrelated responsibilities at the slice root; keep only genuinely shared building blocks and executable/composition entry points outside feature slices. Preserve namespaces and runtime contracts during physical moves.
