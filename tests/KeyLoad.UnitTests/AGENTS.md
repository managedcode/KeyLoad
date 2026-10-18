# KeyLoad.UnitTests

## Purpose, entry points and ownership

- Owns focused TUnit tests for contracts and feature behavior, including queries, transactions, storage codecs, admission, change feeds, subscriptions, search, messaging and artifacts.
- Test sources include `QueryExecution/Cases/QueryAdapter/QueryAdapterTests.cs`, `ResourceExecution/Cases/Transaction/TransactionTests.cs`, `StorageRecovery/Cases/KeyCodec/KeyCodecTests.cs`, `ResourceExecution/Cases/Command/CommandAdmissionGovernorTests.cs`, `GraphTraversal/Cases/Graph/GraphTraversalTests.cs` and `Search/Cases/SearchResource/SearchResourceTests.cs`, `TestDatabase.cs` and the other feature-named test files in this project.
- Organize feature-owned tests under the same canonical `<SliceName>/` used by implementation and `docs/Features/<SliceName>.md`; shared test infrastructure may remain outside slices.
- Tests must derive from acceptance criteria and prove observable behavior. Do not add mocks, fakes or stubs, weaken assertions, or treat these unit tests as cluster, real-process recovery or production proof.
- Applicable skills: none installed; skill installation is prohibited by owner direction.
- Maintain meaningful success, negative, edge and failure assertions. Coverage qualification is not configured; numeric complexity rules are configured but their complete delivered-SHA gate remains pending. Neither gate may be claimed as passing without its authentic required evidence.

## Boundaries and protected risks

Follow the [root policy](../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use native Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet test --project tests/KeyLoad.UnitTests/KeyLoad.UnitTests.csproj --no-build --no-restore --configuration Release --output Detailed
```

Use the root's complete solution, native TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
