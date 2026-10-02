# KeyLoad.Analyzers.Tests

## Purpose and entry points
- Own real Roslyn diagnostic regressions under `Features/CodeQuality/`; entry point is `KeyLoad.Analyzers.Tests.csproj`. Follow CodeQuality acceptance and ADR-033.

## Boundaries and ownership
- TUnit on Microsoft.Testing.Platform is the only runner. Compile valid product examples with real SDK Roslyn and real Orleans metadata. Source snippets are compiler inputs, never substitutes for external framework types.
- No mocks, stubs, fake Orleans attributes/interfaces or package implementation copies. Assert semantic fixture compiler success before asserting analyzer findings.
- Test all applicable positive, negative and edge paths, precise IDs, severities and locations. Do not weaken tests to hide analyzer faults.

## Commands and verification
- Development build: `dotnet build tests/KeyLoad.Analyzers.Tests/KeyLoad.Analyzers.Tests.csproj --no-restore --configuration Release`.
- CI-only test: `dotnet test --project tests/KeyLoad.Analyzers.Tests --no-build --no-restore --configuration Release`. Never execute local tests or qualifications.

## Applicable skills
- No repository skills installed. Do not install skills/tools or change global configuration.

## Protected risks
- Test-only normal analyzer assembly reference is distinct from central Analyzer attachment. Prevent recursive analyzer references and retain TUnit execution evidence at the delivered SHA.
