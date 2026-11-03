# Full-text construction measurements

Local evidence from 2026-11-03: macOS ARM64, .NET 10.0.14, Debug,
ZoneTree 2.1.1 and ZoneTree.FullTextSearch 1.1.0. These are native-provider
diagnostics, not public comparison-benchmark results.

The harness calls the actual `RuntimeTextProjection`, uses the canonical search
dataset with seed 1729 and twelve words per document, and retains the normal
three-root `TextResourceOwnership` and unchanged execution settings. It verifies
the actual projection assembly location, MVID and DLL hash. The measurement
loop and independent posting oracle are unchanged between targets; diagnostic
constructor binding adapts the retained baseline outside that loop.

## Cause and correction

The baseline used `Sync` WAL for disposable bootstrap postings. Each WAL stream
write admitted and settled a physical reservation; both paths recursively
censused the registered roots. A bounded allocation profile attributed most
observed allocation ticks to that KeyLoad accounting path, with path strings
and `FileInfo` dominating. This does not establish an engine defect or an
`fsync` per posting.

Fresh bootstrap generations now use native `None`, with eviction, joined close,
complete owned-file inventory and manifest publication before reuse. Sealed
bootstrap readers retain that mode. Canonical storage durability and native
`Sync` for incremental/online owners remain separate contracts.

Scale testing exposed a real failure before completion: a native segment was
deleted between the old direct census enumeration and `GetAttributes`.
Periodic checks now use the existing resource-accounting gate shared by native
file mutations. Every bootstrap composition supplies a real owner; the
resource-less route and direct census implementation were removed. Path-ledger
ordering, directory creation and actual local handle close use that ownership
protocol. Writes join before close enters the gate; failed closes retain their
ledger charge. Native-owned empty directories are accepted while exact file
sets, receipt ownership, link checks and resource limits remain enforced.

The code owners are [RuntimeTextIndex](../../src/KeyLoad/Search/Storage/FullText/Reads/RuntimeTextIndex.cs),
[RuntimeTextProjectionLifecycle](../../src/KeyLoad/Search/Storage/FullText/Projections/RuntimeTextProjectionLifecycle.cs)
and [TextOwnedFileMutation](../../src/KeyLoad/Search/Lifecycle/FullText/TextOwnedFileMutation.cs).

## Clean fixed-work observations

No profiler was attached to these runs. Counters wrap the original registered
verifiers after initialization, invoke them unchanged, and cover all three
roots. The wrapper adds a delegate invocation and atomic increment per counted
census. Counts are root-census calls, not OS syscalls. Allocations are cumulative
managed bytes across threads through publication and joined owner close, not
retained RAM; independent full-posting verification follows that interval.

| Work | Baseline | Current |
|---|---:|---:|
| Documents | 2,000 | 2,000 |
| Allocated bytes | 1,217,010,256 | 68,548,760 |
| Root censuses | 145,893 | 2,463 |
| Verified persisted postings | 24,000 | 24,000 |

The observed fixed-work counts are 17.75 times fewer allocated bytes and 59.23
times fewer root censuses. The earlier intermediate implementation's figures
do not describe the current accounting path.

The current 100,000-document run completed publication and joined close in
22.7308945 seconds within its original 240-second budget, allocating
6,631,639,400 managed bytes and counting 85,224 root censuses. After a sealed
cold open, an independent SHA-256 oracle verified every `(token, record,
previous-token)` key: 1,199,995 unique postings, no missing or additional keys.
The five duplicate composite keys are expected from the 1,200,000 token inputs.
The actual lease also verified canonical expected `w000` positives at its
declared source cut before publication.

## Identities and limits

Baseline DLL MVID: `47c67e65-ca06-4fc4-84a4-464b72239952`;
SHA-256: `2bcf50c7869297faeb548f1a3707722d4a1db23b58d3fe1d3a4101162c743d0c`.
Current DLL MVID: `a1390f03-84e5-42e8-94c6-15cc65384593`;
SHA-256: `e2dbf9db316e77c7c573fb520886fea2850b6debb48fa58d585d47b7068fb7df`.

Ignored local evidence lives under
`tests/KeyLoad.FunctionalTests/bin/Debug/net10.0/performance-investigation/search-native-20261103/`:
`final-measurements.json`, `after-race-identities.json`, retained baseline/current
DLLs and PDBs in `baseline-producer/` and `after-race-producer/`, harness sources
in `after-race-harness/`, clean-run JSON lines and separate allocation
captures. Raw diagnostic output is not published here.

A separate two-second current-process allocation capture observed native disk
segment append and compression/decompression buffers as its largest allocation
sites. It completed the same full posting verification. Allocation ticks are
sampled and weighted; capture-loss quality was not supplied, so it remains
unknown. The MCP CPU sampler misclassified these CoreCLR DLL processes as
NativeAOT and supplied no usable CPU profile.

Concurrent qualification and native scheduling variation prevent a clean-machine
wall-time speedup claim. These results qualify this local construction workload,
not the full authorized query path, power-loss durability, Linux delivery or
public database competitiveness. Exact-source Linux checks and benchmarks using
the latest released immutable image remain separate delivery evidence; local
numbers must not enter the website comparison results.
