# KeyLoad.AppHost

## Purpose, entry points and ownership

- Owns Aspire application composition for KeyLoad services and infrastructure resources.
- Entry point: `Program.cs`; benchmark resource composition: `BenchmarkComparisons/Resources/SessionTopology/BenchmarkResources.cs`.
- Keep host wiring and genuinely shared infrastructure composition here. Feature-specific deployment behavior belongs under `<SliceName>/` and the matching `docs/Features/<SliceName>.md`.
- Aspire owns resource orchestration, not database storage state. Each silo's ZoneTree owner (node-local `PartitionHost`) owns its storage, journals, file locks and the apply gate; Microsoft Orleans does all clustering and Orleans grains route commands (ADR-136).
- ClusterResources composes real Aspire container resources. Every configured silo owns ZoneTree storage; data copy count is independent of silo count. Native Orleans owns membership and communication. Do not retain a second peer protocol or coordination-only database nodes.
- Apply the installed Orleans and Aspire skills within their actual ownership boundaries.
- Owner correction2026-10-03 explicitly permits local development through the same Aspire-owned entry. Preserve secret boundaries, resource health dependencies, scoped fault injection, complete resource shutdown and the required Orleans cluster topology; local results do not qualify delivered-source Linux or public performance gates.

## Boundaries and protected risks

Follow the [root policy](../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use native Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
dotnet build KeyLoad.slnx --no-restore --configuration Release -warnaserror
```

Use the root's complete solution, native TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
