# Scripts module

## Purpose, entry points and ownership

Owns feature-local repository tooling. Entry points include `Features/RepositoryGovernance/verify.mjs`, the native TUnit argument selector and the source-bound coverage, benchmark and release helpers. Tooling does not execute a substitute database runtime. Inventory checks derive projects from the current solution and never follow symbolic links outside the repository.

## Boundaries and protected risks

Follow the [root policy](../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use native Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
node scripts/Features/RepositoryGovernance/verify.mjs
```

Use the root's complete solution, native TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
