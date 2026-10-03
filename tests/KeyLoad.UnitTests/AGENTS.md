# KeyLoad.UnitTests

## Purpose and entry points
- Owns focused TUnit tests for contracts and feature behavior, including queries, transactions, storage codecs, admission, change feeds, subscriptions, search, messaging and artifacts.
- Test sources include `QueryAdapterTests.cs`, `TransactionTests.cs`, `KeyCodecTests.cs`, `CommandAdmissionTests.cs`, `GraphAndSearchTests.cs`, `TestDatabase.cs` and the other feature-named test files in this project.

## Ownership and boundaries
- Organize feature-owned tests under the same canonical `Features/<SliceName>/` used by implementation and `docs/Features/<SliceName>.md`; shared test infrastructure may remain outside slices.
- Tests must derive from acceptance criteria and prove observable behavior. Do not add mocks, fakes or stubs, weaken assertions, or treat these unit tests as RF3, real-process recovery or production proof.
- TUnit with Microsoft.Testing.Platform is the target. Existing xUnit/VSTest references are migration debt, not permission to add another framework.

## Commands and evidence
- GitHub Actions invoking command: `dotnet test --project tests/KeyLoad.UnitTests --no-build --no-restore --configuration Release` in `.github/workflows/ci.yml`, following solution restore/build.
- Execute tests only in GitHub Actions. Preserve exact run/SHA/job links for qualification; no skipped suite counts as passing.

## Skills and protected risks
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Maintain meaningful success, negative, edge and failure assertions. Coverage qualification is not configured; numeric complexity rules are configured but their complete delivered-SHA gate remains pending. Neither gate may be claimed as passing without its authentic required evidence.

## Read-first and canonical slice ownership
- Owns `Features/RelationalStorage/` real-ZoneTree schema/row/constraint/atomicity/linkage regressions under ADR-055 and QueryExecution SQL compilation/equality regressions under ADR-054; authored cases require exact-SHA GitHub execution.
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned test slices: `DocumentStorage`, `EventStreams`, `Messaging`, `GraphTraversal`, `TimeSeries`, `Search`, `QueryExecution`, `Authorization`, `ChangeFeeds`, `StorageRecovery`, `ClusterReplication`, `ClusterRouting`, `ClientApi`, and `BackupRestore`.
- Target test paths are `Features/<SliceName>/` for each named slice; `TestDatabase.cs` remains shared fixture infrastructure.
- Also owns `BenchmarkComparisons` pure corpus and correctness-contract checks under `Features/BenchmarkComparisons/`; read [its feature contract](../../docs/Features/BenchmarkComparisons.md) and [ADR-034](../../docs/ADR/ADR-034-cluster-comparisons.md). Execute those TUnit checks only in GitHub Actions; real engine, container and replicated topology proof belongs to the comparison integration suite.
- Also owns `ResourceExecution` common-budget and canonical-JSON acceptance cases under `Features/ResourceExecution/`; exact bytes, real-store replay and allocation assertions derive from ADR-035 and execute only in GitHub Actions.
- ADR-058 extends that same ResourceExecution test ownership with actual cache-budget reservations and real ZoneTree point/cache/snapshot/pin/pressure cases. Derive tests from AC-CACHE criteria, use genuine files and bounded real synchronization, and retain native/cache/logical counter distinctions; no mock provider or local execution is qualification.
- Also owns `Features/BlobStorage/` contract/golden and genuine ZoneTree lifecycle/range/quota/authorization tests under ADR-038/AC-BLOB-001–007. These do not substitute for real-process or Docker RF3 .NET/official MCP proof.

## SG009P original child boundary
- Guarded original-store tests in Features/StorageRecovery/ MUST launch the actual
  Release KeyLoad.CrashHost existing-store-inspect helper for every guarded call,
  including invalid inputs. Preserve original files/data/failure assertions and
  hold the existing node.owner.lock until actual child exit and both readers
  settle; no fake parent exception, detached cleanup or direct parent guard open.
- Normal/scalar Actions reports and full recovery qualify this private test stage;
  they do not qualify actual benchmark node stop/restart, physical copies or ACKs.

## Owner-authorized local development verification, 2026-10-03
- The explicit owner correction in root AGENTS.md supersedes the historical GitHub-only execution restrictions above for development verification. Run the actual TUnit command locally against a freshly built source snapshot; retain actual machine, source, command and original results. Local results are development evidence. Exact-source GitHub recovery/RF3/endurance and publication gates remain required, with every necessary suite executed.
