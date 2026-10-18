# ResourceExecution in KeyLoad.Clustering

## Purpose, entry points and ownership

- Own the bounded node-local resource admission, disposable cache, resource-retirement and owner-inventory execution accepted by ADR-058. Coordination between silos uses ordinary Orleans grain/Grain Service calls.
- Entry points: `Execution/NativeCommandMetadataExecution.cs` and `Queries/ResourceOwnerInventoryReadExecution.cs`; the exact types/aliases/Ids/APIs are in the ResourceExecution feature docs.
- Read the solution and Orleans project AGENTS.md, docs/Architecture.md, ResourceExecution feature and ADR-058 before changing this slice.
- Owner correction 2026-10-04 requires populated `Models/`, `Contracts/`, `Serialization/` and `Validation/` folders inside this slice. Keep messages in Models, marker interfaces/constants in Contracts, byte encoding in Serialization and shape checks in Validation. Do not recreate flat C# files or change namespaces, aliases/Ids or runtime behavior during moves.
- Root owns shared generated metadata, enums, marker interfaces, primitives, docs/config and final integration. Workers must stay within explicit disjoint file scopes and escalate ambiguity.
- Preserve node-local store ownership, connection-owned per-operation execution (ADR-125), current public authorization/read barriers and every existing database/replica/token/persisted byte contract.
- Never apply the separately held native internal-format migration as a prerequisite or workaround.
- Validate the complete closed shape and all lengths before allocation.
- Secrets and credentials never enter metadata, diagnostics or tests' output. Original concurrent tasks must finish or remain finitely observed before owners are released.
- A failed finite wait must retain a completion owner for unsettled originals; owned resources are released only after actual originals settle, with complete fault observation and no late mutation of reported failures.
- Inherit file400/type200/function50/nesting3 limits and all stricter root quality policies. No analyzer suppression or weakened assertions.
- Apply the already owner-authorized Orleans skill at /Users/ksemenenko/.codex/skills/orleans/SKILL.md. Install no other skills/tools or global configuration.
- Protect exact versioned aliases/Ids, flat-rejection shapes, immutable inputs and complete preflight.

## Boundaries and protected risks

Follow the [root policy](../../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use native Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet build KeyLoad.slnx --no-restore -warnaserror
```

Use the root's complete solution, native TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
