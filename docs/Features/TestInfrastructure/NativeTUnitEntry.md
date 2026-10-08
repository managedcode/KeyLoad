# TestInfrastructure: native TUnit CI entry

The owner correction on 2026-10-07 requires CI to run native TUnit tests after compilation. TUnit owns test outcomes and native Detailed progress. Existing TUnit fixtures start real Aspire applications and exercise discovered endpoints with the actual C# clients; benchmark workload containers also execute TUnit. Benchmarks remain exclusive to their separate pipeline.

REQ-TUNIT-ENTRY-001: select each functional, scalar, recovery, RF3, site or benchmark project without an outer Aspire CLI invocation. AC-TUNIT-ENTRY-001: native argument regressions verify project/filter/TRX/coverage, bounded parallelism/timeout, scalar environment and rejection of unknown/duplicate options. Actual C# TUnit client operations and measured workload timings remain unchanged.

REQ-TUNIT-ENTRY-002: native RF3 server coverage retains the original source/manifest/collector contract. AC-TUNIT-ENTRY-002: the TUnit session starts only the existing Aspire image prerequisite, propagates the original validated runner environment, removes the recursive runner, observes its original exit and joins output/application/image cleanup. Existing native coverage fixture tests and Linux RF3 coverage receipts qualify this lifecycle.

REQ-TUNIT-ENTRY-003: all actual benchmark client workload execution is a TUnit test. AC-TUNIT-ENTRY-003: the pinned Aspire load-generator image contains the ComparisonHost native TUnit executable, runs its existing C# client workloads after resource dependencies settle, asserts failure/success and retains original measurement JSON plus native TRX. Existing host startup, negative configuration, report and cleanup cases cover failures.

Implementation: [ADR-117](../../ADR/ADR-117-native-tunit-ci-entry.md), TASK-TUNIT-ENTRY-001..004. Root owns scripts/Features/TestInfrastructure, workflow joins, IntegrationTests Features/CodeQuality/Lifecycle, ComparisonHost Features/BenchmarkComparisons/Cases and associated regressions. Frontend: N/A, runner execution only. Contracts: N/A, no public database API change. Existing RF3 membership, authorization, scalar, recovery, correctness, provenance and performance requirements remain mandatory.

Verification for this editing task is compilation, native formatting, Node syntax and workflow syntax only. The owner explicitly requested no local runtime tests or infrastructure startup; Linux runtime qualification remains unproven until the original CI suites complete.

Automated traceability: AC-TUNIT-ENTRY-001 maps to `tests/KeyLoad.UnitTests/Features/TestInfrastructure/Cases/NativeTUnitSelectionTests.cs`; AC-TUNIT-ENTRY-003 maps to ComparisonHost `NativeClientWorkloadTests`, the existing real host configuration/report/cleanup cases and ComparisonTests `NativeWorkloadTestReportRetentionTests`. AC-TUNIT-ENTRY-002 maps to the existing native RF3 collector/fixture cases and complete Linux coverage cohort; no local runtime qualification was executed. Static stage evidence: [NativeTUnitVerification](NativeTUnitVerification.md).

REQ-TUNIT-ENTRY-004: the original Linux RF3 job must have a bounded aggregate scheduling budget for the complete same-image native product-coverage cohort. AC-TUNIT-ENTRY-004: the `docker-rf3` job in `.github/workflows/build-and-tests.yml` allows at most 180 minutes for its build, required RF3 suite, two full uninstrumented unit censuses, ten positive instrumented unit groups, instrumented recovery/RF3 runs, strict descriptor admission and product merge. Preserve every individual native test/session timeout, corpus, original exit code, no-skip rule, source/image identity and coverage threshold; a larger job budget does not qualify a failed or incomplete run.

TASK-TUNIT-ENTRY-005 freezes this scheduling amendment before the workflow change. The unchanged local R111 full unit census took 19 minutes 28 seconds and failed one ANN deadline; its duration is planning evidence only. The required normal/scalar census and group union execute the functional cases twice in each mode, with build, RF3, recovery and admission in the same job. The former 60-minute aggregate budget cannot be treated as a demonstrated bound for that complete cohort. Root owns the workflow join; the coverage agent may prepare a private overlay against this contract. Verification is the original successful complete Linux cohort and its native receipts, with failures and timeouts retained. Frontend and public database contracts: N/A, CI scheduling only.

REQ-TUNIT-ENTRY-005: an explicit local six-silo membership selection owns its fresh
image prerequisite and final exact-tag cleanup inside the TUnit case.
AC-TUNIT-ENTRY-005: [ADR-119](../../ADR/ADR-119-tunit-owned-local-membership-image.md)
defines the exact selector, typed handoff, source/image admission, six actual
container proof, SDK/MCP no-dispatch flow, failure joins and unchanged bounds.
TASK-TUNIT-LOCAL-MEMBERSHIP-IMAGE freezes the ordered source/test stages there.
The default GitHub route and Linux delivery gates remain mandatory.

REQ-TUNIT-ENTRY-008: original required RF3 failure diagnostics become available before the longer covered cohort completes. AC-TUNIT-ENTRY-008: after the original `rf3-required` native step finishes with success or failure, the same job uploads its unchanged original reports and already prepared source/image receipts as `docker-rf3-original-diagnostics`; a native report-presence check includes ignored test artifacts, and missing reports fail that check before upload. This artifact is diagnostic evidence only. Complete qualification still requires the existing terminal `docker-rf3-qualification` artifact, final source/image verification, every required suite and strict coverage admission. No copied or edited report, synthetic outcome, skipped suite, changed deadline or new permission is admitted.

TASK-TUNIT-EARLY-RF3-DIAGNOSTICS-008 maps this requirement to the existing native RF3 invocation and pinned artifact action in `.github/workflows/build-and-tests.yml`, under ADR-117. Root freezes the contract, adds the native step identity and diagnostic upload, reviews the workflow, then authenticates the original Linux run/attempt/source/job/artifact digest. The earlier ordinary RF3 failure is otherwise unavailable from the artifact API while the full covered cohort is still executing. Frontend and public contracts are N/A; this only shortens the failure feedback path. Rollback removes the additional diagnostic upload; all final qualification gates remain mandatory.

REQ-TUNIT-ENTRY-006: the explicit standard RF3 Q2/schema batch also owns fresh
image preparation and cleanup through its native C# fixture. AC-TUNIT-ENTRY-006:
the exact second selector, typed handoff, three actual container identities,
eight whole operations, failure joins and source/image/cleanup bounds are frozen
in ADR-119. TASK-TUNIT-LOCAL-RF3-IMAGE-006 maps to the existing native selection
whole-process tests and these real SDK/official-MCP cases; it authorizes their
current local runtime qualification while retaining the separate Linux gate.

REQ-TUNIT-ENTRY-007: the fixture's real Docker CLI subprocess remains owned
through cancellation and failure. AC-TUNIT-ENTRY-007: every started original
process, exit wait and stdout/stderr reader settles before the helper returns
or throws; preserve the initiating cancellation and all cleanup failures, and
run a healthy native Docker inspection afterward. No detached timeout or
synthetic Docker executable qualifies this operation. TASK-TUNIT-DOCKER-JOIN-007
and its exact source/test stages are frozen in ADR-119. Local fixture evidence
does not replace the original Linux RF3 and source/image gates.


### TASK-MEMBERSHIP-NATIVE-HEALTH-MAPPING-001 (accepted source repair; runtime pending)

REQ/AC-MEMBERSHIP-002/003/008 and NativeTUnitEntry's existing fixture-owned six-silo gate require distinct native states, not generic HTTP availability. Actual local R322 failed1/1 at904.656s with zero4200-source/896-image drift. Group A native HTTP repeatedly returned404 for /health/membership-authority while AppHost WaitForAuthority kept Group B pending;15-minute initiating cancellation then canceled creation of node4-6. Original60-second DCP watch failures and secondary OfflineFileNotFound cleanup remain original separate evidence, not inferred primary causes.

Freeze before code: Server ClusterRouting Transport/ReplicaMembershipHealthEndpoints.cs maps GET /health/silo to200 only after existing OrleansNode.SiloJoined and actual published native Grains factory are present, otherwise503. GET /health/membership-authority returns200 only in validated Authority mode after existing node native join/factory publication and ReplicaMembershipAuthorityOwner.IsReady (real IMembershipTable provider published only after Group A native silo/catalog startup, and admission still open); Local/Proxy/unpublished/closed states return503. No address/catalog/credential/payload is disclosed; boolean routes return empty status only. Owner.IsReady is the same existing native authority endpoint admission gate; no replacement provider, probe/reflection, unconditionalHealthy or application-request dispatch is introduced.

Group A early authority health MUST NOT require six Active rows, or Group B's native WaitFor gate would deadlock. Existing GET /health/membership-ready remains unchanged: actual native ReadAll/proxy path, six exact Active generations/fingerprint, bounded historical rows and local membership presence. Both group database-ready routes remain503 and SDK/officialMCP deny before request-grain dispatch. AppHost dependencies and all original container/image identities, deadlines, cancellation/reader/node/storage/lock/image joins remain unchanged.

Existing real TwoRf3MembershipProfileTests.AcMembership001To003UsesOneSixSiloMembershipAndKeepsBothDatabaseGroupsClosed is the regression: native Group A authority gate permits Group B startup, independent actual six-silo fingerprint/membership and per-node silo200/membership200/data503/authorityA200-B503/publicSDK-MCP-denial oracles remain exact. Source review cannot claim the before-catalog startup observation or runtime success; those existing acceptance criteria remain required. Root owns guarded join, strict solutionbuild, fresh exact native six-silo gate and mandatory full Linux suites. No watch timeout/configuration is raised. Rollback removes both missing route mappings together with this task contract only; existing three-node readiness/ownership contracts remain unchanged.
