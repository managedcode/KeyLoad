# Native ZoneTree measurements

These measurements run the upstream transaction benchmark directly, without
KeyLoad, Orleans, HTTP or Aspire. They establish the engine's local workload
costs and help locate additional work in KeyLoad. They do not replace the
released-image Linux [comparison benchmarks](../Features/BenchmarkComparisons.md).

## Source and execution

The measured source is ZoneTree 2.1.1, commit
`568c222736213f45a9963f51250621a8de092f6d`. The owning instructions and workload
are [upstream benchmarks/transactions](https://github.com/ZoneTree/ZoneTree/tree/568c222736213f45a9963f51250621a8de092f6d/benchmarks/transactions).
The checkout is the existing sibling `../ZoneTree`; no private source copy or
KeyLoad dependency override is involved.

Run the upstream Release benchmark with .NET 10. Its reference-run instructions
require `DOTNET_TieredCompilation=0`. This setting belongs to this upstream
measurement command, not KeyLoad's product configuration. Do not rebuild or run
another load concurrently. Each engine/scenario/worker row starts with its own
fresh database; a requested sync mode must never be inferred from a reused async
database.

From the ZoneTree checkout:

```sh
dotnet build benchmarks/transactions/src/Transactions.Benchmark.csproj -c Release -warnaserror
DOTNET_TieredCompilation=0 dotnet benchmarks/transactions/src/bin/Release/net10.0/Transactions.Benchmark.dll \
  --keys 100K --workers 1,4,16 \
  --engine zonetree-mvcc-ser-async,zonetree-mvcc-ser-sync,zonetree-mvcc-snap-async,zonetree-mvcc-snap-sync,zonetree-raw-async,zonetree-raw-sync \
  --scenario rmw-uniform,insert-unique,hot-1,hot-256,batch-100,durable-rmw,snapshot-point \
  --data benchmarks/transactions/src/bin/Release/net10.0/keyload-native-20261102/data \
  --output benchmarks/transactions/src/bin/Release/net10.0/keyload-native-20261102/results \
  --keep-data --timeout-seconds 300
```

The command selects 126 rows: seven scenarios, six engine variants and three
worker counts. `100K` sets the base key count and job scaling; hot-1 loads one key
and hot-256 loads 256. Each scenario retains upstream's own work count. The
report's groups/second counts completed units of work. For `batch-100`, one group
contains 100 writes; for `snapshot-point`, one snapshot group contains ten point
reads. Keep groups/second, written records/second and individual reads/second
explicit.

## Modes and guarantees

| Variant | Isolation | Actual native WAL | Ordinary completion |
| --- | --- | --- | --- |
| `zonetree-mvcc-ser-async` | Serializable MVCC | `AsyncShared` | Commit can depend on queued WAL records |
| `zonetree-mvcc-ser-sync` | Serializable MVCC | `Shared` | Caller appends before commit returns |
| `zonetree-mvcc-snap-async` | Snapshot MVCC | `AsyncShared` | Commit can depend on queued WAL records |
| `zonetree-mvcc-snap-sync` | Snapshot MVCC | `Shared` | Caller appends before commit returns |
| `zonetree-raw-async` | Direct operations | `AsyncCompressed` | WAL work can remain queued |
| `zonetree-raw-sync` | Direct operations | `SyncCompressed` | Caller publishes the mapped WAL tail |

Upstream disables WAL compression for all six variants. Its transactional factory
promotes configured `SyncCompressed` to `Shared` and `AsyncCompressed` to
`AsyncShared`. The `-sync` suffix selects caller-side appends; it does not request
an fsync for every ordinary commit. `durable-rmw` separately waits for
`TryCommitDurableAsync` for transactions or `SyncWALAsync` for each raw unit of
work. Concurrent durable callers may share a disk sync.

The upstream benchmark adapts these durable APIs to its synchronous dedicated
worker threads with `GetAwaiter().GetResult()`. This is the benchmark's scheduler;
KeyLoad continues to await engine APIs asynchronously inside its Orleans work.

Raw read/modify/write is not an atomic transaction. Upstream intentionally skips
its transactional invariant/anomaly checks for raw rows. Transactional rows
retain the benchmark's actual checks after close/reopen. A clean reopen is not
an abrupt-process-kill test or evidence of power-loss durability.

## Local result, 2026-11-02

Apple M2 Pro, 12 logical processors, 32 GiB RAM; macOS 27.2 Arm64;
SDK 10.0.403, runtime 10.0.14, Release, server GC. Upstream uses 100-byte
values, seed 42 and scale 1. This differs from KeyLoad's 1,000,000-record,
seed-1729, 16-network-client comparison contract.

The process exited 0 after all 126 rows and 40,320,000 completed scenario groups.
All 84 transactional rows passed the native checks after close/reopen; the 42 raw
rows explicitly skipped transactional checks. No row failed or timed out.
All 126 retained `0.json` files matched their expected mode.

Rates below are completed groups/second, rounded to whole groups. Batch columns
contain 100 writes/group; snapshot columns contain 10 reads/group.

| Variant | RMW, 1 worker | RMW, 16 | Batch-100, 16 | Snapshot, 16 | Durable RMW, 1 | Durable RMW, 16 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Serializable async | 294,089 | 224,443 | 13,825 | 789,557 | 229 | 1,060 |
| Snapshot async | 308,444 | 217,073 | 13,358 | 749,838 | 207 | 1,427 |
| Serializable sync | 146,352 | 142,192 | 7,844 | 721,526 | 236 | 1,951 |
| Snapshot sync | 229,986 | 256,864 | 9,026 | 763,573 | 225 | 1,890 |
| Raw async | 1,045,504 | 2,863,608 | 30,131 | 1,816,056 | 227 | 2,951 |
| Raw sync | 1,207,622 | 1,194,499 | 17,632 | 1,768,583 | 232 | 1,981 |

Per row, uniform RMW completes 500,000 groups, unique insert 300,000, hot-1
100,000, hot-256 300,000, batch-100 20,000, snapshot-point 1,000,000 and durable
RMW 20,000. Contention changes completed work and retries: raw hot-key RMW is
not an equivalent atomic counter. Serializable sync durable RMW increases from
236 groups/second with one worker to 1,951 with sixteen on this disk; native
group sync does not imply linear scaling for every variant.

The original files remain in the ZoneTree checkout under
`benchmarks/transactions/src/bin/Release/net10.0/keyload-native-20261102/`:
`command.txt`, `measurement-identity.json`, `metadata-mode-audit.json`,
`measurements.csv` and `results/20261102-210403/100K/summary.json` / `summary.md`.
The identity file retains source, runtime and DLL/PDB hashes. This is one
fixed-order pass with upstream monitoring and histograms. Short rows have limited
precision. These measurements establish no KeyLoad throughput multiplier or
version-to-version speedup.

## Applying the measurements to KeyLoad

- Choose transactions from operation invariants. A document plus its indexes,
  command outcome and other atomic effects cannot lose their atomicity for a
  raw-operation throughput number. Resources explicitly configured as
  nontransactional use their existing direct-storage contract.
- Evaluate separate physical trees for independently owned resources against
  measured contention, physical ownership, joined shutdown, backup and replication.
  A native transaction belongs to one tree; splitting resources across trees
  must preserve KeyLoad's advertised cross-resource atomic operations. A logical
  resource prefix and an independently opened ZoneTree are different boundaries.
- Keep ordinary commit publication separate from an explicit durable barrier.
  Await the engine's asynchronous APIs and retain its group sync and maintenance;
  do not add per-command disk flushes or a second KeyLoad WAL scheduler.
- Inspect the existing store before claiming a WAL mode. ZoneTree provides the
  persisted `0.json` metadata and the public
  `ZoneTreeMetaWAL.LoadZoneTreeMetaWithoutWALRecords` reader. Requested options and
  an already-open writer are separate observations. A kill of an existing async
  writer does not qualify a newly requested sync setting.
- Keep one native snapshot while reading its authorized range. Seek indexed
  bounds, avoid decoding unrelated records and enable
  `ContributeToTheBlockCache` for repeated native iterator reads where the workload
  benefits. KeyLoad's native forward and reverse iterators already enable it.
- Compare the same operation, concurrency, record size, isolation and durability.
  Separate engine time, serialization/allocation, authorization, Orleans routing,
  replication and transport before selecting the next optimization.

Local measurements and their original files stay separate from public website
figures. The website consumes only GitHub Benchmarks artifacts.
