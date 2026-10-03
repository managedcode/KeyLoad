# Local Garnet core and ZoneTree diagnostics, 2026-10-03

This is local development evidence authorized by the owner, excluded from website/global measurements. BenchmarkDotNet 0.15.8 ran published Microsoft.Garnet 2.2.0 public raw Tsavorite and ZoneTree 1.9.8 sequentially on the same Apple M2 Pro/macOS 27.0.1 ARM64 host (.NET 10.0.12, SDK 10.0.401). Source was a dirty main checkout at 8071148cd; source hashes and originals are retained under artifacts/local. Other desktop/project work was present, and elevated scheduling priority was unavailable.

The read refinement uses 4096 deterministic 16-byte keys and 32/1024-byte values, one non-concurrent native session/fixture per child, hot key 0 or reserved missing key 4096, resident RAM, WAL/durability disabled, and an identical one-copy caller scratch contract. Actual generated jobs and every Actual row show 4194304 operations, one launch, 8 warmups and 10 iterations. All 80 Actual iterations last at least 100 ms. Values below are reported BDN means, with native outlier filtering preserved; they are not RF3 throughput or request percentiles.

| Operation | Payload | ZoneTree mean ns | Tsavorite mean ns | ZoneTree managed B/op | Tsavorite managed B/op |
| --- | ---: | ---: | ---: | ---: | ---: |
| PointRead | 32 | 106.685 | 57.019 | 0 | 0 |
| PointRead | 1024 | 117.175 | 70.051 | 0 | 0 |
| MissingRead | 32 | 25.546 | 30.800 | 56 | 0 |
| MissingRead | 1024 | 25.448 | 30.104 | 56 | 0 |

In this narrow local hot-hit lane, Tsavorite is 1.871× faster at 32 B and 1.673× at 1 KiB. ZoneTree misses are faster, while allocating 56 managed bytes per operation; Tsavorite misses allocate 0. Hit paths allocate 0 on both. These observations justify deeper cache evaluation, not a product storage switch or universal database leadership. Native allocation/RSS was not measured.

The original short 8-cell run also completed real point/miss/overwrite/create-delete operations for both engines, but its 1024-invocation/3-warmup/5-iteration timing has MinIterationTime warnings and large confidence intervals. Its write numbers do not establish a winner. The first attempted long-read CLI override was genuinely still 1024 because the InvocationCount attribute won; its originals remain separately retained. The source correction moves invocationCount into SimpleJob and supplies only the unroll-1 mutator. Default metadata must continue to assert 1024/1.

The pure eight-cell validator originally demanded 5 Result rows. Actual BDN output proved that Result rows are reduced by outlier filtering. The accepted GE005-V2 correction requires 5 unfiltered Actual rows and N retained Result rows, with one common launch and independently unique stage indexes. Both unmodified original 8-cell reports passed this real pure module.

Local TUnit/Microsoft.Testing.Platform execution passed all 32 raw fixture, CRUD, lifetime, report and workflow cases in the normal mode and all 32 with DOTNET_EnableHWIntrinsic=0, with no skips. The initial converter test failed only because its method-name oracle assumed alphabetical vendor declaration order; sorting the actual distinct names preserves the closed four-method/eight-cell contract. Local replication-term regressions separately passed 19/19 with no skips after correcting a malformed native-wire test vector. The full 26-project Release build passed with zero warnings and errors. These local checks do not establish delivered-source GitHub, full recovery or RF3 qualification.

The separate native-serialization suite also passed 59/59 with no skips, including real child cancellation and the generated-consumer Dry smoke test executing all 24 external cases. Dry timings are correctness evidence and are excluded from performance comparisons. Its final child-lifetime correction compiled without warnings/errors before this execution.

The broader local baseline launched after the delivered f403bf61e checkpoint passed all 194 process-recovery tests, with no skips. The full unit suite failed: 2494 total, 2425 passed, 69 failed, no skips. Those failures are recorded individually in the working plan and original failure inventory; focused passes do not make this baseline green. This is development evidence in a shared, concurrently changing checkout, not exact-source Linux qualification. Process recovery does not prove power-loss durability, RF3 or endurance.

After the native and fixture repairs, the complete local unit rerun at 13:28 UTC executed 2574 tests: 2570 passed, four failed, no skips. The earlier 13 CLI deadline cases passed in this rerun, so this receipt does not establish a current CLI lifetime defect. Open failures are the unknown native well-known type 58 guard, GitHub step pending-status rejection, malformed stored queue-body fixture, and membership cancellation's TaskCanceledException versus exact OperationCanceledException assertion. The unchanged strict assertions remain failures; no complete-suite pass is claimed. The integrated 26-project Release build and required formatter passed, and all current eligible source was committed/pushed to main at bfb04d64bf9c0ca8a1ab6e0c703194e1813250f0. Authentication reader regressions separately passed 24/24 in both normal/scalar modes. These are local development checks, with exact-source CI still required.

The authentic [GitHub diagnostic run 37124585233](https://github.com/managedcode/KeyLoad/actions/runs/37124585233), at exact f403bf61e730919a7b6e4b811539b870b8c8433d, passed the full Release build, formatter and governance job and all 32 raw-contract tests in each normal/scalar mode, with no skips. At 13:20 UTC both engine measurement jobs were queued; this receipt contains no GitHub speed numbers. The diagnostic run intentionally omits RF3 comparisons and website publication. Its original correctness archive 11274790938 has SHA-256 1b69f807ee076a895d3fe5bf4ab1374662effabc4439c1d067de5af3481fb36c, matching the GitHub artifact digest. Local means above remain excluded from website/global data.

Original local TRX SHA-256:

- normal32: 21e1047368d14a3a79bb0f15c114fcb8dbebae405cf3a41e645ac96d67e91228 (artifacts/local/raw-storage-correctness/normal-r2/KeyLoad.UnitTests_net10.0_arm64.trx).
- scalar32: b63deeb54bc637f6d9ba0eeb778c2d83f086d301c13edcd2c2fbdb422b26c6ac (artifacts/local/raw-storage-correctness/scalar-r2/KeyLoad.UnitTests_net10.0_arm64.trx).
- replication-term19: d54799b174fc5544939f82a3469fbd1b4c6c2f8009c043befb70ff2d1829128a (artifacts/local/replica-term-cleanup-r3/KeyLoad.RecoveryTests_net10.0_arm64.trx).
- native-serialization59: 8504133e16809e7eb9d5c545dc6b5f8159eab6332db7ce00a64be974e6c49f88 (artifacts/local/native-serialization-correctness/final/KeyLoad.UnitTests_net10.0_arm64.trx).
- full-process-recovery194: de322978570f722da5fe4451e886eee0de949ce01871bea9bbe9ac0fe9334bb6 (artifacts/local/full-recovery-f403/KeyLoad.RecoveryTests_net10.0_arm64.trx).
- full-unit2494: b0e910db49a85e86da07967c62cf6fc1c35f04c7ecbbdca6955a471875ce8d89 (artifacts/local/full-unit-f403/KeyLoad.UnitTests_net10.0_arm64.trx); failure inventory c82a01381826cc1578312b438a5db1c02dee2f36dedef10169492b5d2150f6f4.
- full-unit2574: 4d1882673a7d812e68aef7bf68d9c474fd1c6539317e3c779a0091efa44295e2 (artifacts/local/full-unit-nar001-joined/KeyLoad.UnitTests_net10.0_arm64.trx).

Original long-read report SHA-256:

- zonetree: dbc2e139d8ad3c8ffc9987ebab3e4c4b017bad5ce976e42fcad649255b4a8740 (artifacts/local/raw-storage-zonetree-long-read-4194304/results/KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons.RawStorageBenchmarks-report-full.json).
- tsavorite: 4e1140dd17eeccd7dabf99d01f23b58ebc344603717ebd7577ea050c13f30eb4 (artifacts/local/raw-storage-tsavorite-long-read-4194304/results/KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons.RawStorageBenchmarks-report-full.json).

Raw benchmark source SHA-256 at the read refinement:

- benchmarks/KeyLoad.BenchmarkScenarios/Features/BenchmarkComparisons/RawStorageFixtureFailures.cs: c2a5c9f92f5c5a16e1686c43b6b9115c095e27701b94c23a7edc32ddd808c6b1.
- benchmarks/KeyLoad.BenchmarkScenarios/Features/BenchmarkComparisons/RawStorageFixtureZoneTreeEngine.cs: 2314ed2a7d33c7e6aea1abd511926a83097af46fc48035398a760fabfabf7012.
- benchmarks/KeyLoad.BenchmarkScenarios/Features/BenchmarkComparisons/RawStorageFixtureEngine.cs: 8c579c572deb580eb68db074cbbcac627d3198e92b96827b3ca132ec7a576d19.
- benchmarks/KeyLoad.BenchmarkScenarios/Features/BenchmarkComparisons/RawStorageCorpus.cs: b6cfd9d9322955a42cd2180e6499e90abd759c6d0b9e87c8d42feb50fc694a45.
- benchmarks/KeyLoad.BenchmarkScenarios/Features/BenchmarkComparisons/RawStorageBenchmarks.cs: b87c3654684b96f0f3020ee89e9a6446ec833b2ea2d33d5a0d8a2b8c5f38b9fc.
- benchmarks/KeyLoad.BenchmarkScenarios/Features/BenchmarkComparisons/RawStorageFixture.cs: cbb76ab5249c2f16e2cce0a3cfdd148687d69f62f350486270ed187bf7b51a23.
- benchmarks/KeyLoad.BenchmarkScenarios/Features/BenchmarkComparisons/RawStorageFixtureTsavoriteEngine.cs: f1abcf5b41a57351367b60a6c4d63621fff528e4e3a0f56eca4d184618c7d64b.

Remaining: reliable equally bounded write durations, random/hot/mixed concurrent workloads, full Garnet RESP/network/AOF/recovery, equal fault/durability contracts, actual RF3/multi-host KeyLoad comparison, server phase/RSS/contention measurements, and authentic coverage gates. ADR-066 remains Accepted; local numbers cannot publish through the website aggregation.
