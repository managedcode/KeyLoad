# SessionContracts

## Purpose, entry points and ownership

Owns the shared native benchmark preparation checks in `action.yml`. Read the root and workflow policies and ADR-142. Execute only inside Linux Benchmarks; preserve original image, topology, readiness, failure, cancellation and workload assertions. This composite never produces website measurements. Root integrates shared workflow changes.
Every filter must name retained TUnit test classes. The comparison entry requires at least one discovered test; an empty filter is a failure, never a skipped qualification. Shared-session process admission is exercised by `ComparisonHostStartupTests`, `ComparisonHostCleanupTests` and `BenchmarkSessionNativeParserTests`. `BenchmarkSessionProgressTests` uses an explicitly controlled development target to check acknowledgement, cancellation and failed-oracle accounting; it is not native database performance evidence. `DocumentComparisonNativeTests` retains real SurrealDB/HelixDB shared-session correctness and cancellation against small functional corpora.
The native resource-model group retains all current database topology, credential, private-volume and foreign-selection checks. Native shutdown tests retain original primary/cleanup failures and joined resource/log ownership. Keep MongoDB readiness, pinned-image inspection and KeyLoad resource/admission gates independent.

## Boundaries and protected risks

Follow the [root policy](../../../../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use native Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet build KeyLoad.slnx --no-restore -warnaserror
```

Use the root's complete solution, native TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
