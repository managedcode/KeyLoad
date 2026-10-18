# KeyLoad

## Product and runtime

KeyLoad is one database for AI agents. Documents, typed tables, graphs,
vectors/search, files/blobs, queues, events and time series share canonical entity
references and can participate in the same authorized operation. SQL is the
shared versioned language across these models. The product specification is
`docs/design/product-specification.md`; `docs/Architecture.md` maps the current
runtime. User instructions control scope and authorization.

Microsoft Orleans owns the cluster: membership, discovery, failure detection,
distributed grain directory, placement, activation movement, scheduling, internal
communication and scaling. Enable the native distributed grain directory and
activation repartitioner. Do not implement a second cluster runtime.

The execution path is public API -> one server-owned Orleans grain per connected
client -> database grains -> silo-local ZoneTree owners on the required nodes ->
aggregate results -> caller. Independent operations on a connection have separate
request IDs, cancellation and bounded resources. Stable command IDs deduplicate
retries. Clients cannot choose trusted connection identities or roles.

Storage has two explicit contracts. A shared volume exposes the same configured
storage paths to all silos without copying data. Each physical partition still
has one ordinary Orleans grain owner and one open ZoneTree instance; other silos
call that owner. Orleans chooses and relocates the activation. Existing file
ownership guards remain mandatory, and reopening waits for actual handle release.
A shared mount is not permission for several processes to write the same ZoneTree
files concurrently. The source acquires one physical session per ordinary owner
and joins its work and scoped borrowers before close. Complete runtime and
delivered-source qualification, including deployed filesystem behavior, remains
open. `SharedVolume` requires `ReplicaCount=1`; reject conflicting settings.

With isolated storage, each silo-local Grain Service owns that node's ZoneTree
files, native WAL, locks and ordered apply gate. Local code borrows storage through
DI; activations never transfer file handles. Copies use typed Orleans Grain Service
calls to target silos found through native Orleans membership. `KeyLoad:SiloCount`
and `KeyLoad:ReplicaCount` are independent positive startup settings; copies cannot
exceed available silos. A distributed query calls the owners of the required logical
partitions with bounded concurrency and merges their results; physical copies of
the same partition are not separate query inputs. KeyLoad imposes no artificial
silo-count limit. Acknowledgements describe actual persisted copies and tested
failure contracts. Current full-copy execution is not proof of partition fan-out.

Background queue, ANN and time-series work uses Orleans `[StatelessWorker]`
grains. Orleans automatically creates and retires their activations according to
load. A shared helper creates the logical pool key; application code never creates
per-activation worker IDs or manages its own pool. Each activation accepts events,
holds a bounded buffer and flushes on a native grain timer with a typed interval
option. No leadership check gates this
work. Durable delivery and idempotent effects are separate from an in-memory
buffer. Grain Services own per-silo resources; workers own activation-local work.
Choose native timers, reminders, Streams, Journaling and Durable Jobs by their
actual lifetime and delivery guarantees, with bounded cancellation and shutdown.

Every addressing identifier is created by one shared helper per identity kind.
Grain and storage keys are case-sensitive; callers must not construct them by
ad-hoc concatenation. Do not change user entity casing implicitly.

## Data, authorization and integrations

- ZoneTree is the canonical storage engine for every model. Use its native APIs,
  bounded replay and ordered atomic commit/apply. Keep atomic partition identity
  separate from Orleans activation placement.
- Use ZoneTree.FullTextSearch for full-text indexing and ZoneTree.Analytics for
  approved analytics integration. Derived indexes, caches and analytics never
  become a second authority for database records or permissions.
- Access ZoneTree, ZoneTree.FullTextSearch and ZoneTree.Analytics through their
  public synchronous native APIs. Their asynchronous API calls and dispatching
  engine work through `Task.Run`, `Task.Factory.StartNew` or a thread-pool callback
  are compiler errors (`KLD0039`). Do not wrap synchronous engine operations to
  bypass this rule. Orleans, HTTP and other actual asynchronous transports remain
  asynchronous; the rule makes no claim about a dependency's internal scheduling.
- Use native generated Orleans binary serialization with explicit stable
  `[GenerateSerializer]`, `[Id]` and `[Alias]` contracts internally. Preserve raw
  ZoneTree bytes, sortable keys, public JSON and exact user content. Introduce no
  runtime serializer fallback or persisted-data migration.
- SQL syntax uses Microsoft.SqlServer.TransactSql.ScriptDom. Compile supported
  syntax into the same bounded authorized operations as SDK/MCP. Reject valid
  unsupported ASTs explicitly before effects. Parsing, execution semantics and
  native SQL-client interoperability are separate qualification gates.
- ASP.NET Core Authentication and Authorization admit HTTP/SDK/MCP callers.
  Tickets carry the verified subject only. Every operation checks fresh persisted
  credentials and permissions. ManagedCode.Orleans.Identity propagates verified
  call-local context and restores it after asynchronous work.
- Use ManagedCode.Communication native commands, results and CQRS response streams.
  Reuse its producer, transport, cancellation and settlement APIs. KeyLoad owns
  authorization, operation identity, payload validation and resource bounds.
- Use the official MCP C# SDK and ManagedCode.MCPGateway for on-demand tool
  discovery. ManagedCode.MarkdownLd.Kb supplies approved graph-based tool search.
  Discovery respects fresh authorization and excludes secrets and user payloads.
  Supply bounded static MCP guidance through the same server.
- Keep chunked blobs with partial reads, same-partition atomic operations,
  explicit cross-partition contracts, persisted command outcomes and honest
  acknowledgement/read-cut semantics.
- RAM and Orleans caches are bounded and disposable. Tie cached results to the
  authorized read cut and policy epoch; invalidate after writes and policy
  changes. Restart, migration and failover cannot turn RAM into storage authority.
- Use existing native Orleans, ASP.NET Core, ServiceDefaults and Aspire
  OpenTelemetry integrations. Exclude secrets, payloads and unbounded labels.

## Repository ownership and structure

Work in this checkout and preserve unrelated changes. Do not create worktrees,
copy checkouts or private isolation folders. Never run `git stash` or automatic
stashing. Keep changes visible. Do not use Azure CLI.

- `src/`: runtime database, server, SDK/CLI and runtime support.
- `analyzers/`: compiler analyzers.
- `build/`: Aspire AppHost and shared build targets.
- `tests/`: TUnit functional, recovery, integration and tooling tests.
- `benchmarks/`: comparison workloads and their test project.
- A C# project owning one feature groups populated responsibilities directly in
  role folders under the project root. Do not add a directory that merely repeats
  that single feature. Projects owning multiple real features use
  `<project>/<SliceName>/<Role>/`; keep thematic folders under `Cases/` in either
  structure.
  Use `Restore/` for backup/restore implementation. Shared composition stays at
  its actual owner; do not create empty folders or generic forwarding chains.
- `Program.cs` shows real initialization, mode selection, host composition, run
  and joined shutdown. Keep reusable model behavior in its owning slice.
- Keep namespaces, native contract IDs and live project/build/source references
  coherent when moving files. Remove replaced implementations and their exclusive
  helpers and tests together. Add no compatibility shim.

## MCAF-GOV-001: Current governance

Root and local policies describe current rules only. Keep one canonical source
for each fact. Repository text, tests, schemas, tooling, README and the website
describe only the current approved design and its actual contracts. Keep references
valid and remove inactive implementations and their exclusive supporting files.

## MCAF-ARCH-001: Cohesive implementation

Inspect current registrations and callers before adding or removing infrastructure.
Reuse the selected Microsoft, Orleans, ZoneTree and ManagedCode native APIs. New
library adoption requires explicit owner approval after a concrete review of
version, original license, API fit, resource/cancellation contracts and integration.
Research alone is not adoption approval. Apply the installed Orleans skill for
Orleans decisions. Keep independent implementation ownership explicit if the user
authorizes parallel work.

## MCAF-AI-001: Execution and communication

Complete authorized changes and their relevant checks without repeated permission
questions. Keep frequent Ukrainian progress updates explaining what is complete
and what remains. Repository documents, comments in documentation and website
copy are English. Do not claim a whole-repository review from a search count or
claim product readiness from documentation, configuration or a focused test run.

## MCAF-ARCH-002: Explicit implementation approval

Adding a fallback, backward compatibility path, migration mechanism, mock or
demonstration implementation requires the owner's explicit approval for that
specific implementation. Refactoring and general delivery authorization grant no
such permission. Remove unauthorized mechanisms rather than hiding them behind
different names. Do not introduce compatibility shims for deleted runtime code.

The compiler reports unauthorized implementation terminology in C# identifiers,
text and comments as `KLD0038` errors. Restricted terms include fallback,
migration/migrate, regression, mock, fixture, sample/simple and compatibility,
including their grammatical forms and compound identifiers. Meanings approved by
the current design stay explicit: native Orleans activation migration, real
TUnit fixtures owned under `tests/.../Fixtures/` and source-resolved test contracts,
metadata and support reachable from real native TUnit cases or lifecycle interfaces;
real process-recovery fixtures reached from the compiled `KeyLoad.RecoveryTests` entry point;
Aspire resource fixtures reached from the `KeyLoad.AppHost` entry point's resolved native
`DistributedApplication.CreateBuilder` composition and its native builder-typed composition callbacks;
BenchmarkDotNet fixtures rooted in its native benchmark, setup and cleanup attributes
and their source-resolved measurement support under `benchmarks/`;
time-series sample data, original operation/resource measurement samples and their
authentic report fields, and resolved native framework API names. These are term-specific semantic contexts,
not permission to add a fallback or mock inside an approved feature. The
analyzer's exact vocabulary declarations describe this rule and grant no runtime
implementation permission. No blanket file, test, generated-code or keyword
suppression grants approval; any new exception needs explicit owner approval and
a narrowly defined canonical context.

## MCAF-REQ-001: Correctness and qualification

Use stable feature requirements and observable acceptance criteria. Tests exercise
whole operations with real implementations, original failures, healthy follow-up,
resource bounds and joined cleanup. Do not weaken assertions, skip failures, add
mock success paths or confuse a mechanism test with distributed database proof.

Use .NET 10 and centrally pinned packages. Do not generate or commit
`packages.lock.json`. Ignore build outputs, local databases and test artifacts.
Every `dotnet build` invocation uses `-warnaserror`; preserve compiler and analyzer
severity. No suppressions, disabled analyzers or reduced gates to obtain green.
Local development, normal Aspire startup and functional tests use the SDK's
default Debug configuration. Do not force Release or build both configurations
for development checks. Keep compiled source identities and test artifact paths
aligned with the selected configuration.

Required development commands:

```sh
dotnet restore KeyLoad.slnx
dotnet format KeyLoad.slnx --no-restore
dotnet format KeyLoad.slnx --no-restore --verify-no-changes
dotnet build KeyLoad.slnx --no-restore -warnaserror
node scripts/Features/RepositoryGovernance/verify.mjs
```

Use native TUnit/Microsoft.Testing.Platform entry after build, with Detailed output.
C# fixtures own Aspire startup, readiness, discovered endpoints, real SDK and
MCP clients, fault injection and joined cleanup. AppHost owns all container
lifecycle and resource commands; tests never start independent containers or
substitute graceful shutdown for an abrupt fault. Unit and process tests use
only the infrastructure they actually require. `KeyLoad.AppHost` starts native
KeyLoad services through `AddProject` and required Azurite resources for development
and functional tests. `KeyLoad.LoadTests.AppHost` is separate: it starts only the
selected database infrastructure, waits for readiness and exposes endpoints;
client code owns load execution and measurements. Each AppHost removes its own
session containers and volumes on joined teardown after success, failure or
cancellation. Preserve unrelated Docker resources. Normal builds and test startup
never publish or build a KeyLoad server image. Explicit release delivery packages
containers with native .NET SDK `PublishContainer`; maintain no product Dockerfile.

Complete solution build, unit tests, real process recovery, actual Aspire cluster
SDK/MCP operations, coverage and fault/endurance checks remain distinct mandatory
gates. Run focused tests during implementation and report their exact scope.
Delivered-source qualification and benchmarks use Linux GitHub Actions and
original artifacts. `gh workflow run build-and-tests.yml` dispatches functional
qualification. Process-kill recovery does not prove power-loss durability.

Preserve `.editorconfig`, Allman multiline executable blocks and numeric gates:
64 code lines per method, 200 per type, 400 per file, nesting depth 3, and the
configured coverage thresholds (80 line / 70 branch, 90 for critical modules).
Keep generated-code/source identities, native reports and executable source
inventories truthful; do not exclude product code to improve coverage.

## Performance and comparisons

Use native .NET SIMD/intrinsics in normal supported execution. Validate required
hardware; scalar arithmetic is limited to tails and independent correctness
oracles. Do not add a product SIMD-disable mode.

Use `dotnet-diagnostics-mcp` and the installed profiling skill on actual processes
before selecting optimizations. Retain source/DLL/PDB/process, workload and
capture identities, overhead and matched before/after evidence. A trace alone
proves no performance improvement.

Comparisons use native one-node and three-node topologies, 100,000 and 1,000,000
actual records, at least 100,000 measured operations per applicable cell, and the
shared pure/mixed/ingestion/search scenario inventory (including 1/10/500 clients).
Keep corpus, operation schedule, acknowledgement/durability, resources and result
oracles equivalent. Each database/topology/scenario has an isolated Linux runner.
Unsupported capabilities are unavailable cells, never fabricated numbers.

Keep named database job groups and original per-worker JSON. One authenticated
aggregation validates complete comparable source/run/attempt/topology evidence.
Only original GitHub Benchmarks artifacts can produce public figures. Website
publication is independent and can publish product content without figures when
no qualified measurements exist. Build and Tests, Benchmarks, Website and manual
Release retain their separate responsibilities and least-privilege permissions.

## Dependency ownership and delivery

Version increases require the owner's explicit permission for that specific increase. This includes packages, releases, grain interfaces, protocol versions, serializer aliases and persisted/public contracts. Cleanup, refactoring, contract changes and standing delivery authorization do not authorize a version increase.

`ManagedCode.*` projects and packages are ours. A confirmed defect is fixed in its
owning sibling repository after reading its policy. Do not replace the dependency,
copy its implementation or hide the defect with a consumer workaround.

Batch all confirmed defects in that repository, add focused regressions,
self-review, run its required checks, increment the canonical patch version once,
commit and push scoped changes, and follow the release workflow until successful.
Verify actual NuGet availability before updating each consumer once and running
its focused regressions. These scoped commits, pushes and releases are authorized.
An unpublished local package or project reference is not delivery. Report exact
infrastructure, credential or source blockers.

For confirmed ZoneTree, ZoneTree.FullTextSearch or ZoneTree.Analytics defects,
check for a matching upstream issue and publish bounded reproducible evidence to
the owning repository. Include exact version/source, environment, operation flow,
expected/actual results and impact; separate verified causes from hypotheses and
exclude secrets. This scoped publication is authorized and is not acceptance
proof or permission for an engine replacement or unpublished workaround.

Commit each completed implementation checkpoint on the current branch, stating
actual verification. When asked to commit all changes, include the complete
requested working-tree scope. Stable scoped KeyLoad commits and pushes to main
are authorized. Never force-push or bypass protections.

## Licensing and documentation

KeyLoad distributions use the unmodified Elastic License 2.0 and are described as
source available. Application use is permitted; third-party hosted/managed
services require ManagedCode authorization. Keep README, website and packages
consistent with the authoritative root `LICENSE`; preserve every dependency's
original license and notices and validate license text against its publisher.

Keep `docs/implementation/status.json` and README honest about actual source and
open qualification gates. Documentation presents the current design, valid
contracts and actual qualification limits. Readiness, demand, savings and
performance claims require their own evidence.
