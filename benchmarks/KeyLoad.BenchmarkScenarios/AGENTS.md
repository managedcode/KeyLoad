# KeyLoad.BenchmarkScenarios

## Purpose, entry points and ownership

- Owns the public embedded microbenchmark fixture library required by BenchmarkDotNet's external generated runner.
- Project entry: KeyLoad.BenchmarkScenarios.csproj; scenario entry: Benchmarks/EmbeddedBenchmarks.cs.
- Read root AGENTS.md, docs/Architecture.md, BenchmarkComparisons REQ-BC-022, ADR-047 and embedded-benchmark acceptance/plan before implementation.
- All scenario/fixture/seeding/lifetime behavior belongs to BenchmarkComparisons and docs/Features/BenchmarkComparisons.md.
- The fixture remains public and unsealed with public Benchmark/GlobalSetup/GlobalCleanup methods because the actual generated external consumer inherits it. Document its public contract with XML; never suppress CA1515 or spoof output/test metadata.
- KeyLoad.Benchmarks owns the executable and typed BenchmarkSwitcher application; this library owns no CLI, comparative corpus/report/site, database implementation, cluster orchestration or qualification claim.
- Preserve the three original timed operations, inputs, real engine/store and MemoryDiagnoser. No fake storage, in-process toolchain substitution, optional fallback, payload duplication or local load evidence.
- Keep acquired storage/directory ownership explicit, IDisposable correct and cleanup complete after partial setup; TimeProvider.System preserves real command time.
- Apply the installed Orleans and Aspire skills when their runtime or composition boundaries are involved.
- All file/type/unit/depth400/200/64/3 limits apply; no partial-type loophole or analyzer severity/threshold weakening.
- Preserve unrelated shared-checkout source. No local tests/recovery/runtime probes/load benchmarks, secrets, ignored failures, skipped suites or invented resource/performance numbers.
- Dry/generated-runner qualification establishes execution only; never report cluster, power-loss, matched performance, coverage, endurance or production readiness from it.

## Boundaries and protected risks

Follow the [root policy](../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet build KeyLoad.slnx --no-restore -warnaserror
```

Use the root's complete solution, TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
