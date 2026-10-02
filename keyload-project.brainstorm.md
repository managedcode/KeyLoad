# KeyLoad project delivery brainstorm

## Problem framing

The owner asks for a complete implementation of KeyLoad against the attached architecture v0.3 and its 104-task backlog. The existing checkout already contains a large, unfinished project-wide change set and parallel implementation packets. The immediate job is to integrate that work safely, close real build defects, and carry the full delivery through the repository's GitHub qualification and release rules.

The attached Markdown matches `docs/design/architecture-v0.3.uk.md` byte-for-byte. The HTML is the rendered 46-section edition of that same specification. The repository's single implementation ledger is `docs/implementation/status.json`; the 104-task mapping is `docs/implementation/documentation-coverage.json`.

## Scope

In scope: the complete v0.3 database and eventing product, .NET 10/C# 14, centrally pinned packages without lock files, ZoneTree storage, Orleans as cluster/runtime foundation, RF3 Docker/Aspire, persisted credentials and authorization, the .NET SDK, official MCP C# SDK server/client coverage, chunked public blob storage and partial reads, agent API, TUnit suites, docs/site/benchmarks, GitHub qualification, and the authorized stable delivery to `main`.

Out of scope: changing the product contract to a demo or single node; replacing ManagedCode packages to conceal defects; weakening security, durability, or acceptance claims; and claiming production readiness without the specified fault/endurance gates.

## Options and decision

1. Replace the current tree wholesale. This discards substantial in-progress work, loses its acceptance history, and makes it easy to violate the node-local storage and RF3 contracts.
2. Integrate the existing work as ordered feature slices, repair build and contract joins, and qualify each delivered capability on GitHub. This preserves the accepted architectural decisions and gives each public claim exact evidence.

Choose option 2. Keep the checkout's existing changes and inspect each join before integration. Keep one owner for shared contracts, composition roots, central package configuration, and status evidence.

## Boundaries and risks

- An atomic partition identifies transaction scope; physical placement is a separate mapping. Node-local `PartitionHost` owns ZoneTree files, journals, locks, and the apply gate. Orleans grains route commands and may migrate independently of those resources.
- Each request gets a separate Orleans request grain. The cluster enables the distributed grain directory and activation repartitioning/migration. DotNext cluster packages and runtime are prohibited.
- Orleans calls can time out after side effects; command IDs, persisted receipts, and explicit retry outcomes remain required.
- ManagedCode packages are owned dependencies. Any defect is repaired and released in its sibling source repository before KeyLoad changes its package pin.
- Unit, recovery, RF3, comparison, and site test qualification is GitHub Actions only. Current GitHub authentication is invalid and network access from this checkout has failed; this is an external delivery risk, not a reason to report local build output as CI evidence.
- Process termination proves process-recovery behavior only. Endurance, power-loss, total-reset, and independent failure-domain gates remain separate.

## Current baseline

- Checkout starts at `9c570f8c3` on `main`, tracking local `github/main`; 259 paths are already modified, deleted, or untracked. Preserve them while tracing ownership.
- The first Release build completed with 97 compiler/analyzer errors, mostly in the newly integrated benchmark comparison tests, plus two CrashHost localization diagnostics, three BlobStorage test namespace errors, and two RF3 fixture disposal errors.
- No tests have been run locally. The baseline TUnit/recovery/Aspire RF3 run is pending the authorized GitHub Actions workflow.
- `gh auth status` reports an invalid GitHub token; the GitHub API and SSH hostname could not be reached from this environment.
