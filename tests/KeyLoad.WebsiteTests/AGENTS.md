# KeyLoad.WebsiteTests

## Purpose, entry points and ownership

- Owns the developer checks of the benchmark results website. Entry: KeyLoad.WebsiteTests.csproj; implementation uses populated role folders directly under this project. This suite never runs from or blocks the Website/Pages workflow. The landing page has no dependency on .NET, Aspire, AppHost or product qualification. Read root/site policy before implementation.
- TUnit/Microsoft.Testing.Platform, net10.0, centrally pinned packages and all central analyzers are mandatory. No Core/AppHost dependency, alternate runner, mocks/fakes/stubs or browser automation package.
- Cases run the real `site/scripts/results.mjs` merge and `site/scripts/build.mjs` builder as Node child processes and read the merged file and the rendered HTML. C# states the expected types, cases, databases and values independently of the JavaScript contract.
- `Fixtures/Results/<artifact>/result.json` holds the test result files, laid out like downloaded Benchmarks job artifacts. They are test data and local preview input only and are never published as measurements.
- Root owns this policy, project, solution, workflow, shared contracts and governance inventory.

## Boundaries and protected risks

Follow the [root policy](../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Each case owns a unique directory below the test output and removes it after its Node processes have exited; caller cancellation stops and joins the child process tree. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet build tests/KeyLoad.WebsiteTests/KeyLoad.WebsiteTests.csproj --no-restore -warnaserror
dotnet test --project tests/KeyLoad.WebsiteTests/KeyLoad.WebsiteTests.csproj --no-build --no-restore --output Detailed
```

Node must be on the path. Local checks are development evidence; public measurements require the original Benchmarks result files. Do not weaken compiler, analyzer or runtime assertions.

## Skills

Use the relevant installed formatting or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
