# KeyLoad.ComparisonHost

## Purpose, entry points and ownership

- Owns the benchmark client: a TUnit project that measures one database, data type
  and node count per run. The contract is
  [BenchmarkComparisons](../../docs/Features/BenchmarkComparisons.md); the decision
  is [ADR-150](../../docs/ADR/ADR-150-per-type-benchmark-jobs.md).
- Entry points: one TUnit class per data type in `<Type>/Cases/` (for example
  `Graph/Cases/GraphBenchmarks.cs`), selected with
  `--treenode-filter "/*/*/<Class>/*"`, and the shared fixture
  `Deployment/Hosting/BenchmarkDeployment.cs`.
- `Deployment/` reads the job from `Benchmarks__*` environment settings, starts
  `build/KeyLoad.LoadTests.AppHost` through `Aspire.Hosting.Testing`, waits for its
  resources and reads the published connection. `KeyLoadDatabase` gives every
  KeyLoad driver the same SDK clients and partition.
- `Measurement/` owns the standard settings (seed 1729, 16 clients, 100,000
  operations, 1,000,000 records), `OperationLanes` and the result file records.
- The AppHost owns every container and its cleanup; this project owns the
  workload, the verification and the result file.

## Shared modules: change a dataset or scenario once

- Each data type has exactly one module, `<Type>/{Dataset,Scenario,Cases,Drivers}`.
  `Dataset/` generates the standard data and any independent oracle; `Scenario/`
  owns the case ids, schedule and verification of every answer; `Cases/` runs the
  cases in order; `Drivers/` holds the driver interface and its factory.
- A database implements only its driver: the interface's operations through the
  database's own client or HTTP API. A driver never generates data, schedules calls,
  retries for a better number or checks answers.
- Change a dataset, scenario or verification once in its module; it applies to
  every database of the type. Never fork a scenario per database.
- Adding a database adds its driver, its factory entry, its AppHost resource and its
  workflow matrix rows. Adding a type, case or database, or changing a dataset,
  schedule or count, needs the owner's approval.

## Boundaries and protected risks

- Follow the [root policy](../../AGENTS.md). Record what happened: a failed case
  keeps its real first error, and no value is estimated, zeroed or filled in.
- Rewrite the result file atomically after startup, after every case and on
  disposal, so a failed or timed-out job still uploads what it measured.
- No evidence machinery: no runtime bundles, hash or identity proofs, resource
  observation, receipts, aggregation or GitHub metadata capture. No mocks or
  in-memory substitutes for a database.
- Keep credentials out of results and logs. All files live below the test binaries'
  `AppContext.BaseDirectory` or the configured `Benchmarks__Output`.

## Commands and verification

From the repository root, one cell at a time:

```sh
dotnet build benchmarks/KeyLoad.ComparisonHost/KeyLoad.ComparisonHost.csproj -warnaserror
dotnet benchmarks/KeyLoad.ComparisonHost/bin/Debug/net10.0/KeyLoad.ComparisonHost.dll --output Detailed --treenode-filter "/*/*/GraphBenchmarks/*"
```

Set `Benchmarks__Database`, `Benchmarks__Type`, `Benchmarks__Nodes` and, for
KeyLoad, `Benchmarks__KeyLoadImage` first; `Benchmarks__Records`,
`Benchmarks__Operations` and `Benchmarks__Output` are optional. Local results are
development evidence; published figures come only from the Benchmarks workflow.

## Skills

Use the installed Aspire skill for the AppHost boundary, the Orleans skill for
KeyLoad runtime questions and the profiling skill before any optimization claim.
Follow explicit owner scope and dependency-adoption rules.
