# Stage60 native execution checkpoint

The original 104-task plan remains **18 accepted and 86 in progress**. KL-105 is a separate additional task. This checkpoint does not close another acceptance gate. Current functional coverage is unmeasured; load tests do not contribute to functional coverage.

ASP.NET Core Authentication/Authorization owns HTTP authentication. Its authenticated subject is checked against current persisted database policy for each operation. ManagedCode.Orleans.Identity carries verified context between grains. No Azure identity provider is selected by this checkpoint.

## Implemented operation repairs

The native six-host recovery fixture now keeps stable Orleans serializer aliases, validates its original inputs, transfers ownership only after successful construction, and joins every acquired owner when construction fails. Native discovery cancellation releases the original held callback and joins the same original task and memoized stop before disposal. Original exceptions remain observable.

Authorization flows retain malformed and missing credential refusals before dispatch, complete no-effect observations, repaired persisted policies, exact command replay, healthy continuation and same-root cold checks. Real HTTP client fixtures join client disposal, Kestrel stop and Kestrel disposal even when an earlier owner fails. Failure sampling follows those joins.

The committed-response fixture now uses the existing required `id.secret` credential format. Its original R8 failure occurred in native credential lookup; a stored hash alone had not proved that credential usable through the public operation. No product authentication check was weakened.

Physical owner probes now await the same native replica-startup prerequisite as physical catalog startup before authentication. The subsequent R11 operation failures prove that this prerequisite change **does not resolve the complete startup defect**. Its remaining cause requires original receiver evidence and owning-source review.

## Recorded local evidence

All runs below are macOS ARM64 development evidence with normal mandatory SIMD. Configured native TUnit parallelism is 50; selected counts are actual discovered instances, not a claim that 50 tests ran simultaneously.

| Cohort | Actual result | Source stability |
|---|---|---|
| Full solution Release R8 | 0 warnings, 0 errors; 359.139 seconds | 8,943 inputs, zero drift; before the later credential and startup changes |
| Native Unit R8 | 8 passed, 2 failed | Original malformed fixture-credential failures retained |
| Unit Release R9 | 0 warnings, 0 errors; 184.375 seconds | 8,943 inputs, zero drift |
| Same native Unit10 R9 | **10 passed, 0 failed, 0 skipped**; 9.447 seconds | Original native discovery, source/PDB/image binding and joined process reports; zero drift |
| Recovery Release R11 | 0 warnings, 0 errors; 106.964 seconds | 8,944 inputs, zero drift |
| Same native Recovery10 R11 | **8 passed, 2 failed**; native exit 2 | All 8,944 sources and 2,650 DLL/PDB images unchanged; original runner exit and both readers joined and disposed |

R11's two original six-host cases fail during genuine creation/startup, before their kill/cold operation. The signed receiver trace reports Authentication/OwnershipLost while the observed deadline and stopping flags are false. This is not proof of an observed leader-state cause. No owned recovery/CrashHost process remained in the subsequent process observation. The failed roots are not attributable from the original exception, so successful settlement of their 18 storage locks is **not claimed**. The separately prepared failure-root attribution is excluded from R11.

Original reports, TRX, native discovery and image receipts remain under `TestResults/stage60-native-originals/`. Completion SHA256 values:

| Original completion | SHA256 |
|---|---|
| Full Release R8 | `0a4978541cfb86b916f53cea8df6c19b59045a2d80ba7a1450921a9d8fbd339c` |
| Unit Release R9 | `e2d9cd778ba3728d0f90c71c1b0c0f3231b2a1aecda49544ad85444b2825c4f5` |
| Unit10 R9 | `1b53e34080df03088718f1f4b4e1858f52383b8b461c2316dccb4bb6a89cc247` |
| Recovery Release R11 | `28160b8e5f3c002c41a334e2aa17015d0162d1514576ee99790781c22a3baad7` |
| Recovery10 R11 | `b728a5a7041824457f34efc11319b08b5f8d51fe3f0029cf4521620d17b431bc` |

Repository governance and `git diff --check` passed after runtime settlement. Complete current-source solution/formatter, Linux unit/recovery, genuine SDK/official MCP Aspire RF3, successful functional coverage, fault/endurance and measured performance gates remain open. Source88, native replay READY115, snapshot/restore and the additional MCP/live-query packets are prepared successors, not members of these recorded cohorts.

## Delivered Linux history

The recorded `001bed73675ff47bf6feb56a4bed1dcdb1555e4c` [Build and Tests run](https://github.com/managedcode/KeyLoad/actions/runs/38170003679), attempt 1, passed 349/349 process-recovery cases and 3,469/3,480 Unit cases. Its 11 Unit failures remain original evidence; passing selected local successors does not replace that complete suite. KL-065 retains its independently verified 78-case acceptance at the earlier recorded source. KL-035 and KL-079 remain in progress. Process-kill proof is not power-loss or production qualification.
