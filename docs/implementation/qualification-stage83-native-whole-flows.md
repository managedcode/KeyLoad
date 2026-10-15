# Coherent native whole-operation implementation checkpoint

Development evidence on macOS arm64, 2026-10-15. Original acceptance remains **20/104**; KL-105 remains open. Ready SourceN drafts are excluded from this checkpoint.

The integrated batch strengthens retained event identity checks, decimal/integer query translation, capability reporting and native ANN request identity. Whole-operation coverage spans shared document/event/queue transactions, snapshots, state envelopes, authorization, query contexts, placement, backup/restore and native recovery. The current source keeps Orleans routing, node-local ZoneTree ownership and all required RF3 gates.

| Check | Actual result | Boundary |
|---|---|---|
| R104 complete normal Release solution | PASS; zero warnings/errors; 463.376 s | 12,642 source inputs unchanged; regenerated Clustering/Unit images recorded |
| R105 full canonical formatter verification | PASS; 178.202 s | No source or DLL/PDB changes |
| R97 related Unit whole operations | 54 passed, four failed; 26.912 s; actual TRX overlap peak 50 | 58 cases/21 classes; every original failure retained |
| R106 four original Unit failures | Four passed; zero failed/skipped; 12.828 s | After R104, no source or compiled drift; no replay of the other unchanged54 cases |
| R92 related process/native recovery | 46 passed, one failed; 55.012 s | Three new native WAL-corruption, seven shared-domain and seven local-inbox cases passed |
| R98 original follower failure | One passed; zero failed/skipped; 23.472 s | Complete original clock/ACK/cold flow, not a full Recovery pass |

The initial Unit failures exposed incorrect test identity for administrative outbox observation, replay error precedence and an unclosed original ANN maintenance session. Repairs preserve every original permission denial, full outcome/state/cursor assertion and both cold attempts. Administrative observation now uses the existing root while separately proving the worker still denied. The Recovery failure was serialization of a genuinely absent MessageBody after ACK; both-null absence is now explicit, one-sided absence fails, and every present typed value retains full native-byte comparison. Production null serialization remains strict.

R104 changed16 Clustering output copies plus Unit DLL/PDB. R106 uses those final images. Earlier R97/R92/R98 results remain bound to their own images; do not sum them into one current58/47-case pass. Original earlier movement/typed-INSERT, RecoveryHostLifetime and model Explain failures remain assigned.

Selected R97 functional coverage records **13,377/108,668 lines (12.31%)** and **6,677/68,817 branches (9.70%)**. It includes failed functional operations and excludes load/benchmark credit. The changed-production class gap ledger is retained; this narrower selection does not establish full-product coverage, satisfy every changed-code threshold or compare directly with broader historical selections.

KL-105 attempted a separate, coverage-free existing native ANN whole operation at10,000 genuine records with normal SIMD; one test passed in32.324 s (process36.042 s). The native EventPipe CPU collector rejected attachment as UnsupportedRuntime with an unproved NativeAOT classification; a later query saw Unknown/ServerNotAvailable after the process ended. There are **zero delivered CPU samples**; other collectors did not start, loss/overhead remain unknown and no optimization or performance claim follows. This functional development case is not the required100,000/1,000,000-record Linux benchmark evidence.

R95 stopped before tests on IPC permission denial. R101 rejected a wrapper configuration before SDK launch. R102 produced no compiler output or changed images for381 s and was stopped by the coordinator; its graceful native exit0 is **not** a successful build. R104, with permitted native IPC, is the actual complete build receipt. Original failure/stop files are preserved.

| Original artifact | SHA256 |
|---|---|
| R104 build stdout | `71362ace9dc2dbf1ecd0561a65d1294a1e4b38d645f0062b516558473b0e15f1` |
| R97 initial Unit stdout | `9a2925dcc02ea3bc66ce21f466ce7eb35d22d81c9fc1e127d2d545dba9016d87` |
| R106 repaired Unit TRX | `541e406cfc16bd8f33423d62cf1bd540e1b58708992c8564abf5d08525691c21` |
| R92 initial Recovery stdout | `ed9c52d450c8ef1b830e21d7c6e4f1d84954e3c6c1c30fe31bcaeeb772a5bea4` |
| R98 repaired Recovery stdout | `4f1e3e0225d31f17717d712636a2a5297026227f0e7beaae1f050a3f149116c7` |

Originals live under ignored `TestResults/stage61-native-join/` with command/PID/UTC/source/DLL/PDB/closed-output/joined-process receipts. They are local development evidence. [Build and Tests38674118943](https://github.com/managedcode/KeyLoad/actions/runs/38674118943), for the previously pushed source85e406, remained queued with19 jobs at inspection; no cause or Linux success is inferred.

Current-source complete native suites, real Docker/Aspire RF3 through SDK and official MCP, delivered-source Linux runs, full functional coverage and all feature-specific fault/endurance/performance gates remain open. No new task acceptance, production readiness or power-loss durability is claimed.
