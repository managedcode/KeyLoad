# KeyLoad load-test infrastructure

## Purpose, entry points and ownership

- Owns the Aspire deployment of exactly one database and node count for one
  benchmark job ([BenchmarkComparisons](../../docs/Features/BenchmarkComparisons.md),
  [ADR-150](../../docs/ADR/ADR-150-per-type-benchmark-jobs.md)).
- `Program.cs` visibly composes, builds, runs and disposes the application.
  `Resources/BenchmarkTopology.cs` reads `Benchmarks:Database`, `Benchmarks:Nodes`,
  `Benchmarks:KeyLoadImage` and `Benchmarks:DataRoot`; `Configuration/BenchmarkDatabases.cs`
  lists the databases and the node counts each supports; `Identity/IsolatedDispatcher.cs`
  adds the selected database's resources.
- The benchmark client (`benchmarks/KeyLoad.ComparisonHost`) starts this AppHost
  through `Aspire.Hosting.Testing` and reads `LoadTestClientSettings`: the
  resources that must become healthy, the bootstrap containers that must exit with
  code 0 and the published connection string, endpoints and credentials. The
  client owns the workload, its verification and the result file; never add
  database work or the workload to this resource graph.
- The ordinary development and functional-test host is `../KeyLoad.AppHost`. Do not
  introduce another AppHost, resource runner or forwarding project.

## Boundaries and protected risks

- Every database runs from its pinned image and entry point. KeyLoad runs the
  Release-published `ghcr.io/managedcode/keyload` server image pinned to the digest
  in `Benchmarks:KeyLoadImage`, with `KeyLoad:SiloCount` and `KeyLoad:ReplicaCount`
  equal to the node count and the in-memory RapidMembership provider discovering
  silos on the container network. This host never builds or publishes a KeyLoad
  image.
- A node count a database does not support is rejected before any resource starts;
  never present standalone instances as a cluster.
- Containers use session lifetime and anonymous writable volumes; readonly
  bootstrap and configuration mounts stay. Disposing the application removes only
  the resources this run created and preserves unrelated Docker resources.
- No evidence machinery: no resource observation, hardware capture, provenance
  options or receipts. Do not implement a database runtime, membership algorithm,
  scheduler or second container lifecycle here.
- Follow the [root policy](../../AGENTS.md) for storage safety, authorization,
  package ownership, versions, numeric gates and shared-checkout work.

## Commands and verification

From the repository root:

```sh
dotnet build build/KeyLoad.LoadTests.AppHost/KeyLoad.LoadTests.AppHost.csproj --no-restore -warnaserror
```

A real deployment is exercised by running one benchmark cell (see the
BenchmarkComparisons feature). A build or a resource model alone proves no runtime,
replication or performance behavior.

## Skills

Use the installed Aspire skill for resource composition and lifecycle and the
Orleans skill for KeyLoad cluster settings. No additional library, container
framework or package version is authorized by this policy.
