# ClusterRouting scripts

## Purpose and boundaries

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
