# Embedded benchmark acceptance

Goal: strict all-project source analysis with the real BenchmarkDotNet generated
consumer still working. Actors are benchmark callers, the generated child runner,
TUnit and GitHub CI. REQ-BC-022, AC-CQ-007/008, ADR047. Product/RF3/data/API/site,
comparative profiles, unrelated package upgrades and performance claims are out
of scope. The observed NU1608 graph join permits only a central CSharp5.0.0 peer
pin matching existing Common5.0.0; SDK/Orleans/BenchmarkDotNet/ManagedCode pins
and analyzer SDK-reference selection remain unchanged.

- AC-EM-001: KeyLoad.BenchmarkScenarios is a nonpackable Library with central
  pinned .NET10/C#14/BenchmarkDotNet0.15.8 and all normal SDK/custom analyzers.
  Public unsealed namespaced EmbeddedBenchmarks and its public benchmark/setup/
  cleanup methods have real XML and remain externally inheritable/discoverable.
  Existing executable keeps its invocation/defaults via one internal typed runner
  and FromAssembly on the fixture assembly. Remove the old fixture body and dead
  host-only package/dependency references in the coherent migration; no duplicate,
  compatibility alias, test-project spoofing, suppression or toolchain change.
  CLR fixture namespace/assembly migration is intentional; method names and their
  filter name fragments remain. No database wire or persisted contract changes.
  The actual restored benchmark/UnitTests graphs must resolve a coherent Roslyn
  CSharp/Common5.0.0 pair without NU1608, asset exclusion or warning suppression;
  enabled compilation and003's actual generated Dry consumer prove compatibility.
- AC-EM-002: retain the exact seeded collection/document/principal/credential,
  PartitionRef and operation payloads. PointRead, five-component CompositeKey and
  two-vector ExactCosine keep their current calls, values and MemoryDiagnoser.
  Owned store/temp directory are released by a standard IDisposable pattern even
  on setup failure; uninitialized and repeated cleanup/dispose are harmless.
  Use the real TimeProvider.System UTC time. No fake store, optional fallback,
  shifted command order, extra full payload copy or frozen clock. Real TUnit
  setup/read/cleanup and post-cleanup store failure prove normal lifetime; manual
  full acquisition/finally audit supplements environmental setup-failure paths,
  which cannot be made deterministic without a prohibited substitute/seam.
- AC-EM-003: a real TUnit case invokes the built actual executable with named Dry
  job and full JSON exporter using its ordinary out-of-process toolchain. It must
  return normally and produce exactly the three expected method identities, each
  with non-null statistics and positive actual Workload/Result measurements.
  Missing/failed compilation, execution, report/method/statistics/measurements,
  timeout or unexpected process error fails the test, even if exit code alone0.
  Capture/drain output concurrently with finite bounds and always dispose/kill
  owned process on failure/cancellation; await completion. Preserve raw reports
  under artifacts/qualification for existing CI upload. Never run locally or
  publish these Dry results as comparative/performance/site evidence.
- AC-EM-004: new local policy exists before code, canonical feature/ADR/architecture
  and exact project inventory are updated, all original policies preserved. All
  files/types/units/depth400/200/50/3, enabled builds, formatter/governance/import/diff
  and exact delivered-SHA complete GitHub TUnit/recovery/RF3/required suites pass.
  Existing Unit225/later19 debt, coverage and current qualification remain pending
  until genuine gates pass; no old-main or worker source claim unblocks them.

| Criterion | Automated caller proof | Command / level | Explicit review evidence |
|---|---|---|---|
| 001 | Real BenchmarkConverter discovers exactly three actual public methods; actual generated consumer from003 | GitHub UnitTests MTP after full Release build | class/XML/inheritance/default/argument/project graph diff |
| 002 | Real fixture setup/point payload/key/cosine and cleanup/repeated/uninitialized/post-cleanup cases | Same full UnitTests command in ci.yml | exact old seeding/timed call/values, all setup-failure resource/finally paths |
| 003 | Real executable/child build and three successful Dry full-JSON reports, timeout/cancel ownership paths | Same GitHub TUnit suite, raw artifacts/qualification | pinned0.15.8 exporter schema and bounded concurrent process/drain review; no synthetic result |
| 004 | MSBuild policy, full solution/analyzer/formatter/governance and required CI suites | Canonical exact-SHA ci.yml commands | truthful docs/status/source hashes and preserved unrelated dirty work |

No mock/fake/stub, runtime experiment, benchmark or test may run locally. TUnit uses
Microsoft.Testing.Platform. No new collector/baseline result is implied. Rollback
is a coherent scoped library/host/test/reference/inventory revert preserving every
mandatory quality requirement; never return to suppressed diagnostics or retained
duplicate fixture. The format of database files and comparative reports needs no
conversion; new generated consumer compiles against the new namespaced assembly.
