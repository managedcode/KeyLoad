# KeyLoad.Diagnostics

## Purpose, entry points and ownership

- Owns generic bounded callback-free database phase diagnostics, under ADR-063 and its feature contract. Group implementation in populated role folders directly under this project.
- Entry points: immutable phase/outcome/quality/snapshot schema, DatabasePhaseBank and root-owned process startup facade.
- BCL-only shared implementation; no ASP.NET/Orleans/provider/product/package dependency except the existing centrally attached analyzer.
- Keep diagnostic implementation in its actual role folder; shared assembly friend metadata belongs to the project composition owner.
- Preserve both authorized cuts, node-local ZoneTree ownership, all existing gates/async originals/ACK/WAL. This project owns no database state, file, credential, role or protocol.
- Ten current phase boundaries retain their IDs within a fixed32-slot capacity, with6 outcomes/16 buckets/four stripes, startup bank128KiB, record maximum4 CAS attempts and sticky degradation; undefined phase slots are invalid inputs. No hot callbacks, logging/export, allocations, gates, ambient state, per-request identifiers, reset or live mode changes.
- Only root freezes public shared signatures/project references and mode/export composition; workers may not invent broader telemetry/control architecture.
- No project-local skill installed; do not install tools/skills or modify global configuration. Installed authorized Orleans guidance applies to unchanged producer lifecycles outside this BCL project.
- Unknown/partial/degraded measurements are unavailable evidence, never zero or a performance winner. Snapshot reads are non-atomic; overlapping phases and whole-process samples are not request attribution.
- Preserve root400/type200/function50/nesting3 limits, compiler/style diagnostics and all independent ownership. Stop on package/public/wire/security change or scope conflict.

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
