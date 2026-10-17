# KeyLoad.AppHost

## Purpose and entry points
- Owns Aspire application composition for KeyLoad services and infrastructure resources.
- Entry point: `Program.cs`; benchmark resource composition: `BenchmarkComparisons/Resources/SessionTopology/BenchmarkResources.cs`.

## Ownership and boundaries
- Keep host wiring and genuinely shared infrastructure composition here. Feature-specific deployment behavior belongs under `<SliceName>/` and the matching `docs/Features/<SliceName>.md`.
- Aspire owns resource orchestration, not database storage state or atomic partition identity. Each node-local `PartitionHost` owns storage, journals, file locks and the apply gate; Orleans grains route commands and physical placement remains separate from atomic partition identity.
- ClusterResources composes actual Docker RF3 resources. Do not replace the required Docker/Aspire RF3 topology with an in-memory or single-node demonstration. The accepted ADR-082 protocol-cohort test seam preserves fixed three-voter membership and node-local storage ownership.

## Commands and evidence
- The canonical Aspire-owned test entry is defined by root AGENTS.md and ADR-074. AppHost defaults to Release because the pinned Aspire CLI evaluates RunCommand without forwarding outer dotnet-run configuration; verify the actual launched path and native TUnit outcomes. Do not bypass CLI/DCP or count stale Debug output as current qualification.
- GitHub Actions solution build: `dotnet build KeyLoad.slnx --no-restore --configuration Release`.
- Owner correction2026-10-03 requires every test caller, including integration/RF3, to use `dotnet run --project build/KeyLoad.AppHost --no-build --no-restore --configuration Release -- --KeyLoadTests:Suite=<suite>`. Native dotnet test is only the owned child process; preserve all suites and original artifacts. Delivered-source qualification remains actual Linux GitHub Actions evidence.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Owner correction2026-10-03 explicitly permits local development through the same Aspire-owned entry. Preserve secret boundaries, resource health dependencies, scoped fault injection, complete resource shutdown and the required Orleans RF3 topology; local results do not qualify delivered-source Linux or public performance gates.

## Read-first and canonical slice ownership
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned composition slices: `ClusterReplication`, `ClusterRouting`, `ClientApi`, `TestInfrastructure` and `BenchmarkComparisons`; target paths use the same `<SliceName>/` convention. ClusterRouting's protocol image overrides follow NativeCqrsRequestV2 and ADR-082; they are allowed only in an explicitly selected ephemeral fixed-three-voter child test topology, never benchmark or ordinary production composition.
- `ClientApi/Resources/ClusterConnectionEndpointResources.cs` owns the named direct HTTP/2 container endpoint under ADR-125, preserving original HTTP/1.1 endpoints, peer identity, readiness and genuine RF3 resources. Actual parallel-connection SDK/official MCP tests discover this endpoint through Aspire; no proxy or synthetic session can substitute for its accepted TCP connection.
- Keep `Program.cs` as the shared Aspire composition entry point; put feature-owned resource definitions under their named slice.
- `CodeQuality/` owns the test-only native functional coverage configuration and Aspire preparation/resource dependencies under ADR-033 and CodeQuality AC-CQ-039..042. Use the same centrally pinned Microsoft engine as MTP, downloaded as a package; do not install a global tool. Preserve exact contributor/source/DLL/PDB/image identity, original process settlement and all full mandatory suites. Runtime coverage still comes from the original owned server processes, not the AppHost assembly.

## Vertical-slice responsibility folders
- Keep feature-owned implementation inside its canonical `<SliceName>/` and organize it in populated, feature-local responsibility folders (such as `Models/`, `Contracts/`, `Commands/`, `Queries/`, `Transport/`, `Hosting/`, `Serialization/`, or `Validation/`). Do not leave a flat dump of unrelated responsibilities at the slice root; keep only genuinely shared building blocks and executable/composition entry points outside feature slices. Preserve namespaces and runtime contracts during physical moves.

## Native TUnit entry, owner correction 2026-10-07
- ADR-117 supersedes the earlier outer AppHost caller requirements: CI starts TUnit directly after build with Detailed output. Test fixtures own Aspire infrastructure startup, readiness, client operations and cleanup. scripts/Features/TestInfrastructure/run-tests.mjs only selects native test arguments/environment; it cannot execute database workloads. RF3 coverage preparation belongs to the TUnit session lifecycle. Preserve every original qualification/artifact gate and separate Benchmarks ownership.


## TASK-TEST-APPHOST-NATIVE-SIMD-001 — current normal-only suite composition

Owner2026-10-11 mandatory native SIMD supersedes every earlier active unit-scalar/runner-disabled-intrinsics clause, including REQ/AC-TEST-009 and historical suite examples. Immutable original scalar reports remain history, never current qualification. Current TestSuiteProtocol and ProjectForSuite admit only analyzers/unit/recovery/rf3/comparison/site; former unit-scalar is unknown and refuses before resource creation. TestSuiteResources removes only its explicit DOTNET_EnableHWIntrinsic=0 runner branch; native50 ordinary/exclusiveheavy1, explicit supported parallelism, original comparison isolation1, all project/argument/TRX/coverage/path selection and genuine Aspire preparation/readiness/clients/shutdown remain unchanged. No old ignored alias, scalar alternate or limits/configuration fallback.

Native audit proves TestSuiteApplication, TestSuiteSettingsReader and typed execution policy have no scalar-specific behavior, so their cancellation, output/process joins and failure ledgers remain byte-exact. Existing AspireTestEntryModelTests same declarations verify the complete six-suite model, exact native argument/project/results shape and absent injected disable environment. FilterIsOneNativeArgumentAndIsBoundedBeforeResourcesAreAdded now refuses old unit-scalar before its original genuine healthy model; full negative and healthy controls/disposal remain. Its independent default ordinary literal is50, while the explicit rejected comparison20 configuration remains independently20. This source-owned model remains in ComparisonTests and executes only through authorized GitHub Benchmarks under its local policy; no local benchmark workload is started. Existing FunctionalTestParallelismTests/NativeTUnitSelectionTests normal controls and final all-source compiler/format/native census remain mandatory.

Ordered source owners: this spec/local AppHost policy/ADR first; exact protocol and selection retirement; resource branch retirement; original complete model assertions. Root alone joins/builds/executes live inputs. No product/public/persisted ID or storage-format change, migration, clock/deadline/resource increase. Rollback cannot reinstate the superseded scalar mode; preserve originals and repair the same normal-native contract. Current execution/coverage/Linux qualification is OPEN until actual source/DLL/PDB/discovery/reports/cleanup bind. Native source-path/count arithmetic is not UID or PASS.
