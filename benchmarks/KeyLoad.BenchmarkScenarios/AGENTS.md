# KeyLoad.BenchmarkScenarios

## Purpose and entry points
- Owns the public embedded microbenchmark fixture library required by BenchmarkDotNet's external generated runner.
- Project entry: KeyLoad.BenchmarkScenarios.csproj; scenario entry: Features/BenchmarkComparisons/Benchmarks/EmbeddedBenchmarks.cs.
- Read root AGENTS.md, docs/Architecture.md, BenchmarkComparisons REQ-BC-022, ADR-047 and embedded-benchmark acceptance/plan before implementation.

## Ownership and boundaries
- All scenario/fixture/seeding/lifetime behavior belongs to Features/BenchmarkComparisons and docs/Features/BenchmarkComparisons.md.
- The fixture remains public and unsealed with public Benchmark/GlobalSetup/GlobalCleanup methods because the actual generated external consumer inherits it. Document its public contract with XML; never suppress CA1515 or spoof output/test metadata.
- KeyLoad.Benchmarks owns the executable and typed BenchmarkSwitcher application; this library owns no CLI, comparative corpus/report/site, database implementation, RF3 orchestration or qualification claim.
- Preserve the three original timed operations, inputs, real engine/store and MemoryDiagnoser. No fake storage, in-process toolchain substitution, optional fallback, payload duplication or local load evidence.
- Keep acquired storage/directory ownership explicit, IDisposable correct and cleanup complete after partial setup; TimeProvider.System preserves real command time.

## Commands and verification
- Development restore/build: dotnet restore benchmarks/KeyLoad.BenchmarkScenarios/KeyLoad.BenchmarkScenarios.csproj; dotnet build benchmarks/KeyLoad.BenchmarkScenarios/KeyLoad.BenchmarkScenarios.csproj --no-restore --configuration Release.
- Tests and benchmarks execute only in GitHub Actions. Real TUnit UnitTests exercise fixture metadata/lifetime and the actual executable's generated Dry consumer under the root ci.yml workflow; retain exact SHA/run/job/raw artifacts.
- Canonical formatter, central analyzer policy, governance and full solution gates remain mandatory. No package lock files; use only centrally pinned .NET10/C#14/packages. This library is nonpackable.

## Skills and protected risks
- Applicable skills: none installed; do not install skills/tools or change global configuration.
- All file/type/unit/depth400/200/50/3 limits apply; no partial-type loophole or analyzer severity/threshold weakening.
- Preserve unrelated shared-checkout source. No local tests/recovery/runtime probes/load benchmarks, secrets, ignored failures, skipped suites or invented resource/performance numbers.
- Dry/generated-runner qualification establishes execution only; never report RF3, power-loss, matched performance, coverage, endurance or production readiness from it.

## Owner-authorized local optimization, 2026-10-03
- The explicit root owner correction supersedes the historical local execution prohibitions above for development verification. Use real local BenchmarkDotNet processes and bounded native fixtures to optimize code, retaining actual source, settings, machine and originals. Ordinary correctness tests also execute in CI; internal raw/code measurements must not appear in benchmarks.yml. Public database figures require authenticated original GitHub full-database comparisons with verified comparable hardware, resources, topology, durability and workloads.

## Vertical-slice responsibility folders
- Keep feature-owned implementation inside its canonical `Features/<SliceName>/` and organize it in populated, feature-local responsibility folders (such as `Models/`, `Contracts/`, `Commands/`, `Queries/`, `Transport/`, `Hosting/`, `Serialization/`, or `Validation/`). Do not leave a flat dump of unrelated responsibilities at the slice root; keep only genuinely shared building blocks and executable/composition entry points outside feature slices. Preserve namespaces and runtime contracts during physical moves.
