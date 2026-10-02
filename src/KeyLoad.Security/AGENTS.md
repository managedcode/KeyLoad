# KeyLoad.Security

## Purpose and entry points
- Owns authorization policy contracts and evaluation.
- Main source/type: `AuthorizationPolicy.cs`; project: `KeyLoad.Security.csproj`.

## Ownership and boundaries
- Authorization behavior belongs under `Features/Authorization/` and `docs/Features/Authorization.md` when that feature contract is created.
- This project owns policy evaluation, not credential persistence or transport authentication unless a feature contract assigns those explicitly. Credentials and authorization policies are persisted in the database; clients cannot supply trusted roles.
- Current root-level file is migration debt. Security-boundary changes require an ADR and caller-visible negative-flow evidence; do not add permissive defaults or compatibility bypasses.

## Commands and evidence
- GitHub Actions solution build: `dotnet build KeyLoad.slnx --no-restore --configuration Release`.
- Security unit scenarios run through `dotnet test --project tests/KeyLoad.UnitTests --no-build --no-restore --configuration Release`; public flows are covered by IntegrationTests. All tests run only in GitHub Actions.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Treat identities, credentials and policy inputs as untrusted. Never commit secrets or claim a security test passed without its exact CI run and artifacts.

## Read-first and canonical slice ownership
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned slice: `Authorization`; target feature path: `Features/Authorization/`, matching `docs/Features/Authorization.md`.
- `AuthorizationPolicy.cs` is the current entry point; policy behavior belongs in the named slice.
