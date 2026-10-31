# KeyLoad.BenchmarkScenarios

## Purpose, entry points and ownership

- Owns the public embedded microbenchmark fixture library required by BenchmarkDotNet's external generated runner.
- Project entry: KeyLoad.BenchmarkScenarios.csproj; scenario entries: Benchmarks/EmbeddedBenchmarks.cs and the other classes under Benchmarks/.
- Read root AGENTS.md, docs/Architecture.md, BenchmarkComparisons REQ-BC-022, ADR-047 and embedded-benchmark acceptance/plan before implementation.
- All scenario/fixture/seeding/lifetime behavior belongs to BenchmarkComparisons and docs/Features/BenchmarkComparisons.md.
- The fixture remains public and unsealed with public Benchmark/GlobalSetup/GlobalCleanup methods because the actual generated external consumer inherits it. Document its public contract with XML; never suppress CA1515 or spoof output/test metadata.
- KeyLoad.Benchmarks owns the executable and typed BenchmarkSwitcher application; this library owns no CLI, comparative corpus/report/site, database implementation, cluster orchestration or qualification claim.
- There are two kinds of benchmarks. Public comparison benchmarks against other databases run in the GitHub Benchmarks workflow and alone produce published figures; they live in KeyLoad.ComparisonHost, not here. Local development benchmarks live here: add any measurement that helps find or prove a KeyLoad change (for example commit throughput or journal flush cost) and run it locally; such measurements are development evidence and are never published as comparative figures.
- Preserve the three original timed operations of `EmbeddedBenchmarks`, their inputs, the real engine/store and MemoryDiagnoser. Every benchmark uses the real engine and storage: no fake storage, in-process toolchain substitution or optional fallback.
- Keep acquired storage/directory ownership explicit, IDisposable correct and cleanup complete after partial setup; TimeProvider.System preserves real command time.
- Apply the installed Orleans and Aspire skills when their runtime or composition boundaries are involved.
- All file/type/unit/depth400/200/64/3 limits apply; no partial-type loophole or analyzer severity/threshold weakening.
- Preserve unrelated shared-checkout source. No secrets, ignored failures, skipped suites or invented resource/performance numbers; report a local measurement with its machine, configuration and exact command.
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
