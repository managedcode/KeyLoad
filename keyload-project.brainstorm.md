# KeyLoad project delivery brainstorm

## Problem framing

The owner asks for a complete implementation of KeyLoad against the attached architecture v0.3 and its 104-task backlog. The existing checkout already contains a large, unfinished project-wide change set and parallel implementation packets. The immediate job is to integrate that work safely, close real build defects, and carry the full delivery through the repository's GitHub qualification and release rules.

The attached Markdown matches `docs/design/architecture-v0.3.uk.md` byte-for-byte. The HTML is the rendered 46-section edition of that same specification. The repository's single implementation ledger is `docs/implementation/status.json`; the 104-task mapping is `docs/implementation/documentation-coverage.json`.

## Scope

In scope: the complete v0.3 database and eventing product, .NET 10/C# 14, centrally pinned packages without lock files, ZoneTree storage, Orleans as cluster/runtime foundation, RF3 Docker/Aspire, persisted credentials and authorization, the .NET SDK, official MCP C# SDK server/client coverage, chunked public blob storage and partial reads, agent API, TUnit suites, docs/site/benchmarks, GitHub qualification, and the authorized stable delivery to `main`.

Out of scope: changing the product contract to a demo or single node; replacing ManagedCode packages to conceal defects; weakening security, durability, or acceptance claims; and claiming production readiness without the specified fault/endurance gates.

## Options and decision

### Runtime checkpoint ad594642 diagnostic refinement

Run37015193756 leaves four distinct unit failures, seventeen official MCP
initialization failures and one retained-replica failure. The latest RF3 log
overwrites earlier failures; the visible legacy initialize rejection is a
fallback after an unobserved discovery failure. Widening accepted protocol
versions would hide that missing first cause. Choose closed failure-stage and
method-category diagnostics while preserving every guard and public error.
Keep each bounded RF3 failure receipt, then qualify the actual initial discovery
and replica failure on GitHub before accepting a behavioral repair.

Aspire13.6.0's executable watch awaits the Kubernetes watch response under a
one-minute Polly timeout. Upstream f48a7b1251d339b21856497f10b36d227a5e57cc fixes
that retry behavior but no compatible patch is published. The native runner's
exit state must remain a required gate. Preserve completed report bytes even
when that gate fails; neither report existence nor successful engine samples
substitute for the native zero-exit assertion. Keep recovery execution independent
of unit failure after a successful build, with both failures retained in CI.

Root owns all shared contracts/docs/workflow joins and bounded RF3/report
retention. A disjoint worker owns only MCP guard stage metadata, its closed
diagnostic formatter, and meaningful real-provider privacy regressions. The
existing failed exact-SHA suite is the baseline; every repair remains pending
until full exact-SHA GitHub qualification.

1. Replace the current tree wholesale. This discards substantial in-progress work, loses its acceptance history, and makes it easy to violate the node-local storage and RF3 contracts.
2. Integrate the existing work as ordered feature slices, repair build and contract joins, and qualify each delivered capability on GitHub. This preserves the accepted architectural decisions and gives each public claim exact evidence.

Choose option 2. Keep the checkout's existing changes and inspect each join before integration. Keep one owner for shared contracts, composition roots, central package configuration, and status evidence.

## Boundaries and risks

- An atomic partition identifies transaction scope; physical placement is a separate mapping. Node-local `PartitionHost` owns ZoneTree files, journals, locks, and the apply gate. Orleans grains route commands and may migrate independently of those resources.
- Each request gets a separate Orleans request grain. The cluster enables the distributed grain directory and activation repartitioning/migration. DotNext cluster packages and runtime are prohibited.
- Orleans calls can time out after side effects; command IDs, persisted receipts, and explicit retry outcomes remain required.
- ManagedCode packages are owned dependencies. Any defect is repaired and released in its sibling source repository before KeyLoad changes its package pin.
- Unit, recovery, RF3, comparison, and site test qualification is GitHub Actions only. The initial credential/network probe failed, but later approved GitHub access produced exact run 36988949282 without changing credentials; verify access and evidence again for each delivery operation.
- Process termination proves process-recovery behavior only. Endurance, power-loss, total-reset, and independent failure-domain gates remain separate.

## Initial baseline before first delivery

- Checkout starts at `9c570f8c3` on `main`, tracking local `github/main`; 259 paths are already modified, deleted, or untracked. Preserve them while tracing ownership.
- The first Release build completed with 97 compiler/analyzer errors, mostly in the newly integrated benchmark comparison tests, plus two CrashHost localization diagnostics, three BlobStorage test namespace errors, and two RF3 fixture disposal errors.
- No tests have been run locally. The baseline TUnit/recovery/Aspire RF3 run is pending the authorized GitHub Actions workflow.
- The initial `gh auth status`/GitHub API probe reported an invalid token and unavailable network. Subsequent authorized GitHub access succeeded without changing credentials; retain this as an initial environment observation, not current remote status.

## Integrated delivery and priority update, 2026-10-02

- First stable foundation commit `3559225a5f918160e46e32c9a812c3f71790e382` is pushed to protected `main`. Its exact GitHub Actions run `36988949282` failed: analyzer 84/88, comparisons 2/4, RF3 3/23, and governance on all three operating systems; unit/recovery suites did not run because governance stopped their jobs. See the root project plan and implementation status for the complete failure ledger.
- Keep the repair set coherent before the next protected-main push: Timescale resource-name/digest assertions, RF3 failure diagnostics and cluster cleanup, analyzer/source-span cases, site-builder portability, full site evidence provenance, and the missing GitHub workflow qualification. Development build/format/governance remain separate from runtime qualification.
- The owner now requires whole-system operation efficiency and RF3 scalability, not one isolated fast path. Prefer explicit per-operation resource/fault budgets, matched workloads and honest server-versus-client measurements. The updated [AC-010](keyload-project.acceptance.md#ac-010--system-wide-operation-efficiency-and-cluster-scalability) traces this to memory-performance gates.
- The owner-designated SMID work is a highest-priority product workstream, but `SMID` is absent from both repository and v0.3 specification. The exact meaning must be clarified and mapped to a canonical slice before implementation; the unresolved requirement is AC-011. No acronym expansion is inferred.

## Exact candidate runtime repair, 2026-10-02

Full canonical run `37005805424` at candidate `6949fa0c3099443c6f34f91245ab8064ef22c63c` passed the matrix builds, format, governance and 88 analyzer tests, then failed unit, RF3 and comparison qualification. Recovery did not execute. The null-tombstone projection, stream fixture command IDs and official MCP negotiation repairs are source-complete and require a new exact-SHA run.

Investigate the remaining independent families concurrently before authorizing writes: analytical admission and blob row authority; strict JSON/queue protocol validation; and comparison resource image/dependency orchestration. The lead owns client error decoding, evidence retention, failure inventory, shared contracts and integration. Preserve every acceptance assertion, the accepted Timescale digest and RF3 topology. Do not add timeout extensions, reader-side empty-value workarounds, protocol downgrades, or unmeasured performance claims. Portable SIMD validation remains staged behind real baseline and integrated caller correctness evidence.
