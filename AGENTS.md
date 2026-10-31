# KeyLoad

## Rule 0: Question every action

Before starting and continuously throughout the work, ask yourself:
**"Am I doing bullshit here?"**

Verify the real cause, necessity, scope and ownership before each change. Use the
existing standard framework path. Do not add infrastructure, configuration, locks
or workarounds to compensate for an unverified diagnosis. Existing code is not
proof that its design is correct. When evidence does not justify an approach,
stop that approach and remove your own speculative additions. Apply this rule
immediately, including to your previous changes, while preserving unrelated work
and required correctness gates.

KeyLoad is under development. Replace superseded implementations completely when
adopting an approved new design. Remove their exclusive helpers, contracts, tests
and documentation together. Keep one current implementation; do not preserve old
formats, backward compatibility routes or conversion mechanisms. The native
ZoneTree MVCC storage adoption uses the current data epoch across storage and
internal request purposes.

## Product and runtime

KeyLoad is one database for AI agents. Documents, typed tables, graphs,
vectors/search, files/blobs, queues, events and time series share canonical entity
references and can participate in the same authorized operation. SQL is the
shared versioned language across these models. The product specification is
`docs/design/product-specification.md`; `docs/Architecture.md` maps the current
runtime. User instructions control scope and authorization.

Microsoft Orleans owns the cluster: membership, discovery, failure detection,
distributed grain directory, placement, activation movement, scheduling, internal
communication and scaling. Enable the distributed grain directory and
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
files, WAL, locks and ordered apply gate. Local code borrows storage through
DI; activations never transfer file handles. Copies use typed Orleans Grain Service
calls to target silos found through Orleans membership. `KeyLoad:SiloCount`
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
holds a bounded buffer and flushes on a grain timer with a typed interval
option. No leadership check gates this
work. Durable delivery and idempotent effects are separate from an in-memory
buffer. Grain Services own per-silo resources; workers own activation-local work.
Choose timers, reminders, Streams, Journaling and Durable Jobs by their
actual lifetime and delivery guarantees, with bounded cancellation and shutdown.

Every addressing identifier is created by one shared helper per identity kind.
Grain and storage keys are case-sensitive; callers must not construct them by
ad-hoc concatenation. Do not change user entity casing implicitly.

## Data, authorization and integrations

- ZoneTree is the canonical storage engine for database records and file metadata.
  File and blob payload bytes live in physical storage managed by
  ManagedCode.Storage, outside ZoneTree. ZoneTree stores their metadata and
  physical-storage references. Use ZoneTree APIs, bounded replay and
  ordered atomic commit/apply for database state. Keep atomic partition identity
  separate from Orleans activation placement.
- Use ZoneTree.FullTextSearch for full-text indexing and ZoneTree.Analytics for
  approved analytics integration. Derived indexes, caches and analytics never
  become a second authority for database records or permissions.
- Use ZoneTree, ZoneTree.FullTextSearch and ZoneTree.Analytics through their
  public APIs. Await their asynchronous APIs, such as `CommitDurableAsync` and
  `SyncWALAsync`, so engine waits never block a grain or thread-pool thread.
  Blocking on their tasks (`.Result`, `.Wait()`, `.GetAwaiter().GetResult()`) and
  dispatching engine work through `Task.Run`, `Task.Factory.StartNew` or a
  thread-pool callback are compiler errors (`KLD0039`). The rule makes no claim
  about a dependency's internal scheduling.
- Use generated Orleans binary serialization with explicit stable
  `[GenerateSerializer]`, `[Id]` and `[Alias]` contracts internally. Preserve raw
  ZoneTree bytes, sortable keys, public JSON and exact user content. Introduce no
  runtime serializer fallback or persisted-data migration.
- SQL syntax uses Microsoft.SqlServer.TransactSql.ScriptDom. Compile supported
  syntax into the same bounded authorized operations as SDK/MCP. Reject valid
  unsupported ASTs explicitly before effects. Parsing, execution semantics and
  SQL-client interoperability are separate qualification gates.
- ASP.NET Core Authentication and Authorization admit HTTP/SDK/MCP callers.
  Tickets carry the verified subject only. Every operation checks fresh persisted
  credentials and permissions. ManagedCode.Orleans.Identity propagates verified
  call-local context and restores it after asynchronous work.
- Use ManagedCode.Communication commands, results and CQRS response streams.
  Reuse its producer, transport, cancellation and settlement APIs. KeyLoad owns
  authorization, operation identity, payload validation and resource bounds.
- Use the official MCP C# SDK and ManagedCode.MCPGateway for on-demand tool
  discovery. ManagedCode.MarkdownLd.Kb supplies approved graph-based tool search.
  Discovery respects fresh authorization and excludes secrets and user payloads.
  Supply bounded static MCP guidance through the same server.
- Blob API contracts and server-side upload/download implementations use
  ManagedCode.Storage. Reuse its storage interfaces and provider implementations,
  including S3. Do not add a KeyLoad IStorage adapter, custom multipart engine,
  transfer stream or parallel storage implementation when the library already
  supplies that behavior. KeyLoad exposes the ManagedCode.Storage file-storage contract
  (`IStorage`, or its VFS surface where applicable); this is a public KeyLoad
  capability, not merely a client for an external bucket. ManagedCode.Storage
  owns physical payload access through the selected provider; ZoneTree owns
  metadata and physical-storage references, never the binary payload or its
  parts. Remove superseded KeyLoad transport code instead of maintaining a
  parallel Blob API or writing a separate S3 implementation. Preserve fresh
  authorization and explicit publication, cleanup and recovery contracts across
  the metadata and physical-storage boundary; a metadata commit alone does not
  prove file persistence or replication.
- Keep chunked blobs with partial reads, same-partition atomic operations,
  explicit cross-partition contracts, persisted command outcomes and honest
  acknowledgement/read-cut semantics.
- RAM and Orleans caches are bounded and disposable. Tie cached results to the
  authorized read cut and policy epoch; invalidate after writes and policy
  changes. Restart, migration and failover cannot turn RAM into storage authority.
- Use existing Orleans, ASP.NET Core, ServiceDefaults and Aspire
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
- Keep namespaces, contract IDs and live project/build/source references
  coherent when moving files. Remove replaced implementations and their exclusive
  helpers and tests together. Add no compatibility shim.

Maintain one current implementation and contract per capability. Do not retain version-suffixed types, parallel older routes or version-selecting execution paths.

## MCAF-GOV-001: Current governance

Root and local policies describe current rules only. Keep one canonical source
for each fact. Repository text, tests, schemas, tooling, README and the website
describe only the current approved design and its actual contracts. Keep references
valid and remove inactive implementations and their exclusive supporting files.

## MCAF-ARCH-001: Cohesive implementation

Inspect current registrations and callers before adding or removing infrastructure.
Reuse the selected Microsoft, Orleans, ZoneTree and ManagedCode APIs. New
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
the current design stay explicit: Orleans activation migration, real
TUnit fixtures owned under `tests/.../Fixtures/` and source-resolved test contracts,
metadata and support reachable from real TUnit cases or lifecycle interfaces;
real process-recovery fixtures reached from the compiled `KeyLoad.ProcessRecoveryTests` entry point;
Aspire resource fixtures reached from the `KeyLoad.AppHost` entry point's resolved execution
`DistributedApplication.CreateBuilder` composition and its builder-typed composition callbacks;
BenchmarkDotNet fixtures rooted in its benchmark, setup and cleanup attributes
and their source-resolved measurement support under `benchmarks/`;
time-series sample data, original operation/resource measurement samples and their
authentic report fields, and resolved framework API names. These are term-specific semantic contexts,
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
Local development, normal Aspire startup and local functional tests use the SDK's
default Debug configuration. Do not force Release or build both configurations
for development checks. Functional GitHub Actions qualification builds and tests Release explicitly.
Keep compiled source identities, process launches, test artifact paths and framework
IPC aligned with the selected configuration; never qualify a different build's DLLs.

All test-owned temporary files, directories, storage and logs live below the
executing test assembly's `AppContext.BaseDirectory`, beside its binaries in
`bin/Debug` or `bin/Release` for the selected configuration. Allocate a unique
directory per execution and join all owned work before cleanup. Retain required
failure evidence in that same output tree. Never use system temporary locations
such as `/tmp`, `/var`, `Path.GetTempPath()`, `Path.GetTempFileName()` or
`Directory.CreateTempSubdirectory()`, or create scratch files in source folders.
Child processes receive their test-owned paths explicitly.
Framework IPC endpoints use `tests/bin/<configuration>/ipc`, keeping their
configured Unix socket paths within the framework's length bound. The framework
owns unique socket names and joined cleanup; never remove another run's files.

Required development commands:

```sh
dotnet restore KeyLoad.slnx
dotnet format KeyLoad.slnx --no-restore
dotnet format KeyLoad.slnx --no-restore --verify-no-changes
dotnet build KeyLoad.slnx --no-restore -warnaserror
node scripts/Features/RepositoryGovernance/verify.mjs
```

Use TUnit/Microsoft.Testing.Platform entry after build, with Detailed output.
C# fixtures own Aspire startup, readiness, discovered endpoints, real SDK and
MCP clients, fault injection and joined cleanup. AppHost owns all container
lifecycle and resource commands; tests never start independent containers or
substitute graceful shutdown for an abrupt fault. Unit and process tests use
only the infrastructure they actually require. `KeyLoad.AppHost` starts execution
KeyLoad services through `AddProject` with the published RapidMembership Orleans
provider for development and functional tests; membership is in memory with
auto discovery and an independent shared secret, without external storage. `KeyLoad.LoadTests.AppHost` is separate: it starts only the
selected database infrastructure, waits for readiness and exposes endpoints;
client code owns load execution and measurements. Each AppHost removes its own
session containers and volumes on joined teardown after success, failure or
cancellation. Preserve unrelated Docker resources. Normal builds and functional
tests never publish or build a KeyLoad server image. The manual Release workflow
publishes the server image with execution .NET SDK `PublishContainer` to
`ghcr.io/managedcode/keyload` under the commit SHA and `latest` tags; maintain no
product Dockerfile. Benchmarks resolves the latest released image to one digest per
run and runs the KeyLoad cells as Aspire containers from that digest.

Complete solution build, unit tests, real process recovery, actual Aspire cluster
SDK/MCP operations, coverage and fault/endurance checks remain distinct mandatory
gates. Run focused tests during implementation and report their exact scope.
Delivered-source qualification and benchmarks use Linux GitHub Actions and
original artifacts. `gh workflow run build-and-tests.yml` dispatches functional
qualification. Process-kill recovery does not prove power-loss durability.

Preserve `.editorconfig`, Allman multiline executable blocks and numeric gates:
64 code lines per method, 200 per type, 400 per file, nesting depth 3, and the
configured coverage thresholds (80 line / 70 branch, 90 for critical modules).
Keep generated-code/source identities, reports and executable source
inventories truthful; do not exclude product code to improve coverage.

## Performance and comparisons

Benchmarks (`benchmarks.yml`) runs one Linux GitHub job per database, data type
and node count ([BenchmarkComparisons](docs/Features/BenchmarkComparisons.md),
ADR-150). A small first job resolves the latest public released KeyLoad server
image to one immutable digest. Each measure job builds
`benchmarks/KeyLoad.ComparisonHost`, whose TUnit fixture deploys the one selected
database through `KeyLoad.LoadTests.AppHost`, runs the cases of its type in order
and rewrites `results/<database-slug>-<type>-n<nodes>.json` after every case; the
job uploads that file whether it passed or failed. Benchmarks never builds or
publishes a product image and never invokes Release or Build and Tests. Its final
job dispatches Website with the run id.

Each data type has one shared module, `benchmarks/KeyLoad.ComparisonHost/<Type>/`
with `Dataset/`, `Scenario/`, `Cases/` and `Drivers/`: the standard dataset, the
cases, schedule and verification, and the driver interface exist once, and each
database, KeyLoad included, implements only its driver. Change a dataset or
scenario once, in its module. KeyLoad runs every data type. Every database uses
seed 1729, 16 clients, 1,000,000-record datasets and 100,000 measured operations
per case, at one node and, where the database supports it, three nodes.
Unsupported capabilities and node counts have no cell; a failed case keeps its
real error; never estimate, zero or invent a number.

Add no evidence machinery to benchmarks: no runtime bundles, hash or identity
proofs, resource observation, receipts or aggregation. Ask the owner for concrete
approval before changing the benchmark workflow layout, data types, cases,
datasets, schedules, counts, the result file contract or publication rules. A
request to add databases authorizes their drivers and matrix rows within the
existing types.

Use execution .NET SIMD/intrinsics in normal supported execution. Validate required
hardware; scalar arithmetic is limited to tails and independent correctness
oracles. Do not add a product SIMD-disable mode.

Use `dotnet-diagnostics-mcp` and the installed profiling skill on actual processes
before selecting optimizations. Retain source/DLL/PDB/process, workload and
capture identities, overhead and matched before/after evidence. A trace alone
proves no performance improvement.

### Static landing-page boundary

Website (`website.yml`) owns only the static landing page. It downloads the
`benchmark-*` artifacts of the dispatched Benchmarks run, or of the latest
completed run for a site push or manual dispatch, merges them with
`node site/scripts/results.mjs`, builds the static site with
`node site/scripts/build.mjs` and deploys GitHub Pages. The page shows one
comparison table per data type with KeyLoad in every type; failed cases read as
failed with their error and missing results as no data. Without result files the
merge fails and the current Pages stay. Only GitHub Benchmarks artifacts produce
public figures; local results never do.

The landing page has no runtime or qualification dependency on KeyLoad product
code, .NET, Aspire or AppHost. Never restore/build the solution, start containers,
run TUnit/browser suites, analyzers, coverage, product checks or Release from
Website, and never make Pages publication wait for those jobs. Product-code and
test-infrastructure changes must not trigger Website. Adding any such coupling
requires the owner's explicit approval for that specific change. Build and Tests,
Benchmarks, Website and manual Release retain their separate responsibilities and
least-privilege permissions.

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
search the owning repository's open issues and read relevant discussions before
publishing. If a matching issue already exists, report its link and current status
to the user; do not create a duplicate or add comments, repeated evidence or status
requests without the user's explicit instruction. Respect maintainers' requests
to avoid further comments while they are working on the issue. Only when no
matching open issue exists, publish bounded reproducible evidence in a new issue.
Include exact version/source, environment, operation flow, expected/actual results
and impact; separate verified causes from hypotheses and exclude secrets. This
scoped new-issue publication is authorized and is not acceptance proof or permission
for an engine replacement or unpublished workaround.

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
