# Folder ownership

Folder names describe their current implementation responsibility. The first
level of a multi-feature project names the capability; the next level names its
role, such as `Contracts`, `Commands`, `Queries`, `Hosting`, `Lifecycle`,
`Serialization` or `Validation`. A single-feature project places roles directly
under its project root. Test themes belong below their role folders.

## Orleans and storage owners

| Folder | Contents | Owning specification |
| --- | --- | --- |
| `OrleansHosting` | Silo configuration, membership options, host lifetime, node status and telemetry | [Orleans execution](../Features/OrleansExecution.md) |
| `RequestExecution` | Connection/command grains, admitted requests, CQRS response streams, cancellation and draining | [Orleans execution](../Features/OrleansExecution.md) |
| `RuntimeJournaling` | runtime journal records, operations, provider and startup | [Runtime journal](../Features/OrleansExecution/RuntimeJournal.md) |
| `StoragePartitions` | Physical partition identity, placement witnesses, record inventory and node-local physical sessions | [storage ownership](../ADR/ADR-136-orleans-partition-ownership.md) |
| `CommandOutcomes` | Persisted command result records, partition selection and outcome keys | [Orleans execution](../Features/OrleansExecution.md) |
| `BackgroundExecution` | Shared bounded worker contracts, storage reads and host registration | [Execution primitives](../Features/OrleansExecution/ExecutionPrimitives.md) |
| `StorageReplication` | Storage copy contracts and coordination through Orleans calls | [Storage replication](../Features/StorageReplication.md) |
| `StorageEngine` | ZoneTree configuration, reads, commits, WAL, checkpoints and joined disposal | [Storage recovery](../Features/StorageRecovery.md) |
| `PointCache` | Bounded ZoneTree point-read cache and its admission/lifetime | [Resource execution](../Features/ResourceExecution.md) |
| `StorageExecution` | Core storage apply and stored-command access | [Storage recovery](../Features/StorageRecovery.md) |
| `CommandExecution` | Core command creation, input selection, fingerprints and validation | [Internal serialization](../Features/InternalSerialization.md) |

`OrleansExecution` is the specification and cross-cutting test suite for execution
Orleans integration. Production code uses the narrower owners above. Orleans
still owns cluster membership, placement, communication and scheduling.

Administrator startup authentication lives under `Authorization/Hosting/Administrator`.
Table deletion lives under `RelationalStorage`; the analytics dependency notice
lives under `AnalyticsStore/Assets`. Domain records and their stable field
declarations live with their domain. `InternalSerialization` retains shared
serialization infrastructure and shared alias declarations.

## Retained capability names

The populated project and role directory inventory was inspected across `src`,
`build`, `analyzers`, `benchmarks`, `tests`, `scripts` and `docs`. This is a
structural naming review, not a behavioral audit of every implementation.

| Folders | Named responsibility |
| --- | --- |
| `AdminDashboard`, `AnalyticsStore`, `Authorization`, `BlobStorage` | Administration UI/observations, user analytics, access control and file metadata/provider access |
| `DocumentStorage`, `RelationalStorage`, `GraphTraversal`, `Search`, `TimeSeries` | Their corresponding database models and operations |
| `Messaging`, `EventStreams`, `ChangeFeeds` | Queues and workflows, durable event records, change cursors and projection delivery |
| `ClientApi`, `QueryExecution`, `DatabaseComposition` | Public transports, query compilation/execution and cross-model operations |
| `ResourceExecution`, `Storage`, `InternalSerialization` | Shared resource admission/effects, storage interfaces/codecs and internal binary serialization |
| `Restore` | Backup archive creation, verification and restoration; the existing specification is `BackupRestore.md` |
| `StorageRecovery` in tests and specifications | Fault, recovery and storage correctness qualification; the implementation owners are listed above |
| `BenchmarkComparisons`, `TestInfrastructure`, `CodeQuality`, `ReleaseDelivery`, `RepositoryGovernance` | Comparison workloads, test orchestration, compiler/quality rules, publication tooling and repository checks |

Single-purpose analyzer, diagnostics, comparison host and load-test host projects
keep their roles directly below the project root. Build outputs, dependency
directories and original qualification artifacts are not renamed. Stable
serializer aliases, field IDs, public route strings and telemetry identities
remain unchanged. Existing requirement and acceptance identifiers remain the
qualification authority; directory moves do not close any runtime gate.

The primary AppHost keeps test profiles, process-fault commands and their
observations in `TestInfrastructure`; Orleans telemetry resource registration
belongs to `OrleansHosting`. Shared background roles do not repeat `Background`
below `BackgroundExecution`, and test case folders do not repeat their feature
name below `Cases`.

## Verification boundary

The structural governance check and literal project-file reference check passed
after the owner moves. Dependency restore also passed. Formatting was cancelled
when another operation began renaming files and types throughout the shared
checkout. The required solution build with `--no-restore -warnaserror` stopped at
`FunctionalCompilationIdentity.targets` because central project inputs changed
during compilation; its embedded source identity was rejected as non-authoritative.
A consistent source snapshot is required before completing formatting, solution
compilation and test execution. Source-bound inventory hashes and line
ranges must be reviewed against that final snapshot; updated paths alone do not
qualify the inventory or any test result.
