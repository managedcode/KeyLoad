# KeyLoad.Core

## Purpose, entry points and ownership

- Owns core database domain behavior and orchestration primitives.
- Current principal types include `DatabaseEngine`, `KeySpace`, document/event/messaging/graph models, admission governors, queues, subscriptions and projection outbox; see the named source files at this project root.
- New feature behavior belongs under `<SliceName>/` with the canonical `docs/Features/<SliceName>.md`; shared primitives outside a slice require genuine cross-feature use.
- Core logic must not take ownership of physical node storage. The silo-local ZoneTree owner (node-local `PartitionHost`) owns its storage, journals, file locks and the apply gate, and files never move with activations; Orleans grains route commands and Orleans does all clustering (ADR-136).
- Use the installed Orleans skill for native runtime design and serialization decisions.
- Transactions, command ordering, admission, projections and recovery contracts are high-risk. Runtime claims require the exact GitHub Actions run/SHA/artifacts; process-kill evidence does not establish power-loss durability.

## Boundaries and protected risks

Follow the [root policy](../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use native Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet build KeyLoad.slnx --no-restore -warnaserror
```

Use the root's complete solution, native TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
