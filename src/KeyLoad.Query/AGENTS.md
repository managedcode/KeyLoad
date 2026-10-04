# KeyLoad.Query

## Purpose and entry points
- Owns query parsing, validation, execution, search and live-query behavior.
- Entry points/types: `Features/QueryExecution/Execution/Features/QueryExecution/Execution/Features/QueryExecution/Execution/Features/QueryExecution/Execution/SqlParser.cs`, `Features/QueryExecution/Validation/Features/QueryExecution/Validation/Features/QueryExecution/Validation/Features/QueryExecution/Validation/QueryValidation.cs`, `Features/QueryExecution/Queries/Features/QueryExecution/Queries/Features/QueryExecution/Queries/Features/QueryExecution/Queries/QueryEngine.cs`, `Features/Search/Queries/Features/Search/Queries/Features/Search/Queries/Features/Search/Queries/SearchEngine.cs`, `Features/ChangeFeeds/Execution/Features/ChangeFeeds/Execution/Features/ChangeFeeds/Execution/Features/ChangeFeeds/Execution/LiveQueries.cs`, and query AST definitions.

## Ownership and boundaries
- Query feature work belongs under `Features/<SliceName>/` with matching canonical feature documentation. Query code consumes abstractions and database capabilities; it does not own storage, command routing, credentials, or physical placement.
- Current root-level feature sources are migration debt; keep parsing, validation and execution contracts explicit and avoid leaking implementation-specific storage details through public query contracts.
- Search and query authorization must honor persisted database policy; callers cannot provide trusted roles.

## Commands and evidence
- GitHub Actions solution build: `dotnet build KeyLoad.slnx --no-restore --configuration Release`.
- Query behavior is exercised by `dotnet test --project tests/KeyLoad.UnitTests --no-build --no-restore --configuration Release` and public flows in IntegrationTests, dispatched only through `.github/workflows/ci.yml`.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Preserve resource budgets, cancellation, validation and error semantics. Tests/qualification run only in GitHub Actions; no local test claims.

## Read-first and canonical slice ownership
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned slices: `QueryExecution`, `Search`, and `ChangeFeeds`; target paths: `Features/QueryExecution/`, `Features/Search/`, and `Features/ChangeFeeds/`.
- Keep parsing, execution, search and live-query behavior in the matching named slice; shared entry points remain composition/building blocks only.
## Feature-local responsibility folders

- Every populated `Features/<SliceName>/` source area MUST group feature-owned C# files in populated child folders by their actual responsibility (for example `Models/`, `Contracts/`, `Commands/`, `Queries/`, `Serialization/`, `Validation/`, `Recovery/`, `Admission/`, `Lifecycle/`, `Storage/`, or `Execution/` where applicable). Do not leave a flat mix of roles or create empty placeholders. Keep each role local to its owning feature; preserve namespaces, public signatures, serialization aliases/IDs, and source bytes during structural moves. Keep genuine project composition roots and shared cross-feature building blocks outside feature slices.
