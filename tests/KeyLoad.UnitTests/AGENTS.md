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
- Read the [root policy](../../AGENTS.md), [architecture map](../../docs/Architecture.md), [RepositoryGovernance feature](../../docs/Features/RepositoryGovernance.md), and [ADR-032](../../docs/ADR/ADR-032-mcaf-governance.md) first.
- Owned test slices: `DocumentStorage`, `EventStreams`, `Messaging`, `GraphTraversal`, `TimeSeries`, `Search`, `QueryExecution`, `Authorization`, `ChangeFeeds`, `StorageRecovery`, `ClusterReplication`, `ClusterRouting`, `ClientApi`, and `BackupRestore`.
- Target test paths are `Features/<SliceName>/` for each named slice; `TestDatabase.cs` remains shared fixture infrastructure.
- Also owns `BenchmarkComparisons` pure corpus and correctness-contract checks under `Features/BenchmarkComparisons/`; read [its feature contract](../../docs/Features/BenchmarkComparisons.md) and [ADR-034](../../docs/ADR/ADR-034-cluster-comparisons.md). Execute those TUnit checks only in GitHub Actions; real engine, container and replicated topology proof belongs to the comparison integration suite.
- Also owns `ResourceExecution` common-budget and canonical-JSON acceptance cases under `Features/ResourceExecution/`; exact bytes, real-store replay and allocation assertions derive from ADR-035 and execute only in GitHub Actions.
- Also owns `Features/BlobStorage/` contract/golden and genuine ZoneTree lifecycle/range/quota/authorization tests under ADR-038/AC-BLOB-001–007. These do not substitute for real-process or Docker RF3 .NET/official MCP proof.
