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

The first million-document run on MVID `a1390f03-84e5-42e8-94c6-15cc65384593`
failed the independent cold posting oracle. Native workers began reporting the
file-count bound near 636,000 documents, although the joined owner later returned
and a sealed manifest existed. The receipt contained 512 file entries, of which
500 were absent; only twelve tracked files remained. Across the three roots,
fifteen physical files occupied 88,833,147 bytes. The limit had counted historical
native segment names rather than current files. This was an incomplete derived
index, not evidence of canonical record loss.

New file admission now compacts only file entries proven absent by stat under
the same accounting operation as creation, move or replace. Directories and
the separate unlinked-open-handle accounting remain. Replacement targets are
admitted together; present unowned paths, links and uncertain I/O still refuse.
The physical file and byte limits are unchanged. Before bootstrap disposal,
joined native eviction must also leave `InMemoryRecordCount` zero; a background
merge failure cannot make memory-only postings appear successfully published.

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
| Allocated bytes | 1,217,010,256 | 69,167,416 |
| Root censuses | 145,893 | 2,523 |
| Verified persisted postings | 24,000 | 24,000 |

The observed fixed-work counts are 17.60 times fewer allocated bytes and 57.83
times fewer root censuses. The earlier intermediate implementation's figures
do not describe the current accounting path.

The current 1,000,000-document run completed publication and joined close in
200.8995563 seconds within its original 240-second budget, allocating
86,895,076,592 cumulative managed bytes and counting 771,936 root censuses. After a sealed
cold open, an independent SHA-256 oracle verified every `(token, record,
previous-token)` key before that same deadline: 11,999,937 unique postings, no missing or additional keys.
The 63 duplicate composite keys are expected from the 12,000,000 token inputs.
The actual lease also verified canonical expected `w000` positives at its
declared source cut before publication.

After complete joined cold verification, the actual decoded receipt contained
14 file entries and three directory entries: thirteen files and all three
directories existed, with one retired file entry absent. Across the three roots,
sixteen physical files occupied 98,369,252 bytes in seven directories. The normal
512-file, 32-directory and 268,435,456-byte caps were unchanged. Receipt compaction
runs at new file admission; it does not promise that every later retirement
immediately rewrites the receipt.

## Identities and limits

Baseline DLL MVID: `47c67e65-ca06-4fc4-84a4-464b72239952`;
SHA-256: `2bcf50c7869297faeb548f1a3707722d4a1db23b58d3fe1d3a4101162c743d0c`.
Construction checkpoint DLL MVID: `29c1ace4-4b14-48c2-aaec-ca3d7874360d`;
SHA-256: `7fcade58c6ede39383b4db4bfddc6e053210d17ad7575650d42dcbde90553a10`.

A separate full Core workload at that checkpoint loaded the same million
documents through the canonical ZoneTree store and queried through the real
authorized `TextHostSearch` path. It verified the exact top ten and full JSON
at the unchanged canonical cut. One warm query took 6.404 seconds and allocated
2,980,286,984 managed bytes. The process then failed joined shutdown: closing the
read-only index tried to stage another 116,984,290-byte manifest while the
256 MiB generation quota had only 83,073,886 bytes available. The original
failure is retained in `fullquery-clean-summary.json`.

The read-only close now joins the native index and verifies the existing sealed
manifest without staging a second copy. Construction figures above measure the
earlier checkpoint and exclude this change to the close path.

The same Core checkpoint also ran eight warm queries on the million-document
corpus with the exact top-ten and full-JSON oracle. A six-second allocation
window inside that loop sampled 14,278 weighted allocation events representing
1,523,112,224 bytes. Compiler-generated closure allocations in
`TextProjectionLease.ObserveToken` and `BeginRecord` represented 296,673,344
bytes, or 19.48% of the sampled weight. Both closures belong to writes during
construction, but the compiled methods allocate them before checking whether
the generation is writable. The capture provides no dropped-event count.

Ignored local evidence lives under
`tests/KeyLoad.FunctionalTests/bin/Debug/net10.0/performance-investigation/search-native-20261103/`:
`final-frozen-measurements.json`, `final-frozen-producer-hashes.json`,
`scale-final-1m-owner-summary.json`, retained baseline/current DLLs and PDBs in
`baseline-producer/` and `final-frozen-producer/`, harness sources, clean-run JSON
lines and separate allocation captures. The failed earlier million-document run
and its original receipt remain retained separately. Raw diagnostic output is
not published here.

A separate two-second allocation capture on the earlier `a1390f03` producer observed native disk
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
