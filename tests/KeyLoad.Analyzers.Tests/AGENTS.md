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
## Feature slice responsibility folders
- Place every feature-owned source file under `Features/<SliceName>/<Role>/`, using populated feature-local roles such as `Cases/`, `Fixtures/`, `Assertions/`, `Models/`, `Processes/`, `Contracts/`, `Serialization/` or `Helpers/` according to the file's actual responsibility. Preserve an existing nested scenario/domain folder and add the role beneath it. Create only roles that own files.
- Keep genuinely shared test infrastructure and project composition entry points at their existing shared ownership paths; do not duplicate them into a feature.
- Structural moves preserve every source byte, namespace, type/serializer identity and test assertion. Do not change behavior, test logic or path references as part of a layout-only move; report source-path-sensitive joins to the solution integrator.

## Native TUnit entry, owner correction 2026-10-07
- ADR-117 supersedes the earlier outer AppHost caller requirements: CI starts TUnit directly after build with Detailed output. Test fixtures own Aspire infrastructure startup, readiness, client operations and cleanup. scripts/Features/TestInfrastructure/run-tests.mjs only selects native test arguments/environment; it cannot execute database workloads. RF3 coverage preparation belongs to the TUnit session lifecycle. Preserve every original qualification/artifact gate and separate Benchmarks ownership.
