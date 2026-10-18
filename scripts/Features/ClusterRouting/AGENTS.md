# ClusterRouting scripts

## Purpose, entry points and ownership

This folder contains ClusterRouting-owned executable tooling when a current
feature contract assigns such tooling here. It does not own server behavior,
protocol compatibility or database data. Tooling is limited to the current-source
feature contract and existing homogeneous Aspire test topology.
Read the root policy, `scripts/AGENTS.md`, the owning ClusterRouting feature
specification and required ADR before adding a tool. Keep any executable artifact
within the canonical ClusterRouting feature folder and use the actual current
source, native APIs, AppHost-owned test entry and existing bounded artifact
helpers. Do not create alternate image registries, source overlays, package substitutes,
local data converters or standalone test runners.
Tools must validate bounded inputs and exact ownership, preserve original
failures with cleanup failures, settle all owned processes/readers before cleanup,
and fail closed on missing or ambiguous evidence. Never publish credentials,
user payloads or synthetic source/run/image/test identities. Node syntax checks
are not runtime proof; all required tests run through the actual Aspire entry,
and only exact-source Linux GitHub artifacts qualify delivery.
No executable tool currently has an independent entry point in this folder.

## Boundaries and protected risks

Follow the [root policy](../../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use native Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
node scripts/Features/RepositoryGovernance/verify.mjs
```

Use the root's complete solution, native TUnit, real process recovery and Aspire SDK/MCP gates as applicable. Local checks are development evidence; delivered-source qualification and public measurements require exact-source Linux GitHub artifacts. Do not weaken compiler, analyzer, source-identity, coverage or runtime assertions.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
