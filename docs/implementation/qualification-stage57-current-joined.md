# Stage57 current joined implementation checkpoint

This checkpoint contains the joined graph, messaging and time-series record-integrity repairs; complete authorization, blob-lifetime, generation-visibility, telemetry and SQL authority flows; process-recovery flows; and native test-selection/startup controls. It was checked in the current main checkout based on `8e4cadd03d6547f05ac6904f7c7faf7765762e27`. The separately prepared Result/TimeProvider, ANN, live-page and telemetry successors are not included in these observations.

At this checkpoint the original104-task plan recorded **18 accepted,86 in progress**. The subsequent fc4 Linux snapshot regression reopened KL-035; the current plan records **17 accepted,87 in progress**. KL-079 remains in progress after the earlier Linux full-page regression. See [the original acceptance and regression crosswalk](qualification-stage57-kl035-kl079.md). This checkpoint does not close other original tasks.

## Actual local checks

| Check | Observed result | Original scope |
|---|---|---|
| Complete native solution analysis | 0errors,0warnings | All27 projects;2026-10-11 15:14:45UTC |
| Canonical `dotnet format KeyLoad.slnx --verify-no-changes --no-restore` | Exit0; no source drift | All8600 observed source inputs;15:13:27UTC |
| Full Release solution build | 0errors,0warnings;5m36s | All27 projects;8600 source inputs unchanged;15:23:43UTC |
| Relevant native TUnit Unit run | 386 executed,385 passed,1 failed,0 skipped; actual maximum overlap50 | Graph/queue/chunk/sample integrity, SQL authority, authorization, blob lifetime, native CQRS/startup and selection controls |
| Exact failed Unit case with corrected runner path | 1/1 passed;2.45s | Same test ID, source and every Release DLL/PDB; no product change |
| Real process-recovery selection | 13/13 passed,0 skipped;32s; actual maximum overlap13 | Five blob publication/retirement process-cut families and queue lease authority |

The original Unit invocation remains a failed invocation. Its only failure was `RequestCqrsProbeRecordFileTests.AcCrs004RejectsLinksDirectoriesAndSocketsWithSafeDetails`: this observer supplied a physical temporary directory long enough to exceed macOS's104-character Unix-socket path bound. Only that case was repeated with the short physical directory `/private/tmp/kl57u48t`. Its original native TRX ID `4cb4080a-37ee-ba07-8d38-8058090f95f4` and complete operation assertions were preserved. The385 original PASS cases plus this same-image successful repeat cover386 distinct selected Unit cases; the13 recovery cases are separate. No original report was replaced or relabelled as green.

Both suites used `scripts/Features/TestInfrastructure/run-tests.mjs`, the actual Aspire-owned test lifetime, native TUnit configured maximum50, enabled native SIMD and separate physical temporary/results/pipe directories. Before/after source and Release image observations showed no drift. Recovery verifies process interruption, not power-loss durability. macOS observations are development evidence; required qualification remains Linux-only.

## Original observation identities

| Original observation | SHA256 |
|---|---|
| Native analysis result | `77f812c953ea2bcb3caf22b56ad8ad239ff750e3f6663515c1424afb1af79acf` |
| Full solution build stdout | `063b0b940329fd3d901d606d84ca95b42aa936a66c84b55d3be67a66fcd8e314` |
| Original386-case Unit TRX | `07409b9a7926cb1e06dcdb5b00792206d2c79ac7a764d8ca0336b9ed509c67b3` |
| Exact one-case short-path repeat TRX | `72cbfa6df83040063a779bdf7f6d1a8027d7868b3895d23cc262fb5f93af0dfe` |
| Original13-case recovery TRX | `23a6010808611c3737fdefee9f9b249cf9dd800cad58c6f3cf453fb3c8a12a31` |
| Independent same-image case/result observer | `c78c50d8e75a46fac90dfaff50015229f549bff3e9b01ddc6e3b6df471108f8a` |
| Exact runtime source observation | `05aff1024dae0ab0f1527beab5e43042608ed0e8c306165828c34505db6b98e7` |
| Exact runtime Release image observation | `03b57040735822204a27b4fdd9decee1850b4886f4127cf0b76242fdefcd6c3c` |

Original logs, binlog, source/image observations and reports are retained in the owned `keyload-stage57-current-joined-solution-r2-20261011`, `keyload-stage57-main-focused-runtime-r1-20261011` and `keyload-stage57-main-short-temp-r2-20261011` evidence directories. This document is a subsequent documentation-only addition; it is not claimed to have existed among the8600 runtime source inputs.

Fresh complete native Unit case admission and the required Linux full-suite, recovery, Aspire Docker RF3 SDK/official-MCP, functional coverage, endurance and performance gates remain separate. An implementation checkpoint, local build or focused PASS does not satisfy those gates.
