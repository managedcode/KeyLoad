# GitHub Actions workflows

## Purpose, entry points and ownership

Owns the four delivery workflows: `build-and-tests.yml`, `benchmarks.yml`, `website.yml` and `release.yml`. Build and Tests owns functional qualification; Benchmarks owns isolated database measurements and authenticated aggregation; Website owns independent site qualification and Pages publication; Release owns explicitly authorized packaging and publication. Preserve pinned actions, source identities, original artifacts, needs-gated publication and least-privilege permissions.

## Boundaries and protected risks

Follow the [root policy](../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet build KeyLoad.slnx --no-restore -warnaserror
```

Use the root's complete solution, TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
