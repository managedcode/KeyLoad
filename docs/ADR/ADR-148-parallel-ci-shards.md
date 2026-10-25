# ADR-148: Parallel CI shards

Status: Accepted by owner direction 2026-10-24 ("every test suite runs in parallel so CI is fast; Release runs the tests before publishing"). In source; Linux qualification pending.

## Context

Build and Tests ran every suite one after another in one job on a 4-vCPU ubuntu-24.04 runner. Run 40091311302 (commit 336e6b566) took more than two hours and still did not finish:

- Units took 30 minutes and also executed all 215 process recovery cases a second time. KeyLoad.UnitTests references KeyLoad.RecoveryTests, and an unpinned filter registers the referenced assembly's tests.
- In the Aspire topology suite, TUnit 1.76 sized `--maximum-parallel-tests` at 4 × processors, so 16 test classes that each start their own 3-node cluster started at once. That is 48 silo processes on four cores. None became ready within the 120-second resource wait, and 27 cluster cases failed at startup. Alone, a 3-node cluster is ready in about 22 seconds; with about 15 at once it took 111 seconds.
- Every other topology class carries a bare `[NotInParallel]`. TUnit schedules those last, strictly one at a time, so the run had covered 118 of about 357 cases when its 100-minute limit expired.

## Decision

Build and Tests is a set of parallel jobs driven by one checked-in manifest, `scripts/Features/TestInfrastructure/ci-test-shards.json`, and its runner `ci-test-shards.mjs`:

- **Build:** governance and website inventory checks, the static shard partition check and `dotnet build KeyLoad.slnx -warnaserror`.
- **Units:** four shards. Each runs one process per namespace with the assembly-pinned filter `/KeyLoad.UnitTests/<namespace>/*/*` at processor-count parallelism.
- **Analyzers and process recovery:** one job with filters `/KeyLoad.Analyzers.Tests/*/*/*` and `/KeyLoad.RecoveryTests/*/*/*`.
- **Aspire topology:** twelve shards of two lanes each.
  - A lane is its own `dotnet test` process with `--maximum-parallel-tests 1` and a 45-minute deadline, so a runner holds at most two clusters. Measured lanes take 20–25 minutes, so the deadline sits well above a complete lane.
  - Lanes are typed: one keyed shared cluster, own clusters, other keyed clusters, or one long functional gate method.
  - Every lane process owns its own keyed shared cluster, so a test that kills, restarts or cold-reopens nodes cannot affect another lane.
- **Verify test partition:** reads every shard's TRX artifact and proves that each test ran exactly once, in its assigned shard and lane.

Each test job builds the whole solution with `-warnaserror` in Debug and uploads its TRX reports even when tests fail. A single-project build is not enough: unit CodeQuality checks read the compiled image of every solution project, and the cold bootstrap topology test launches the KeyLoad CLI, which no test project references. The Release workflow calls Build and Tests through `workflow_call` and gates every publishing job on it.

Partition correctness is checked twice:

- **Statically, before any build:** every namespace or class is in exactly one shard or lane, class names are unique (filters match case-insensitively), excluded `HeavyLoad` classes stay out, split classes have every method assigned, and lane kinds keep the per-runner cluster bound.
- **After execution:** no test identity appears in two reports, every inventory class ran, and nothing ran outside its suite, shard or lane.

## Consequences

- Wall time is bounded by the slowest shard instead of the sum of all suites. The estimate is about 25–28 minutes, assuming two lanes per runner run about 1.3 times slower than alone. Lane estimates come from run 40091311302 and need rebalancing from the first sharded reports.
- The workflow uses 19 test-related jobs at once (Build, the planning job, four unit shards, one analyzers-and-recovery job, twelve topology shards) plus Verify test partition at the end.
- Adding a test class is a one-line manifest change. `inventory <suite>` shows the cluster each class uses and whether it disrupts nodes. A class missing from the manifest fails Build.
- Product behaviour and test assertions are unchanged. No product request or admission limit is added; the bound is on concurrent test processes per runner.
- Static source scanning is not runtime proof. Discovery, the TRX identities and the topology lanes are qualified only by actual Linux GitHub runs.
