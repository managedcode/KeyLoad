# KeyLoad.ServiceDefaults

## Purpose and entry points
- Owns shared Aspire service defaults and common service instrumentation/configuration.
- Composition extension: `Extensions.cs`; project definition: `KeyLoad.ServiceDefaults.csproj`.

## Ownership and boundaries
- Keep only genuinely solution-wide service defaults here. Feature behavior belongs under its canonical `Features/<SliceName>/` path and matching feature spec.
- Defaults do not own database policy, storage, partition placement or feature-specific request behavior. Root-level extension is a shared composition point, not a feature layer.
- Do not introduce secrets, project-specific hidden coupling or analyzer/formatting exceptions without a documented owner contract.

## Commands and evidence
- GitHub Actions solution build: `dotnet build KeyLoad.slnx --no-restore --configuration Release`.
- Service behavior is exercised through the invoking IntegrationTests suite in `.github/workflows/ci.yml`: `dotnet test --project tests/KeyLoad.IntegrationTests --no-build --no-restore --configuration Release`. Tests run only in GitHub Actions.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Preserve telemetry privacy, health semantics and centrally managed package versions. No local runtime test or production qualification claims.

## Read-first and canonical slice ownership
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- This project owns shared telemetry, service discovery, and resilience composition; the architecture map assigns no product feature slice here.
- Keep `Extensions.cs` as a shared building block; feature-specific behavior belongs in the owning project’s named `Features/<SliceName>/` path.

## Vertical-slice responsibility folders
- Keep feature-owned implementation inside its canonical `Features/<SliceName>/` and organize it in populated, feature-local responsibility folders (such as `Models/`, `Contracts/`, `Commands/`, `Queries/`, `Transport/`, `Hosting/`, `Serialization/`, or `Validation/`). Do not leave a flat dump of unrelated responsibilities at the slice root; keep only genuinely shared building blocks and executable/composition entry points outside feature slices. Preserve namespaces and runtime contracts during physical moves.
