# TestInfrastructure

### Native process settlement and complete CI suite budget

REQ/AC-TEST-015 requires the local-image lifetime helpers to await already
completed original exit/stdout/stderr tasks on every observation-loop entry.
A completed reader fault immediately starts owned failure cleanup. Cleanup sends
SIGTERM, allows the existing one-second grace and kills only the original owned
tree if actual exit remains pending. Five seconds is a failed-cleanup escalation
threshold, never successful settlement: record the timeout, retry the owned kill
when necessary, await actual exit, then close only still-pending owned pipe
handles and await both original readers. Preserve the primary failure before
cleanup failures. Never release process/context/application owners while original
tasks are unsettled or use a detached continuation as settlement. Native tests
cover an already faulted reader while the child remains alive and cancellation
after actual readiness; explicit temporary-directory helpers delete only after
process settlement and preserve deletion failures.

Root joins local image cleanup in TestSuiteApplication only after actual AppHost
stop and producer/runner settlement, under a separate45-second cleanup token,
before output settlement and application disposal. Preparation output is included
only for explicit local mode. Default GitHub image selection and full RF3 gates
remain mandatory.

REQ/AC-TEST-010 also requires enough bounded GitHub job time for all three
sequential unit, scalar and recovery suites plus build/format/preflight. Exact
run37349838022 exhausted the previous30-minute job limit after both unit suites
failed and cancelled recovery. The Linux verify job receives120 minutes; no
individual native suite deadline, required check, artifact or failure predicate
is removed. A failed, timed out or cancelled suite remains failed/unqualified.

TASK-TEST-RF3-AGGREGATE-BUDGET refines REQ/AC-TEST-010 for the separate RF3
job. Run37560457057 at6816ae91 passed full native normal/scalar2767 each and
recovery235 in its verify job. Its RF3 job started02:07:28Z, the native RF3 step
started02:15:19Z and was canceled03:07:42Z near the configured60-minute job
ceiling; no original RF3 TRX was produced. The same job must also execute the
required instrumented unit/scalar/recovery/RF3 cohort and source/artifact checks.
Allow180 minutes for that aggregate job, preserving every individual deadline,
failure/cancellation predicate, required suite and artifact. This budget change
does not make an unfinished or failed run qualified or alter workload bounds.

TASK-TEST-LOCAL-CURRENT-WAVE extends REQ/AC-TEST-015 to current-image fault
waves. R183 rejected the owned local receipt before creating an AppHost because
RequestCqrsRf3ImageProof admitted only GitHub receipts. Reuse the existing
local-development selector, canonical receipt/reference checks, exclusive
GitHub/local environments and native bounded image verifier. One common local
identity read validates actual immutable COPY inputs and daemon identity before
wave composition; the existing pre-start local verifier checks all three model
annotations. Every expected node reference must equal that exact owned image.
ReadAsync selects only the validated reference; VerifyModelAsync returns the
identity from its actual pre-start verifier to a local variable in WaveStartup.
That exact returned identity supplies the post-readiness container checks. Each
pre-start read revalidates the canonical receipt/reference against the expected
map; no ambient cache, changed caller map or synthetic identity carrier applies.
After native start/readiness establishes resource creation, verify each actual
container config ID/reference under the unchanged wave deadline. Keep partial
start, stopped-voter fault, original failure and joined cleanup semantics.
The default GitHub manifest/digest path remains fail closed. Missing, mixed,
stale or forged local inputs reject; local evidence is development-only.
Root owns contract/integration/gates; Luna privately owns LocalRf3ImageIdentity,
RequestCqrsRf3ImageProof and RequestCqrsRf3WaveStartup, with a feature-local
local-image validation helper only if mandatory type limits require it.
That helper is ClusterReplication/Helpers/LocalRf3ImageSelection.cs: it owns
the existing closed selector/environment/reference/receipt validation only.
LocalRf3ImageIdentity retains native verification and model/container proof.
The existing due leader/rejoin/cold-restart and catalog mismatch whole flows
provide real Aspire SDK/MCP regressions; no fixture/model assertion qualifies
their database effects or substitutes for delivered-source Linux execution.

AC-TEST-010 requires original per-suite MTP/TRX evidence from every required
CI suite. Its Aspire caller explicitly enables `KeyLoadTests:ReportTrx=true`;
an omitted report setting, absent report, cancelled suite or failed native
runner cannot establish qualification. Run37303831451's missing full-suite
TRX receipts are retained as an evidence gap, alongside its original artifacts.

## Unified Aspire test entry

TASK-C1-LOGGER-PROVENANCE-NEGATIVE refines the existing DIAG-003/005/006
model-selection regression after the original Stage IV Linux normal/scalar
reports each passed2770/2771 and failed the same exact negative case. The local
RF3 image reader rejects GitHub provenance before C1 logger selection. Freeze
the negative matrix's exact expected rejection per row before changing source:
the deliberately enabled local-image row explicitly supplies GitHub provenance
to its unstarted builder configuration and expects the existing local-image
configuration error on every host; other incompatible rows retain the exact C1
selection error. Keep resource counts unchanged and the owned root absent,
with original failure/cleanup joins. Never clear inherited runner provenance,
mutate process environment, change a production validator, produce an image,
start Docker or broaden the expected-error set. The independent real positive
builder/build/disposal flow remains mandatory.

Existing ADR-082/117 own this options/model-control boundary; product/data/API
changes and new frontend are N/A. A Luna worker owns only the existing
UnitTests TestInfrastructure selection case and rejection helper in a private
guarded packet; root reviews, joins, runs native normal/scalar, commits/pushes
and retains fresh exact-source Linux evidence. This is model-admission evidence,
not database RF3 or a functional coverage contribution. Qualification pending.

TASK-C1-LOGGER-MODEL-CONTROL maps the native logger-control fixture to the
DIAG-003/005/006 contract in
[NativeCqrsRequestV2](ClusterRouting/NativeCqrsRequestV2.md) and ADR-082. Only its
explicit true-only selector composes the authoritative RF3 resource model and
returns after Build before RunAsync. Native logger operations and original task
joins remain real; startup/readiness/Docker/database RF3 are N/A for this control.
The ordinary ClusterFixture and Wave paths remain mandatory RF3 evidence. The
native testing wrapper owns final application disposal after reader settlement;
inherited image/provenance settings are permitted, selected workload modes reject.

`NativeLoggerModelControlSelectionTests` is a UnitTests model-isolation regression for this exact selector. It
exercises the real distributed-application builder, options admission,
`AddKeyLoad` composition, built RF3 model, owned directories, resource
annotations and joined application disposal. Incompatible selections must reject
before resources or the owned DataRoot exist. This case maps to
DIAG-003/005/006 composition isolation only; it does not start resources, observe
logger streams, execute database operations, contribute product functional
coverage, or qualify Docker/RF3. The unchanged native logger whole flows and
separate RF3 gates remain mandatory.

The rejection matrix also covers `Ephemeral=false` and a blank DataRoot. For
all rejected selections, observe the unchanged resource count and absent root
before cleanup. Preserve the primary admission/assertion failure, delete only the
unique root in `finally` if an unexpected failure created it, retain deletion
failures after the primary, then verify the root is absent. This test-only
refinement changes neither selector behavior nor product cleanup.

[ADR-074](../ADR/ADR-074-aspire-owned-test-entry.md) specifies the owner-required
single test entry and partial-start cleanup. Source and qualification are pending.

| Requirement | Acceptance | Tests / evidence |
|---|---|---|
| REQ-TEST-009: Aspire owns suite execution | AC-TEST-009: each closed suite creates one native executable with correct TUnit project/arguments/results and optional original TRX/Cobertura settings; scalar disablement applies to its runner; unknown/explicit-empty suites, invalid bounded paths, incomplete coverage settings and mixed benchmark modes reject before resources; only comparison may pass its existing native target to the child without composing outer database resources | AspireTestEntryModelTests using the actual Aspire builder; original entry-run reports |
| REQ-TEST-010: test entry preserves outcomes and bounded lifetime | AC-TEST-010: native nonzero/failed-start/missing-exit/cancel/timeout cannot count as success; stop/disposal always run; every existing CI suite and artifact remains | root source lifetime review and actual Linux CI native runner exit/status evidence |
| REQ-TEST-011: RF3 is owned once and startup failure releases resources | AC-TEST-011: outer test model contains no idle duplicate nodes; tested child AppHost owns three Docker nodes and discovered SDK/MCP endpoints; every partial start is disposed before deleting only its owned directory | real Aspire model tests, ClusterFixture source lifetime review, actual Docker RF3/recovery artifacts |

Canonical source: AppHost Features/TestInfrastructure; actual model regressions:
ComparisonTests Features/BenchmarkComparisons/UnitContracts; functional infrastructure tests remain in UnitTests Features/TestInfrastructure. Shared IntegrationTests fixture and
CI composition are root-owned joins. No public database or persisted format change.
Local entry runs are development evidence; delivered-source Linux gates remain.

## Explicit local RF3 image preparation

REQ-TEST-015 / AC-TEST-015 extends [ADR-074](../ADR/ADR-074-aspire-owned-test-entry.md)
with an explicit `KeyLoadTests:LocalRf3Image:Enabled=true` development mode,
accepted only with `Suite=rf3` and an explicit nonblank bounded development filter.
Following the owner's native-entry correction in ADR-117, image preparation is
bounded prerequisite work for the native TUnit fixtures; it cannot launch an
outer AppHost test runner or execute database workloads.
The tested child AppHost owns exactly three Docker nodes, readiness, discovered
SDK/MCP endpoints, scoped faults and complete shutdown; no idle outer cluster is
created. Unknown, non-RF3, protocol-cohort or mixed GitHub/local modes reject before
resources. Missing image configuration continues to fail closed in the default mode.

The producer builds the repository server Dockerfile using its actual effective
COPY inputs, names, modes and bytes, `.dockerignore` and pinned base digests. It
first creates an invocation-owned immutable build-context snapshot of exactly
those inputs and builds Docker from that snapshot. It validates the two context
COPY declarations separately from the exact internal `COPY --from=build` stage
copy; an unsupported Dockerfile or ignore declaration rejects before building.
Admission charges at most 20,000 input files and 512 MiB of aggregate bytes before
reading or retaining their contents; hashing and copying stream one admitted file
at a time. The captured snapshot digest binds the actual Docker build input,
including modes; before/after hashes of a mutable checkout alone are insufficient.
The producer records a bounded canonical input fingerprint, unique invocation tag, actual local
Docker image config ID and a custom input label in a separate `local-development`
receipt. A dirty working tree is identified by these inputs, never a fabricated
GitHub revision. The config ID is not a registry manifest digest. Before startup,
the child verifies receipt schema/bounds, current input fingerprint, actual image
ID/label and all three modeled tags. After startup it verifies each owned container's
actual image config ID. StartAsync schedules resources asynchronously; await the
existing native three-resource healthy readiness within the unchanged two-minute
fixture startup token before inspecting their actual config IDs. Both local-image
and native coverage started-image verification follow that readiness, and all
public SDK/MCP operations remain after verification. Do not treat host StartAsync
return as proof that Docker has created containers or add a separate retry/deadline.
Missing, oversized or malformed receipts, changed inputs,
wrong tags/labels/IDs and conflicting proof selectors fail before database effects.

Build failure prevents runner execution. Every failure/cancellation joins the
owned producer, runner and child resources. Image cleanup occurs only after owned
nodes stop, targets only this invocation's unique tag and rechecks image ID/label
before removal; no force deletion, pruning or unrelated-resource cleanup.
Cleanup validates the immutable owned receipt, tag, label, image ID and absence
of every running or stopped container using that image; a subsequent checkout
edit does not prevent cleanup of its proven owned tag. Current checkout input
validation remains mandatory before database startup. Cancellation, output-limit
failure and timeout signal only the owned native process, escalate within a
bounded grace period and join its actual exit and both original stream readers.
The context snapshot is removed after producer settlement; receipts and a bounded
original Docker build-output tail remain available for diagnosing failure.
Retain the original development receipt and native test reports. Local results cannot
qualify delivered-source Linux CI, registry image provenance or website metrics;
the existing GitHub image producer and verifiers remain mandatory and unchanged.

TASK-TEST-LOCAL-CLEANUP-COMMAND refines the same REQ/AC-TEST-015 owner contract.
`local-image-process.mjs` already selects the Docker executable; the owned-image
container query in `local-server-image.mjs` must pass only `ps` and its bounded
arguments, never a second executable token. Root repairs that command after the
actual R149 image's canonical cleanup failed. Verify the full native owned-image
prepare, identity verification, cleanup and repeated-absence cleanup operation.
Keep receipt/label/config-ID checks, zero running or stopped referencing
containers, non-force removal and all original process/reader joins. This local
tooling operation does not qualify database RF3, Linux delivery or coverage.

TASK-TEST-LOCAL-CONTEXT-REALIZATION implements this existing REQ/AC-TEST-015
snapshot and cleanup contract. Root freezes and owns the joins; unpack_atomicity
Luna owns only `scripts/Features/TestInfrastructure/local-image-context.mjs`,
the new cohesive `local-image-snapshot.mjs`, `local-server-image.mjs` and
`tests/KeyLoad.UnitTests/Features/TestInfrastructure/Cases/LocalRf3ImageContextSnapshotTests.cs`.
The reviewed R2 correction is assigned to ci_failure_evidence Luna and adds only
`scripts/Features/TestInfrastructure/local-image-process.mjs` and the cohesive
`UnitTests/Features/TestInfrastructure/Processes/LocalImageSnapshotProcess.cs`.
Admit each metadata entry and its bytes before retaining it; use bounded directory
enumeration rather than an unbounded readdir array. Track exclusive staging
ancestors for standalone COPY files separately from the unchanged canonical
digest entries. The Node regression includes that real COPY path and an actual
over-limit rejection followed by unchanged input and healthy capture.
Build-output retention uses the existing65,536-byte per-stream cap and a separate
invocation-owned `<receiptPath>.build-output.json` file with only original stdout
and stderr tail bytes encoded as base64. Write it exclusively after the original
producer and both readers settle, on success or failure, before snapshot cleanup;
retain its write failure alongside the original failure. No receipt field changes.
Only Docker build uses tail retention; introspection keeps its strict output-limit
rejection. A reader fault or signal immediately terminates only the owned child,
arms the existing kill grace and joins all original work while preserving failures.
The C# Node fixture has a60-second operation deadline,8-KiB drained output bounds,
owned-child termination and original reader/exit joining. Preserve the operation
failure with every cleanup failure; attempt both snapshots and parent cleanup.
The C# caller creates the private unique filesystem root, supplies it to Node,
and removes only that owned root after original process/readers join, including
timeout and reader-failure paths where Node cannot reach its own finally.
First admit the exact existing input metadata and limits, then stream admitted
regular files into an exclusive invocation context with no-follow/stat checks,
preserving paths and file/directory modes. Hash the bytes actually copied; retain
no unbounded array of input buffers. Build Docker only from this context, join
the original producer and readers, and remove only the owned snapshot after
settlement. Image cleanup uses original receipt/daemon identity rather than
mutable checkout bytes; startup verification still rejects changed current inputs.
The real TUnit/Node filesystem flow rejects a symlink without publishing a partial
snapshot, creates the bounded context, changes the original input, verifies the
frozen copy/digest/modes, cleans it and successfully repeats the operation. No
fake Docker, source-text tests or bound change. Root then prepares an actual
image and verifies real Aspire RF3 clients/cleanup; filesystem proof alone cannot
qualify Docker or Linux delivery. Dependencies, receipt schema, default GitHub
path and database contracts remain unchanged. Rollback removes this helper/join
as one unit and cannot admit a mutable-context build as compliant.

TASK-TEST-LOCAL-RF3-CONTRACT (root) precedes TASK-TEST-LOCAL-RF3-IMAGE
(dependency_closeout). The worker owns AppHost Features/TestInfrastructure local
settings, prerequisite and cleanup; ClusterReplication/Resources local image
selection; scripts/Features/TestInfrastructure bounded producer; separate
IntegrationTests ClusterReplication local verification and native AppHost model
regressions. Request admission belongs to Validation and invocation construction
to Execution; obsolete executable copies under Models are removed. Native
SIGTERM/kill helpers use generated LibraryImport with AllowUnsafeBlocks enabled
only in KeyLoad.AppHost.csproj and KeyLoad.IntegrationTests.csproj. This does not
enable unsafe code solution-wide or suppress analyzer diagnostics. Root owns
shared ClusterFixture/TestSuiteResources/TestSuiteApplication composition joins,
review, build, Aspire runtime evidence and delivery. Tests use the actual Aspire
builder to prove dependency ordering and absence of outer nodes; parser/input
tests cover negative cells without pretending to prove Docker creation. Real
Docker/Aspire RF3 SDK and official MCP evidence must prove image identity, three
nodes and cleanup. Frontend, production data/API changes and local comparison
mode are N/A for this infrastructure stage. Current-image proof remains
mandatory; local image preparation never skips or substitutes current-format
recovery, SDK/MCP or RF3 cases. Original acceptance remains open until its
complete required qualification exists.

### TASK-TI-RF3-IMAGE-MODE-011: preserve the default CI image path

REQ/AC-TEST-011/015 retains the two existing image-proof modes. Exact Linux
CI37462761672 on e6295 recorded121 pre-start failures because the local-only
IntegrationTests helper treated the shared server image reference as an explicit
local selector. A server reference also belongs to the default authenticated
GitHub mode and cannot select local provenance by itself.

LocalRf3ImageIdentity enters its local verification only when a local provenance
or local receipt selector is present. With both absent it leaves the default
path to the existing strict RuntimeContainerImage parsing, exact-source GitHub
receipt and modeled image checks. Preserve every local provenance/receipt/tag/
input/config-ID/membership check and reject incomplete or conflicting local
selectors. Do not add fallback proof, synthesize a receipt, accept mutable images
or change the actual Docker/Aspire RF3 topology.

Root owns this single helper predicate and its integration with the existing
ClusterFixture. Existing genuine default-CI and explicit-local RF3 operation
flows provide the positive/negative evidence; source review alone does not qualify
either mode. Renew canonical build/format and exact-source Linux SDK/official MCP
RF3 with original reports. ADR-074's existing ownership and proof contracts remain
unchanged; rollback restores only this test-harness predicate, retaining failures.

## Prompt termination on owned Aspire failure

[ADR-086](../ADR/ADR-086-aspire-terminal-failure.md) defines the private lifecycle
repair prompted by CI [37202347856](https://github.com/managedcode/KeyLoad/actions/runs/37202347856).
Its original reports show RF3 87/88 passed (one authorization assertion)
and unit 2947/2953 passed (six native CQRS cases); they do not establish an AppHost
crash. Website tests were 201/202 passed, with a real Chrome node-selection
assertion failure. Dashboard-disabled CLI warnings appeared before successful
suites too. These facts come from the original RF3, unit and site TUnit artifacts
at source `15ea5030c6fc010b29127b4b8de07bd255afc7ff`, not inferred CLI text.

| Requirement | Acceptance and evidence |
|---|---|
| REQ-TEST-012: owned terminal Aspire failures stop waiting promptly | AC-TEST-012: a runner's FailedToStart, RuntimeUnhealthy, Finished/Exited without an original exit code, or a required long-lived dependency's terminal state fails its workload without consuming the suite deadline; genuine native notification tests settle within five seconds of publication |
| REQ-TEST-013: completion and planned fault contracts remain intact | AC-TEST-013: expected WaitForCompletion exit is allowed; nonzero/missing completion exit fails; healthy/running and transient health snapshots continue waiting; no dependencies remains valid; guards are canceled and joined before intentional comparison node kill/restart assertions |
| REQ-TEST-014: original outcome, ownership and cleanup remain intact | AC-TEST-014: native runner exit codes, startup exceptions and cancellation remain failures where applicable; AppHost stopping cancels waits; every notification task is canceled/joined, existing diagnostics and stop/disposal remain; unrelated database cells are unaffected |

TASK-TEST-FAILFAST-CONTRACT (root, complete): requirements/ADR and owning source
review. TASK-TEST-FAILFAST-NATIVE (progress_runner, locally verified): only new
AppHost Features/TestInfrastructure lifecycle helpers and ComparisonTests
Features/TestInfrastructure native notification regressions. TASK-TEST-FAILFAST-JOIN
(root, after helper): TestSuiteApplication and IsolatedNativeCase integration,
CI focused selection, docs, review and exact-source validation. vector_runtime
owns read-only lifecycle review. These tasks preserve concurrent engine changes;
RF3 CQRS/authorization failures belong to their existing owner. Public database,
stored data, dependencies and topology are N/A: this is private test orchestration.
Native service tests establish notification handling, not real Docker failure
qualification; delivered Linux tests and failure artifact review remain required.

Local development evidence, 2026-10-04: the complete Release solution build has
zero warnings/errors; the full required formatter and repository governance pass.
The canonical Aspire comparison entry with `/*/*/AspireFailure*/*` reports
36/36 TUnit cases passed in 3.017 seconds, with no skipped, cancelled, timed-out
or flaky cases. The retained report is
`TestResults/aspire-failure-local3/KeyLoad.ComparisonTests-macos-net10.0.tunit-report.json`.
The existing `AspireTestEntry*` model/artifact regressions also pass 5/5 through
the same entry; their original report remains in `TestResults/aspire-failure-entry/`.
This includes genuine DCP missing-executable startup failure and native
BeforeStart failure/cancellation, preserving the original outcome and joining
tasks before cleanup. The no-lifetime case uses Aspire's real custom-resource
extension seam, `Resource, IResourceWithoutLifetime`, and native notifications;
neither native parameters nor certificate collections implement that marker in
the pinned 13.6 runtime. No database/service substitute is used. These checks
ran on macOS against the current working tree, which also contains concurrent
engine changes; they do not qualify an exact delivered Linux SHA or its complete
unit, recovery, RF3 and benchmark suites.

Status: implementation in progress. Owner: KeyLoad lead. Decision: [ADR-036](../ADR/ADR-036-orleans-foundation.md).

REQ-TEST-008 / AC-TEST-008 (TASK-RUNTIME-EVENTSOURCE-W3) coordinates the actual
global Microsoft logging EventSource across the two diagnostic suites. Listener
disposal issues a Disable command; LoggingEventSource disables its global filter
state even when another capture remains active. Exact fa80c701 Windows CI misses
ToolsCall midway through KnownMethodCategoryIsClosed. Use a single named native
TUnit NotInParallel key on McpTransportDiagnosticsTests and
GrainFailureDiagnosticsTests, declared once in a TestInfrastructure constant.
Preserve all native provider/capture/filter and privacy/error/category assertions;
no fake logging, sleeps, retries or production diagnostic change. Existing failing
category/privacy cases and all grain diagnostics are the regression matrix, with
full multi-OS GitHub execution required. Root owns this small cross-slice test
scheduling join because the shared global resource has one integration owner.
ADR039/036 existing contracts suffice; no product/API/data migration applies.

| Requirement | Acceptance and observable evidence |
|---|---|
| REQ-TEST-001: TUnit and Microsoft.Testing.Platform are the sole .NET test framework/runner. | AC-TEST-001: all test projects compile and run with TUnit; no xUnit/VSTest package or attribute remains; migrated assertions preserve every original scenario. |
| REQ-TEST-002: real RF3 tests launch Docker containers under Aspire and invoke the real .NET SDK and official MCP SDK clients. | AC-TEST-002: CI captures three independent containers, stable persisted directories, SDK/MCP success/denial flows and replica failover/rejoin. |
| REQ-TEST-003: generated lock files, local data, secrets, logs and test artifacts do not enter Git or the Docker context. | AC-TEST-003: tracked-file inventory and ignore checks reject these outputs; central NuGet versions remain pinned. |

```mermaid
flowchart LR
    CI[GitHub Actions] --> TUnit[TUnit and MTP]
    TUnit --> Aspire[Aspire lifecycle]
    Aspire --> Docker[Three Orleans containers]
    SDK[Real .NET and MCP clients] --> Docker
    TUnit --> Evidence[Run and artifact evidence]
```

Slice map: tests mirror their product slice; shared fixture/lifecycle infrastructure remains at test project roots. Shared CI/.gitignore/.dockerignore are integration-owned infrastructure. Production data/API/GUI: N/A, this framework migration adds no domain behavior. The Orleans/Docker boundary changes are ClusterReplication and ClusterRouting. Blobs and MCP coverage must not be reported complete from framework migration alone.

Traceability: AC-TEST-001 maps to all invoking test projects, AC-TEST-002 to IntegrationTests and ComparisonTests, AC-TEST-003 to source/ignore checks. TASK-TEST-MIGRATE and TASK-REP-VERIFY are in the execution plan. Tests run only in GitHub Actions; development builds are compilation evidence. Coverage and complexity policy cannot be claimed satisfied without measured configured gates.

TASK-RUNTIME-ARTIFACTS-W retains actual root TestResults reports from native TUnit
alongside each existing scoped report glob under REQ/AC-TEST-001/002/005 and
AC-MP-012. Run37005805424 proves the missing RF3/analyzer report retention path.
Comparison test reports use a separate comparison-test-results artifact so the
measured comparison-suite archive keeps its existing contract. The lead owns
ci.yml; no test, timeout, gate, permission, measured-report schema or ignore rule
changes. Source review and downloaded complete reports at the new exact run/job
SHA are required evidence. The artifact-path repair itself has a manual exact-CI
verification exception; it cannot convert failing or unexecuted tests to success.

TASK-RUNTIME-COMPARISON-DIAGNOSTICS-W is a failure-only refinement of
REQ/AC-TEST-002/005 and AC-MP-012 after run37005805424. Before StartAsync,
passively retain at most 128 lifecycle records for the comparison runner and its
eight direct wait resources. Only fixed resource names, native timestamps,
local observation sequence, closed state/health categories and exit code are
retained. Snapshot version/readiness are internal in the pinned native package
and remain unavailable; do not infer or reflect them. No properties,
environments, endpoints, connection strings, health
descriptions, exception text or payloads. On the existing cancellation/timeout
path, emit at most 80 lines/8KiB to test-runner stderr, preserving the original
failure even if observation or output fails. Keep this diagnostic out of measured
comparison archives and retain the eight-minute timeout and existing terminal
predicate. The worker owns only a new ComparisonResourceDiagnostics helper;
the lead owns the RealComparisonSuite join and shared documentation. The closed
receipt formatter may be a second helper to preserve type limits; lead extracts
existing evidence-directory/report-copy logic to ComparisonTestEvidenceFiles.cs
without changing path resolution or report bytes. Source privacy,
memory/task-lifetime review plus actual exact-SHA GitHub resource events are the
verification exception for an environmental failure path; no synthetic provider,
local AppHost execution or successful-workload claim is allowed.

REQ-TEST-006 / AC-TEST-006: real shared fixture ownership transfers only after
successful setup. Failure after opening a store releases it before deleting its
owned fresh directory; the same path can reopen without a leaked lock.
Existing supplied directories are rejected before acquisition or mutation, with
their files and real active owner preserved; finally cleanup follows an attempted
store disposal even when an environmental disposal failure throws. Admitted
background work is released/cancelled/observed before database disposal on every
path. CQ016's accepted contract/task graph and ADR033/032 govern source-only
changes: lead owns TestDatabase and new TestInfrastructure/FixtureLifetimeTests;
workers own real EventStreams/Messaging setup regressions and analytical task
cleanup. The lead also owns RealZoneTreeReadGateHold and the new canonical
ResourceExecution/ReadGateLifetimeTests. Original ten-second admitted-search
timeouts begin after cancellation/release attempts; final observation always
awaits actual workers and the actual gate holder before store disposal. Real
immediate/entered and repeated gate disposal cases assert subsequent real-store
usability; environmental timeout/fault branches require the supplemental source
audit rather than a synthetic injector. Actual invalid limits/event counts/batch configuration and same-path
ZoneTree reopen are independent GitHub TUnit assertions. Environmental cleanup/
coordination failures additionally require full source lifetime review; no fake
or local qualification. All existing132 methods/19 migrated roots stay preserved.

REQ-TEST-007 / AC-TEST-007 (TASK-RUNTIME-RECEIPTS-W) preserves failure evidence
from run37015193756 without making failed gates pass. Save the existing bounded
RF3 diagnostic text both as the last-failure file and as a sequential fixture-owned
receipt; cap retained individual files at32, keeping the earliest failures and
unchanged per-file privacy/line/byte caps. Bounded filenames contain only the
sequence. Actual GitHub artifacts must retain the first replica failure before
later MCP failures overwrite the last-failure view.

On comparison cleanup, stop the actual application, copy any existing complete
report files using the existing helper, then delete owned temporary data. Native
terminal state, zero exit, deadlines and all existing assertions remain required;
retained report bytes are partial evidence when the test fails. Recovery runs on
each matrix OS after successful build even if a preceding unit/style/analyzer
step fails, with normal job failure preserved and no continue-on-error. A failed
build or cancellation prevents recovery. The lead owns ci.yml, RF3 receipt helper
and RealComparisonSuite cleanup join. ADR036/035 govern the ordered source-only
rollback. Actual temporary-file IntegrationTests verify first/last receipt
preservation, the32-file ceiling and concurrent whole-file writes; source privacy
and exact-SHA GitHub first-failure/report/recovery artifacts supplement those
assertions. Environmental watch/runner failures
must not be replaced by mocks or counted as green qualification.

TASK-RUNTIME-RESTART-RECEIPT-W4 refines REQ/AC-TEST-007 after exact main
b533c80 / CI37032546228. The failed native restart attempt is currently missing
its container generation: the first receipt shows the killed old node and later
receipts show a successful restoration. Capture the public Aspire current
ResourceId, closed state/health, exit code and creation/start/stop timestamps
before Start, after its successful command and immediately after an existing
restart failure. These are sampled snapshots, not a native snapshot version or
proof of the transition which caused the failure. Native Version and readiness
internals are unavailable and must not be reflected or inferred.

The failure receipt includes the existing verified pre-kill Docker ID/start time,
closed failure stage/type, Start success and one immediate read-only Docker
inspection of ID/status/exit/OOM/start/finish/error-present. No Docker error text,
environment, properties, endpoints, credentials, arbitrary method/type text or
payload is included. Only validated native identifiers and timestamps are
retained; unknown states/types become a fixed unavailable/other marker. At most
32 earliest restart receipts plus one latest view are retained, with80-line/8KiB
UTF-8 bounds and filenames containing only the local sequence. The diagnostic
has its own three-second native-inspection deadline after the original failure.
Every path after process creation owns termination, reaping and observation of
both redirected readers, including setup, wait and reader faults. Mandatory
cleanup can extend wall time if operating-system termination is delayed; no
hard end-to-end three-second return or detached process/reader claim is allowed.
Termination checks an exit race and retries a failed live-child tree kill once;
a final live-child kill error is retained through mandatory task joining. If
the operating system permanently refuses termination, finite cleanup cannot be
guaranteed. This environmental branch needs native CI evidence or source review,
never a synthetic process failure or a passing lifecycle claim.
It releases/awaits its actual CLI process and redirected readers on cancellation,
then rethrows the original restart exception even if diagnostic inspection or
file writing fails. Existing synchronous whole-file persistence keeps its
80-line/8KiB limit; no hard three-second filesystem-latency claim or detached
writer task is allowed.
No Start, health, runtime identity, zero-exit, cancellation, timeout, retry count,
quorum, atomicity, data or cleanup assertion changes.

Root owns ContainerRuntimeControl and the closed receipt-kind join in
ClusterFailureReceipts; one disjoint worker owns only new ClusterReplication
failure-capture/projection/CLI-lifetime helpers and real temporary-file tests.
Tests first prove only selected native public snapshot fields survive with
canary properties/env/URL/state/identifier/type rejected, whole valid bounded
files preserve the first failed generation across later receipts, and32-file
retention remains exact. Native-shaped Docker records cover valid identities,
safe error-presence projection and malformed ID/state/exit/OOM/timestamp fields;
actual stream readers prove output is drained after its retained prefix is full.
Do not fake native services, Docker or process failures.
The original RF3 leader-loss/minority scenario remains the real lifecycle test;
an environmental failure branch additionally requires exact-SHA GitHub artifact
review and source lifetime/predicate comparison. If the next run succeeds, do
not claim its unexecuted failure path qualified. ADR-035/036/039 existing
privacy/lifetime contracts suffice; no product/public/dependency boundary changes.
Rollback reverts only this additive diagnostic join and its helpers/tests.

## Accepted comparison-host startup diagnostics

AC-REC-FUP-004 records the preserving W6 fixture follow-up: the exact delivered
SHA, complete GitHub job/report artifacts, native failure identities and report
hashes remain in the runtime ledger. Unit/Recovery/RF3/Comparison must all execute;
failed or unexecuted cases cannot count as green. Local development build,
formatter and governance are source validation only. Full native artifact review
and source lifetime/predicate audit cover environmental branches which cannot be
injected without prohibited service doubles. StorageRecovery owns AC-REC-FUP-003;
ClusterReplication owns AC-REC-FUP-001/002. Existing ADR035/036/033 apply.

REQ-TEST-009 / AC-TEST-009 (TASK-RUNTIME-HOST-DIAGNOSTICS-W5) refines the
private real-process AC-HOST-003/007 fixture after exact8b475d4 /
CI37042082081 repeats four Windows20-second failures. One existing deadline
covers both native exit and redirected-output drain; current failure evidence
does not distinguish them. Source configuration order is not a diagnosis.

Before the existing timeout cleanup, retain only a closed ProcessExit/OutputDrain
stage, actual available exit state/code, closed capture-task states, capped
capture lengths and boolean matches for existing static validation markers.
The receipt is at most8KiB and keeps the original TimeoutException message
prefix. No raw child output, exception text, arguments, environment, paths,
endpoints, credentials or unknown values are logged. Diagnostic observation
failure becomes a fixed unavailable marker and cannot replace the original
startup timeout or caller cancellation.

Each actual StreamReader still uses4096-character reads, retains32768 characters
maximum and drains the remainder. Synchronize partial-buffer observation; retain
the same20-second shared startup deadline and five-second cleanup behavior.
Do not add retries, serialization, sleeps, client/config reorder, product changes
or new detached work. Preserve all four configuration/ordering assertions and
the real cleanup test. Environmental failure/cancellation additionally needs
source lifetime review and the actual GitHub failure receipt; a successful run
does not qualify its unexecuted failure branch.

```mermaid
flowchart LR
    Host[Actual CLI child] --> Exit[Native exit wait]
    Host --> Capture[Bounded readers drain to EOF]
    Exit --> Drain[Capture completion wait]
    Exit --> Failure[Existing startup timeout]
    Drain --> Failure
    Failure --> Receipt[Closed stage and pre-cleanup facts]
    Receipt --> Cleanup[Existing owned cleanup]
```

Slice ownership: one worker owns only ComparisonTests/Features/BenchmarkComparisons/
UnitContracts/Processes/ComparisonHostProcess.cs and new cohesive capture/diagnostic/test helpers;
root owns shared documentation and integration. New genuine native-shaped
projection/privacy and real-stream prefix/EOF cases map AC-TEST-009.1–003;
the existing real host cases map009.4. Exact delivered-SHA full GitHub
unit/recovery/RF3/comparison plus native receipt review map009.5. All tests
remain TUnit/MTP and CI-only. Existing ADR035/043 private fixture ownership,
privacy and lifetime contracts suffice; no public/data/dependency/deployment
migration is authorized. Rollback reverts only this additive private diagnostic
packet and its source docs; coverage and full goal remain unqualified.

## Platform, dependency та release qualification

### Central native TUnit admission

REQ-TEST-016 / AC-TEST-016 (TASK-GENERAL-OPTIONS-TEST-ADMISSION-001) requires
every Aspire-owned TUnit runner to receive its native `--maximum-parallel-tests`
argument from centrally bound and validated `IOptions<TestExecutionOptions>`.
`KeyLoadTests:Execution:MaximumParallelTests` defaults to20 for ordinary functional
tests and accepts only1–50. Comparison selections require1 and reject a larger
explicit value before startup under the owner clarification2026-10-09 in
[NativeTUnitEntry](TestInfrastructure/NativeTUnitEntry.md#functional-concurrency-and-exclusive-measurements-2026-10-09).
zero/unlimited, negative and above-ceiling values fail before resources are added.
The admission history includes the original
full unit v34 run, which completed4061/4133 passing, with four readiness deadline failures
and native cluster startup cancellation while other compiled workloads were
active. Those observations do not establish a defect in production locks or prove
that this admission policy resolves every failure. Preserve each test's own worker
count, workload, deadline and assertions, all required suites and original exit
reports. The setting must flow to the actual runner command, including configured
non-default values; inherited TUnit environment variables cannot override that
validated command argument. This does not cap GitHub benchmark matrices or change
measurement workloads. Heavy functional load/mixed-ingestion cases require a
separate exclusive selection and stay outside coverage; their real complete-flow
qualification remains open under REQ/AC-TUNIT-ENTRY-014.

Native Aspire model tests map default/configured forwarding, boundary and malformed
configuration rejection to AC-TEST-016. Full native unit/scalar/recovery/RF3 and
exact-source Linux reports remain required. [ADR-113](../ADR/ADR-113-centralized-runtime-options.md)
owns the central options standard; root owns this contract, the AppHost contributor
owns Configuration/Execution/Validation/Hosting propagation and its focused model
regressions. Rollback reverts only that setting, propagation and test addition,
without changing existing runner lifetime, cleanup, suite or outcome contracts.

B3 keeps the exact task lifetime above while satisfying enabled CA1031: a private
throwing AggregateException wrapper preserves each original exception object;
the collector handles only that wrapper and adds direct inner errors. It never
flattens or drops an original empty aggregate. Raw-worker awaits retain only the
exact expected cancellation exception. CQ016's accepted B3 contract and graph
govern source review and genuine GitHub admission/cancellation regressions;
environmental error branches retain the explicit full-source audit supplement.

Актори: contributor, CI runner, release owner і evidence consumer. Source/config entry points: [canonical CI](../../.github/workflows/build-and-tests.yml), [global.json](../../global.json), [central packages](../../Directory.Packages.props), dependency survey (report removed from repository), [qualification status](../implementation/status.json). Product runtime N/A: infrastructure запускає та перевіряє справжній продукт, не підміняє його demo engine.

| Вимога | Acceptance / flows | Test / evidence mapping |
|---|---|---|
| REQ-TEST-004: versions/licenses/native dependencies та platforms зафіксовані для delivered source | AC-TEST-004: centrally pinned package/image inventory і source provenance відповідають actual restore/container; required platform jobs pass, incompatible/missing/native/license requirement explicit fail; free comparison engines не потребують Enterprise | Owner-selected Linux-only GitHub qualification; macOS local runs remain development evidence. The 2026-10-07 local inventory has50 central pins,27 project asset files and264 package/version pairs. The fresh R239 isolated restore selects all264 archives from NuGet.org; the VFS10.0.11 archive matches the exact official catalog SHA512/repository commit and the K4os1.3.8 source license is bound to its immutable release commit. There are0 unresolved package/license inventory rows. Restore and inventory alone do not qualify the native build or runtime. Original Linux receipts match central inputs but do not retain complete package-graph/archive identity. Clean delivered-source restore/native/platform qualification remains pending. |
| REQ-TEST-005: release manifest рекламує тільки перевірені capability/guarantee gates | AC-TEST-005: all required build/analyze/format/TUnit/recovery/RF3 SDK/MCP та configured coverage/complexity pass на exact delivered SHA; failures/unsupported/unconfigured remain explicit unavailable; power-loss/endurance/fault gates окремі, stable release withheld до потрібних доказів | PLANNED consolidated exact SHA/run/job/artifact release evidence для KL-041/044/080/104; [CodeQuality](CodeQuality.md), [BenchmarkComparisons](BenchmarkComparisons.md), [ResourceExecution](ResourceExecution.md) owning checks |

Negative/edge/error flows: skipped suite не passing; flaky case — failure; malformed/missing artifact, mismatched SHA/profile, unavailable engine/license/platform або test resource startup failure не замінює previous qualified history. Old CI не кваліфікує uncommitted code; test method name не є result. [ADR-031](../ADR/ADR-031-modular-all-in-one-resource-isolation.md) і [ADR-036 foundation](../ADR/ADR-036-orleans-foundation.md) фіксують topology/capability scope.

TASK-TEST-PUBLISHED-PACKAGE-IDENTITY repairs the local qualification inputs before
further native verification. Preserve the shared global cache and its original
receipts; use a task-private package directory and the unchanged repository
NuGet.Config/central pins for canonical restore. AC-TEST-PACKAGE-001 requires the
actual restored VFS10.0.11 archive to match the authoritative exact-version NuGet
catalog SHA512 and repository commit, with refreshed asset content hashes and no
local feed/project reference. Rebuild and rerun affected native whole-operation
regressions from those published inputs. A mismatch or missing package fails;
earlier local receipts remain explicitly unqualified for published dependency
identity. This changes development build inputs only; runtime contracts,
licensing decisions and dependencies remain unchanged under ADR-107/117.

Target maps: shared AppHost/CrashHost/fixtures/workflows — composition/infrastructure; business cases mirror owning canonical `Features/<SliceName>/`, TUnit/MTP — єдиний .NET framework/runner. Frontend N/A; real first-render website evidence належить BenchmarkComparisons. One integration owner owns central project/CI/resource graph; bounded test workers зберігають every assertion та join з real GitHub evidence. Нові doc files не запускають локальний продукт і не послаблюють жоден gate.
# Build and Tests boundary, owner correction 2026-10-06

REQ-TEST-PIPELINE-001: Build and Tests runs solution builds, repository checks and
KeyLoad functional analyzer/unit/scalar/recovery/RF3 tests only. Benchmark tests
are compiled in the separate KeyLoad.ComparisonTests project and executed only
by Benchmarks, including progress, entry and counter regressions.

AC-TEST-PIPELINE-001: the unit assembly discovers no BenchmarkComparisons cases;
the comparison assembly retains their test inventory and assertions; Build and
Tests invokes no comparison suite or benchmark load-generator preparation.
Benchmarks executes the transferred contracts in normal and scalar modes and
retains original TRX reports. Static workflow/project inventory review is the
explicit infrastructure-placement evidence exception; actual Aspire/TUnit
discovery and original reports verify the runtime boundary.

TASK-TEST-PIPELINE-001 moves benchmark cases and exclusive helpers;
TASK-TEST-PIPELINE-002 updates workflow callers and server-only RF3 image
preparation; TASK-TEST-PIPELINE-003 verifies compilation, discovery, unchanged
assertions, formatting and governance. The implementation contract is
[ADR-074](../ADR/ADR-074-aspire-owned-test-entry.md). No database API, persisted
format, replication or authorization contract changes.

TASK-TEST-PIPELINE-EXCLUSIVE-HELPERS completes REQ/AC-TEST-PIPELINE-001 by moving
the exclusively benchmark-consumed `TestOrchestrationConfigurationKeys.cs` and
`WorkflowDatabaseGroups.cs` into ComparisonTests
`Features/BenchmarkComparisons/UnitContracts/Contracts/` and `UnitContracts/Models/`.
Preserve their complete bytes/namespaces; remove only their two comparison-project
Compile links. `TestElapsedClock` keeps its one shared source because functional
unit cases use it. ADR-074 fixes source ownership and root/Luna join roles; source
byte/hash/reference review plus the existing canonical build/discovery gates
verify this structural continuation without artificial structure-only tests.

TASK-TEST-TUNIT-LOCAL-MEMBERSHIP implements the REQ/AC-TEST-015 prerequisite
ownership gap under ADR-117. [ADR-119](../ADR/ADR-119-tunit-owned-local-membership-image.md)
freezes the explicit native selector, per-case TUnit-owned preparation, typed
selection without global environment mutation, six-name model/runtime proof and
joined exact-tag cleanup after all database resources/locks settle. Traceability
is REQ/AC-TUNIT-ENTRY-005 to the actual six-silo membership case and native
selection/model rejection whole flows. Implementation/runtime qualification is
pending; a pre-existing external receipt is not end-to-end ownership evidence.

## Owned preparation failure diagnostics

TASK-TUNIT-LOCAL-PREPARATION-DIAGNOSTICS-008 refines REQ/AC-TEST-007/010/014/015
and REQ/AC-TUNIT-ENTRY-006 after R312 failed eight selected cases during
preparation startup. Before Build, the session registers a passive native host
logger retaining only closed category/severity/event ID/exception kind; it
never formats messages, exception text, properties, environments or endpoints.
Observe actual prerequisite notifications before Start, retain at most128
records, and emit an80-line/8192-byte failure receipt before teardown. Include
caller-token and ApplicationStopping states and actual prerequisite state/exit.
These identify cancellation provenance; an absent original cause remains
unobserved. Prime the original TestSuiteOutput subscription from actual current
resource state, then keep its existing native stream and joined cleanup. The
producer retains its original bounded build-output sidecar under its existing
contract; neither an absent stream nor a host-stop flag proves a Docker cause.
Preserve primary, diagnostic and cleanup failures together. No deadline,
selector, topology, native provider, success predicate or qualification gate
changes. Sol owns private cohesive ClusterReplication diagnostics/session and
AppHost TestInfrastructure output priming; root owns joins and native validation.

Verification requires the original failed preparation receipt and resource logs,
unchanged failure/cleanup settlement, followed by the same eight actual RF3
SDK/MCP workflows on the current owned image with complete cleanup. Source
lifetime/privacy review supplements environmental failure evidence; no fake
provider or synthetic hook qualifies startup. A successful rerun cannot erase
R312 or establish why it stopped. Runtime qualification remains pending.

## Fixture-owned preparation composition owner

TASK-TUNIT-LOCAL-PREPARATION-OWNER-009 implements REQ/AC-TEST-010/014/015 and
REQ/AC-TUNIT-ENTRY-006. The fixture uses pinned Aspire13.6's existing public
direct testing builder Create rather than the entrypoint-suspending CreateAsync.
An AppHost-owned non-inlined composition bridge ensures native stack-based DCP
metadata discovery selects KeyLoad.AppHost, then invokes unchanged AddKeyLoad.
The session records the actual native builder immediately, extracts the original
validated runner configuration, removes the recursive runner and verifies its
one-prerequisite/no-container model before Build/start. No original standalone
Program/TestSuiteApplication is resumed to wait on the removed tests-rf3 resource.
The session is the sole prerequisite execution owner. Native direct builder
disposal owns the built application after Stop and original output joining;
do not separately dispose that same application first. Keep every original
primary/cleanup failure, selector, policy, producer, receipt and tag gate.
Standalone suite behavior and the authenticated default image path are unchanged.

The pinned [native implementation](https://github.com/dotnet/aspire/blob/v13.6.0/src/Aspire.Hosting.Testing/DistributedApplicationTestingBuilder.cs)
defines direct creation, stack DCP discovery, Build and builder disposal; the
suspended factory instead resumes the original entrypoint. Root integrates and
qualifies this private AppHost Hosting bridge/session join; rollback restores
only this composition path, with no data/dependency/public API migration.
Verification must retain the unchanged R312 failure and run the same eight
actual standard RF3 SDK/MCP operations on its freshly owned image, proving
original producer exit, exact model/container identity, three-node readiness and
complete stop/reader/lock/tag cleanup. Six-silo membership and Linux gates remain
separate. This repairs a concrete competing source owner; it does not assert
R312's original stopping cause or claim an unexecuted green result.

## Explicit public Q2 rejection RF3 selection

TASK-REL-004-INNER-JOIN-PUBLIC-012-SELECTOR maps AC-REL-004-JOIN-001/003/005
and AC-QUERY-007-JOIN-001 to REQ/AC-TEST-015 and REQ/AC-TUNIT-ENTRY-006 under
ADR-118/119. Native local-image selection additionally admits exactly:

    /*/*/RelationalSqlRf3JoinRejectionTests/*

This selects the existing12 Arguments of
RejectedJoinOnFourPublicPathsPreservesRowsAndCompleteHealthyPage. Preserve the
byte-exact standard8 and six-silo1 selectors. Wildcard class names, method-only
subsets, combined classes, unsupported/mixed/inherited image configuration and
GitHub provenance reject; no default selector or tool catalog expands. Both the
native selector and fixture's closed argument reader must admit this exact
additional selector before preparation. The existing actual Node selection
workflow tests positive original TUnit/filter/environment propagation and every
existing rejection for all three selectors, plus four public12 near-miss filters
with the exact safe local-selection error. This is infrastructure evidence only.

Sol owns selector/argument admission and native selection regressions; SolNative
owns the independent public12 helper commandId correction. Every SQL CALL
configuration command carries its required outer canonical commandId for
that operation under the existing stable command identity contract.
Root joins the canonical independent R3 public12 packet and this selector and
runs all12 complete rejection operations through real SDK query/SQL and official
MCP query/SQL on the same existing owned RF3 fixture, preserving literal full
row identity/JSON/revision, no disclosure and complete healthy follow-up. Keep
original image/source verification, readiness, cancellation, process/reader
joining, locks and exact-tag cleanup. No provider, public API, storage format,
SQL language claim, default policy or qualification gate changes. Native
normal/scalar selection proof, actual12 RF3 reports and original Linux delivery
remain pending until executed; neither this selector nor source review closes
full SQL or SQL-client protocol conformance. Rollback removes this additional
selector and its assertions as one unit.


## Native fixture default-time admission, TASK-TEST-EMBEDDED-CLOCK-ORDER-001

TASK-TEST-EMBEDDED-CLOCK-ORDER-001: TestDatabase is the shared test owner of a genuine native ZoneTree DatabaseEngine. Submit without explicit time must use existing DatabaseEngine.ApplyEmbedded, which selects final business time inside the original ordered Store.Commit callback. Submission with explicit DateTimeOffset retains public Apply and its strict supplied-clock/replay/outcome semantics. Preserve the same stable operation ID, serialization, principal, errors and one real commit; no clamp, retry, mock coordinator, new gate or fenced-manager reset. This fixture composition refines TestInfrastructure actual-operation evidence (REQ/AC-TEST-010) and ADR-028 embedded clock ordering, not RF3 topology or clock qualification. NativeSagaTimeout shared PerTestSession fixture concurrently admits default CompleteSaga/CancelSaga/ConfigurePrincipal/revocation operations while native durable-job journal removal commits; pre-gate default clock sampling can therefore race the later committed instant. Existing five actual NativeSagaTimeoutFunctionalTests plus explicit stale-clock/ClockUncertain/replay tests, full normal/scalar and Linux gates remain required. Original R308 journal fences remain immutable evidence; no runtime success is claimed.


## Saga deadline construction and admission

TASK-TEST-SAGA-DEADLINE-ORDER-002 freezes CreateWaitingSaga intent: sampled now constructs dueAt, persisted CompareExchangeSaga.Deadline and identical DueWorkHint.DueAt. It is not an externally authoritative command evaluation instant. Sample deadline construction from the owning Database.EvaluationClock, preserve unchanged DueOffset/CompletionTimeout arithmetic and the exact canonical deadline/hint. Submit without explicit time selects final operation evaluation inside existing ApplyEmbedded commit admission. Never widen deadline, clamp time, retry or reset the native journal fence. Explicit-time canonical clock/replay regressions elsewhere remain unchanged. Existing five genuine NativeSagaTimeoutFunctionalTests deadline/effect/stale/revoked/completed/canceled and healthy control-job oracles remain required, along with original R317 outcomes before join.

### TASK-TEST-R382-NATIVE-OWNER-EXPIRY-001

REQ-TEST-010 / AC-TEST-010 and REQ-MSG-007 / AC-MSG-007: original R382 native failure evidence is retained. Each actual native TestCluster builder carries one private fixture-owner token in its Properties; actual ISiloBuilder.Configuration resolves only that owning fixture. A separately owned fixture cannot replace or clear another builder's owner. Register before native build/start, unregister only the same owner during its original joined cleanup; startup failures retain original failure and database cleanup failures. No new product hook, alternate coordinator, timeout or provider. Existing shared PerTestSession and owned boundary fixtures keep their actual native Orleans/ZoneTree lifetimes.

The parent expiry whole-flow advances the actual owning clock two minutes after the first committed claim. Its unchanged default30second lease has then expired. Canonical receive replay reauthorizes that lease (CommandOutcomes.ValidateCachedQueueReceive -> Messaging.Lease) and must return exact Rejected/LeaseExpired with null result; serializing that null as a recovered receipt was the test error. Assert complete outer/lane identity, typed failure, no stop error, unchanged original persisted ReceiveResult bytes, every native store byte and position, followed by fresh healthy receive. Never extend lease or parent deadline, reset clock, disable strict native validation or invent a result. Existing valid receipt replay cases remain intact. Fresh original focused normal/scalar, full native and Linux gates are required; no source-only PASS or closure.


### TASK-KL014-COLD-BOOTSTRAP-SDK-001

REQ/AC-TEST-010, original architecture KL014 and ADR039/117: one fresh owning ClusterFixture captures actual selected storage root and native path existence immediately before creating the AppHost builder, then starts actual Aspire-owned RF3 current image resources, waits existing readiness, uses its discovered endpoint and private local profile in the actual built CLI status child. Reuse original process reader/exit settlement policy from AppHost; no CLI build/restore, secret argument, manual Docker or deadline expansion. Persist non-admin scoped credentials through actual SDK admin calls, configure collection, create/read full literal document, compare complete native receipt bytes for same command replay, require changed-content Conflict and unchanged full document plus original replay bytes, then healthy new-ID revision2. Await original fixture shutdown and assert only its owned root removed; primary and cleanup failures remain aggregated.

This is cold data/bootstrap proof on native RF3, not proof of an unprovisioned clean operating-system machine or a single server without external services. The original clean-machine criterion additionally requires authenticated Linux runner tool/image prerequisites and actual execution evidence; topology remains mandated RF3. Existing SdkSubmitReturnedCancellationReusesCommittedReceipt remains separate mandatory UnknownWriteOutcome recovery proof, including its retained original failure. No task closure or PASS is claimed from authored source.

The fixed internal ClusterFixtureColdStartObservation records only selected Root plus actual directory/file existence after coverage root selection and argument construction, before native builder/startup. No callback, fault control or caller-selected root. The whole operation requires that observation absent and exact same actual root after startup. Existing covered RF3 profile remains exactly eleven selected cases; this new bootstrap case is normal RF3 qualification, not currently eligible for covered selection. Register its actual case through the existing ledger: normal mode is no-op; unexpected covered selection remains rejected, with no whitelist change or coverage suppression. Constructor random root does not establish selected-root identity.

## TASK-CQ-RF3-PREPARATION-OWNERSHIP-002

REQ-CQ-039/040 and AC-CQ-039/040 retain the closed original eleven contributor,
source/DLL/PDB/image/context and collector admission. Original4e18 BeforeSession
selection failure used obsolete two-class runtime filter while producer selected
seven classes/eleven cases; current shared catalogue already repairs that mismatch.
No nested empty-string normalization is authorized: nested fixture mode/source are
explicitly cleared, suite is absent, and current typed image context remains strict.

The native TUnit coverage preparation owner must compose the AppHost directly through
the existing Aspire testing builder API, validating outer original-node selection and
exact closed catalogue first, then adding the unchanged real prerequisite resources.
It must never resume a standalone TestSuiteApplication after removing its runner.
The fixture owns preparation start/readiness/output/stop/dispose/collector cleanup;
original primary plus joined cleanup failures remain retained. No deadline, image,
source manifest, public RF3 topology, whitelist or thresholds change. Extend existing
genuine source-preparation operation with exact old-selector rejection followed by
healthy canonical-selector source admission and unchanged original manifest bytes.
This admission control is supporting infrastructure evidence, not product coverage;
actual covered RF3 eleven original collector flows and Linux gates remain mandatory.

Supporting control owns GITHUB_SHA briefly under the existing globally nonparallel test, using only the genuine prepared native source manifest revision, restores the exact prior environment in finally and propagates primary/restore failures. Production selection still checks its original environment source SHA; no bypass parameter or fabricated manifest.

R3 joined restoration: observe each original environment-key restore individually through existing ServerFailureObserver, continue every remaining key, retain all earlier stop/output/builder/image failures and throw only after restoration settles. Output lifetime disposal and cleanup service lookup similarly retain original failure identities. Prepare primary/cleanup aggregation remains unchanged. Correct actual AppHostRuntimeOptions.NativeCoverageRf3Image property verified in owning source.

## TASK-KL021-OWNED-SILO-ISOLATION-002 — accepted namespace control implementation contract

REQ-MTOKEN-ISOLATION-003: native fault image derives from the exact existing Dockerfile pinned aspnet/runtime and server build. A dedicated feature-owned fault Dockerfile derives from the authenticated exact server @sha256 image receipt; fault-runtime installs native iptables and a bounded coreutils timeout wrapper as container UID0 at image build, then restores APP_UID. Normal root Dockerfile and its runtime final/default target remain byte-for-byte unchanged and receives no fault tools or capability changes. Aspire13.6 public WithDockerfile(context,Dockerfile,stage:fault-runtime) owns the selected three-node image build/start; WithContainerRuntimeArgs adds only NET_ADMIN to these exact owned resources. No privileged, host network/PID namespace, production default changes, manual topology, image digest fiction or whitelist expansion. Tool version/package/license and base/build/image/config identities are actual build/start receipts, not inferred from a web page.

AC-MTOKEN-ISOLATION-003: fixture opts into a separate owned native fault wave and requires Linux/running container Config.User APP_UID, only admitted additional capability NET_ADMIN, no Host/PID/shared namespace, exactly three model names and matching incarnation/config image/build source. Before every firewall mutation re-inspect owned ID/name/image/config/incarnation and verified unique IPv4 endpoints; reject malformed/ambiguous/IPv6/foreign source rather than accepting alias fallback. Native discovery SiloAddress IP/port must match inspected network address TCP11111; HTTP8080 endpoint remains discovered from Aspire. Unconfigured standard/default/covered fixtures cannot acquire this fault capability.

REQ-MTOKEN-ISOLATION-004: create unique per-run bounded-length chains inside only the old leader's network namespace. For each of exactly two owned peer IPv4s, INPUT source and OUTPUT destination rules block both sport11111 and dport11111; attach those exact chains first in the corresponding chain so existing connections and both directions are isolated. HTTP8080 remains outside all rules. Record chain/rule mutation intent BEFORE execution and actual exit/output/check receipt before next mutation, including partial chain creation/jump install. Never flush shared INPUT/OUTPUT rules or unrelated chains. Restore only own exact jump/rules/chains and verify absence.

AC-MTOKEN-ISOLATION-004: native docker exec --user0 uses inspected full owned container ID, executable argument list, no shell interpolation, no detach/privileged. Image timeout wrapper bounds remote iptables work; xtables lock wait fits within it. Host original exec task and bounded stdout/stderr readers remain owned and are actually joined even after initiating cancellation, then partial intents reconcile against native exact chain/rule state before restore. Docker CLI cancellation alone is not evidence of remote exec completion. Unsettled original remote/host work retains primary/cleanup/resource/image ownership and fails; never restores over an in-flight mutation or deletes retained receipts. Actual counter/rule checks and genuine surviving-majority new-term ACK provide fault proof, not HTTP reachability alone.

AC-MTOKEN-ISOLATION-005: real SDK/officialMCP whole-flow uses current optional MinimumToken public contract: original canonical command ACK/token; exact old leader/container/native term; namespace isolation; actual different surviving leader/newer term and majority ACK of one immutable next command; reachable running old leader rejects token-bearing strong read with exact native no-authority error, no stale/partial body; same exact nodes/rules restored and joined; literal current document/revision/token/receipt and healthy reads across restored replicas. Existing parent/start/cleanup deadlines unchanged, no timer sleeps or generic retry-to-green. Any canonical UnknownWriteOutcome is preserved and reconciled only under existing original-ID/receipt contract, never replaced by fresh IDs or fabricated ACK.

Public API evidence: Docker documents container exec UID selection and non-detached process lifetime at https://docs.docker.com/reference/cli/docker/container/exec/ ; namespace isolation and NET_ADMIN (rather than privileged) at https://docs.docker.com/engine/containers/run/ . Pinned Aspire13.6 local XML advertises WithDockerfile stage/build-before-start and WithContainerRuntimeArgs; these are source/API evidence, not native execution. Netfilter upstream https://www.netfilter.org/projects/iptables/index.html owns rule manipulation semantics. Current tool package version/image ID/namespace behavior will be measured in root's genuine native build/start gates; no runtime availability claim before execution.

Ordered ownership: docs/ADR017 contract precedes Dockerfile fault-only stage and AppHost ClusterReplication resource composition helper. Integration ClusterReplication models/lifecycle/processes own exact node/image/IP/chain/rule receipts, original process/readers and restoration; owning case/flow consumes disjoint KL021 MinimumToken API packet. Root joins source/serialization/API paths coherently, builds and runs actual native Linux RF3. Source/API readiness, functional proof and all broader endurance/performance gates remain distinct; no KL021 task closure from authored code.

REQ/AC-MTOKEN-ISOLATION-006: NodeStatus gains additive native Id9 ConsensusTerm, verified free after existing IDs0..8. NodeAdministration.StatusAsync copies Term from the same existing locked partition.Consensus.StateAsync snapshot used for Leader/Applied. Current persisted authorization, separate request grain, readonly status semantics and native/public serializers remain unchanged. Purpose is authenticated operational authority diagnostics, not a test hook. Original ACK and actual new-majority ACK whole-flow requires positive original term and strictly larger surviving term; changed leader alone does not prove it. No on-disk live inspection or invented term. Public SDK/official MCP status full outcomes bind actual native term; defaults of construction do not qualify it.


Frozen bounded native authority observation (owner approved2026-10-08): under the SAME original McpCallerDeadline token, sequential authenticated SDK Status operations, exactly one awaited original request in flight, on the two inspected surviving voters may observe readiness. No Task.Delay, sleep, polling worker, reset deadline, mutation retry or health-only inference. The latest32 actual status/error observations form a fixed-size closed diagnostic summary; replies do not accumulate. Readiness is bounded by the original deadline, never by an arbitrary attempt count. Only exact OwnershipLost/503/"The cluster has no current leader with a reachable majority." is a pending authority observation; any other failure remains fatal to the flow. Readiness requires an explicit positive ConsensusTerm strictly newer than the original ACK's status and Leader equal to one of those exact two surviving voters, different from the isolated voter. Then submit exactly one immutable next command; require its actual quorum ACK and fresh status/official MCP status native term. Original cancellation fails honestly after joining its actual observation. Default production status semantics remain diagnostics, not a new strong-read promise; the subsequent actual ACK is the authority proof.

Service-user invariant clarification: the fault Dockerfile inherits and restores the base APP_UID USER. The selected resources preserve the already existing ClusterResourceSettings --user uid:gid override exactly, captured through pinned public ContainerRuntimeArgsCallbackAnnotation before adding NET_ADMIN. They do not change default host/container identity or start service as root. Native tool/read commands alone use --user0 inside each verified owned namespace.

Persisted NodeStatus.NodeId is a physical GUID independent of Aspire node1/node2/node3 resource names. The real flow captures each original authenticated endpoint identity and requires it unchanged after restoration; public SDK/official MCP status comparisons bind that actual GUID rather than an invented resource-name identity.


## Accepted partial native restart ownership (2026-10-08)

TASK-REP-PARTIAL-RESTART-001 refines REQ-REP-004 / AC-REP-004 and REQ-TEST-007 / AC-TEST-007. Before implementation: an actual successful Aspire Start is retained independently from its health/readiness completion. Once attempted, an unobserved Start outcome cannot be repeated; it remains owned until actual application/resource shutdown retires it. Once accepted, subsequent recovery resumes only native inspection, health wait and receipt settlement under the caller's existing token/deadline. It never reissues Start because health wait was canceled. The first observed replacement ID/configured image/image ID/start timestamp is retained and exact unchanged identity is required after health, before the original receipt is published. Native runtime identity changing during settlement fails; no readiness success is inferred from running state. Kill cannot replace an unsettled prior restart owner. The original initiating error and all cleanup errors remain separate.

The fixture exposes a useful two-phase native ownership boundary: BeginContainerRestartAsync accepts the actual Start and observes its running replacement; RestartContainerAsync settles native health and receipts for the same owner. This is fixture lifecycle composition, not a product test hook. AC-REP-PARTIAL-RESTART-001: AcEvent008AcceptedRestartCancellationResumesSameReplacementAndPreservesReplay uses actual killed leader/accepted replacement, cancels at the exact-token original health-settlement admission gate, then resumes under the original operation deadline and proves the same running replacement identity, canonical SDK receipt replay, full literal SDK/official MCP aggregate state on all three replicas. No timer, sleep, fake resource, deadline increase or mutation retry is added. During-health scheduler timing is not claimed: the deterministic cancellation is at the explicit accepted-Start-to-native-health boundary. Existing original leader-loss case remains intact.

Original aa103 run37686560768/job113015778654 failure remains immutable. Its initiating Starting/unknown-health cause is unresolved; this repair addresses only the proven secondary repeated Start. Exact-source Linux native RF3 qualification remains open. Root owns join/build/native execution.

```mermaid
flowchart LR
  K[Verified kill] --> A[Attempt Start once]
  A --> U[Unobserved outcome: retain owner]
  A --> S[Accepted Start]
  S --> I[Retain native replacement identity]
  I --> H[Health settlement]
  H -->|canceled| I
  H --> V[Same identity and original receipt]
```

Restart diagnostic refinement: resumed attempts explicitly record AcceptedStartRetained, never a fabricated new Start success. Begin phase failures save the original exception/category at actual StartCommand or RuntimeInspection stage; diagnostic cleanup failure is attached through the unchanged failure-retention path.


## First-incarnation readiness evidence (2026-10-08)

TASK-REP-READINESS-EVIDENCE-001 refines REQ-REP-004 / AC-REP-004 and REQ/AC-TEST-007. This stage follows TASK-REP-PARTIAL-RESTART-001 without changing admission. For each exact Aspire discovered owned node endpoint, one native GET /health/ready is awaited and its bounded response reader settled/disposed. It shares the existing three-second per-node diagnostic deadline with signed discovery, rather than granting either a second budget. Only status200,503,other HTTP status or fixed unavailable is retained; response bodies are drained within the existing8192-byte cap and never retained as readiness explanations. Server's503 body does not identify DatabaseReady/compatible-cohort/catalog branch, so branch remains explicitly Unobserved. No server response/auth semantics change or inferred reason is introduced.

Each existing actual native ResourceId log subscription records its closed started/running/completed/canceled/faulted state plus observation timestamps of first/last received log lines. These are observer times, not invented native event timestamps; observed faults are rethrown for existing joined disposal. Save snapshots this evidence without replacing its original failure. ResourceId-keyed task lifecycle is retained; no unproved reattachment is introduced. Current24-line buffers and80-line/8192-byte total artifact limits remain unchanged. Evidence groups put resource/readiness/discovery/subscription state first, followed by newest retained logs so prefix clipping cannot omit the latest signal. Existing failure markers remain.

AC-REP-READINESS-EVIDENCE-001 maps to the actual AcEvent008AcceptedRestartCancellationResumesSameReplacementAndPreservesReplay flow: canceled accepted restart saves real diagnostic evidence before recovery; after real all-three SDK/MCP healthy replay, another one-shot observation must contain HTTP200 for all three owned discovered endpoints and actual log observation state. No fake response/source assertion/timer/retry/increased budget. Native execution and exact Linux original evidence remain root-owned and pending.

The owned namespace-isolation whole flow uses `ReplicaIsolationFixtureLifecycle` to retain the actual fixture until successful startup, then join exactly one final disposal. Failed `ClusterFixture.InitializeAsync` continues to join its original startup cleanup before returning the failure; the outer scope must not issue a second cleanup. Each restored voter has its own native HTTP/official MCP scope, joined before advancing to the next voter, preserving all primary and cleanup failures.

TASK-KL021-NATIVE-NETWORK-IDENTITY-003 refines REQ-MTOKEN-ISOLATION-003 and AC-MTOKEN-ISOLATION-005 within the existing owned-namespace contract. Authenticated original Linux run37821315110/attempt1/source3458 retains schema2 admission facts: one attached network has a present name and ID; native NetworkMode exactly equals that same attached ID and does not equal its name. Admission requires a nonempty mode/name/ID and accepts exact ordinal equality with either identity of that one actual attached network. Missing/malformed identity, multiple networks or an unrelated mode still fails; no prefix, alias, caller value or raw identity is published. Every original running/nonprivileged/non-root/PID/capability/configured-image/source/incarnation check, genuine majority ACK fault proof and joined rule/container cleanup remains mandatory. Root owns ReplicaIsolationContainerAdmission; the existing full former-leader SDK/official-MCP minimum-token failure/restoration flow remains the actual Linux regression. Source review cannot qualify the namespace or close KL-021.

TASK-TUNIT-COVERED-SHUTDOWN-IDENTITY-009 retains the original verified coverage container names on the same startup/stop identity owner, as frozen in [NativeTUnitEntry](TestInfrastructure/NativeTUnitEntry.md) and [ADR-117](../ADR/ADR-117-native-tunit-ci-entry.md). This removes the post-Stop service lookup which failed four original covered relational flows; every native stopped-container and collector/cleanup/coverage gate remains unchanged and pending current-source execution.

The four `NativeTextWait*` unit flows must seed the actual canonical prefix through the native `DurableReplicaLog` and `ReplicaMaterializer`, wait for completed apply and resolve the original persisted complete receipt. `ReplicaAppliedPositionWaitFixture` accepts optional database limits and evaluation clock for this actual ordered-apply component flow. Direct embedded commits cannot substitute for the persisted replication-applied cut; tests must not write `AppliedBytes` manually or weaken production cut validation. This component fixture remains an explicit single-voter unit control; genuine RF3 authority is verified only by the independently owned Aspire/Docker integration flow.

## Native fixed-name restart recreation observation (c028 failure repair)

REQ-REP-PARTIAL-RESTART-001 / AC-REP-PARTIAL-RESTART-001: after the one accepted native Aspire Start, fixed-name container removal/recreation is an observable lifecycle transition. Successful native `docker container ls --all --no-trunc` scoped to the exact anchored owned name may report no container until creation. The original bounded runtime observation continues under its original token, timeout and polling interval; this is not another Start or a database retry. A present observation must contain exactly one full ID and the exact name, followed by native inspection of that ID; existing image/start identity and health settlement fences remain mandatory. Docker command failure, malformed or foreign identity, cancellation and cleanup failures remain failures, never absence.

TASK-C028-RESTART-OBSERVATION-001 retains the actual `AcEvent008AcceptedRestartCancellationResumesSameReplacementAndPreservesReplay` SDK/official MCP complete receipt/state flow and original retained-replica/leader-loss scenarios as native qualification. Source review does not qualify these cases. Original c028 required TRX failures and all later cascades remain retained; this correction does not establish a cause for unrelated failures.

## Signed public read admission cancellation (AC-ANN-008)

The actual current-image Aspire RF3 wave may accept the same centrally validated
QueryExecutionOptions already owned by ClusterFixture. A typed optional profile
is applied before BuildAsync; null preserves every existing caller. It changes
no image whitelist, authority, deadline, signed control format or covered-eleven
inventory. The explicit ANN profile is disabled outside its owned scenario.

AnnPublicCancellationScenario arms the existing signed v2 AuthorizationReload
read phase for one newly persisted c1-probe identity, empty command identifier,
ApproximateSearch read kind and actual discovered voter. It cancels the original
SDK/official MCP token only after that exact signed observation; cancellation and
ProducerDisposed settle before retiring the arm, rereading the full canonical
literal corpus and performing healthy SDK/MCP/Q1 ANN reads. SDK read cancellation
is Cancelled with no page, never UnknownWriteOutcome. Official cancellation is
its actual interruption. This proves public admission cancellation only; native
observed-work and partial-file-write cases separately prove execution cancellation.
Existing parent/wave/cleanup deadlines, original task joins, locks and primary
plus cleanup retention remain unchanged. No runtime success is claimed.

Source-bound phase precision: the existing signed AuthorizationReload observation
is immediately BEFORE GrainRequestAuthority.ReloadForRequest, followed by the
original scope recheck and fresh persisted reload. This cancellation case proves
pre-reload public read admission settlement, not completed authorization or
during-index work. The ordinary healthy follow-up performs actual current
persisted authorization and one-cut search; separate native worker cases retain
the genuine after-work cancellation oracle. No control semantics are renamed.

## Native ANN fixture applied authority (TASK-ANN-NATIVE-FIXTURE-001)

REQ-ANN-001/007 and AC-ANN-007 require actual persisted applied authority. ANN
Unit and CrashHost fixtures must submit original commands through independently
owned native DurableReplicaLog and ReplicaMaterializer; embedded Apply does not
establish a replicated cut. Every configure/seed/consumer/checkpoint command
retains its exact identifier, payload, explicit time and outcome. The native
materializer writes applied authority through original canonical Apply(index).
After actual bounded WaitForApplyAsync the original operation is replayed only
to retrieve its persisted outcome, with exact no-effect assertions unchanged.
No manual AppliedBytes, no synthetic no-op authority, no collector weakening.

The opt-in TestDatabase native admission preserves every default embedded and
explicit-time caller. It owns its separate replica store/log/snapshots/materializer
and joins the materializer before disposing log/replica/canonical owners. Local
fixture commit is not a claim of RF3 quorum; public RF3 gates remain mandatory.
Original validated ReplicaExecutionOptions.CommandTimeout bounds apply settlement;
original caller cancellation remains linked. Native worker failure is propagated,
not retried or hidden. Constructor and terminal cleanup retain primary plus owner
failures. CrashHost keeps distinct acknowledged and JournalFlushed original cuts
and reopens the same actual durable replica log before replay. Process-kill is
not power-loss evidence. Original R4 defect receipt remains immutable history.

Known native command IDs bind their ORIGINAL retained ReplicaEntry index. If a
restart/canceled wait left that entry unsettled, admission publishes only its
original commit index if not already committed, joins its original apply cut
under the same existing timeout/caller token and resolves its fresh original
outcome. RecoveryRequired cannot cause a second append for a retained ID. Native
entry absence within LastIndex fails closed; no fabricated command authority.

## Scoped KL-036 native Linux acceptance lane (2026-10-09)

TASK-KL036-EXACT-NATIVE-ACCEPTANCE-001 / REQ-KL036-NATIVE-ACCEPTANCE-001 / AC-KL036-NATIVE-ACCEPTANCE-001 add KL-036 to the same closed native task matrix and manual selector. This is scoped public-parent/supporting acceptance, never whole KL-036/product/fullsuite/coverage qualification. Existing task identities KL008/011/014/015/021/027, default full suites, scalar/recovery/RF3 and all artifact/provenance/image/cleanup gates remain mandatory. Five selections contain29 declared cases per profile:20 existing source-mapped supporting instances,3 complete native encoded type-frame flows, and6 genuine Aspire-owned public-parent RF3 flows across PublicParentFlowTests(1), ExpiredRetireCancellationRf3Tests(4), ParentCapacityRf3Tests(1). The UTF8/UTF16 native fingerprint method retains its three actual Int32 boundary instances0/1/2. Framing is supporting registered generated-command/counter framing, malformed native-version refusal then exact healthy complete-payload continuation and concurrent native sessions/canceled admission; it is not a separate product capability.

Root must supply fresh original native filtered metadata and PE/PDB/source bindings before final contract admission. Class/method/parameter/native display/instance/UID/source identity, exact union/no skip/no duplicate/no extra, unmodified original TRX and counters, original source/image before-after and settled native process/registry/resource cleanup remain unchanged. Source-declared counts are not executed or discovered evidence. Linux normal/scalar cells use separate GitHub runners and the existing server-only image producer, exact source/run/attempt/image receipt, native MaximumParallelTests20 and preserved NotInParallel shared-resource protections. Scalar describes only the caller environment; no scalar server assumption is invented. Existing unit30m/RF3 selection60m and180m job limits are unchanged. Failure retains authentic reports and blocks qualification; no omission, retry, fallback, custom runner or extra workflow is introduced.

Supporting cases retain real persisted caller-stamped/wrong-stage denials, cold unknown/prepared-winner cancellation, raw scope/read reservations and release/deadline isolation, authenticated Capture/fullmodel/receipt cold replay, native canonical fingerprint and actual Orleans context restoration/concurrency/disposal. Public RF3 preserves linked models and SDK/official MCP/Q1 receipts across two genuine RF3 owners/cold replay; original cancellation own-outcome/lost-reply/unknown charged refusal/distinct cleanup generation; same-token sealed expiry only Cancelled/no-effect; actual cold-corrupt disposition refusal followed by exact fixture-owned database+replica restoration and healthy continuation; configured capture-shape/grant bound denial and restoration. Independent same-seal MissingAuthority remains OPEN, as do native future embedded SafeDetail upper-bound and exact operational pre-admission+1 criteria. No selected flow can close those unqualified gates or substitute fixture reset for production recovery/power-loss proof.

Traceability preserves existing PartitionTransfer/ADR106 public-parent/cancellation/disposition contracts and ResourceExecution scoped-grant requirements; this additive execution orchestration uses ADR117 and REQ/AC-TUNIT-ENTRY-010. Ordered stages: docs-first scope, private exact contract/validator/workflow change, root fresh build/native discovery identity comparison, unchanged source/image/process checks, commit/push and authenticate each task-acceptance-KL-036-normal/scalar original artifact source/run/attempt/API+ZIP digest before any acceptance claim. Root owns live docs/workflow/compiler/Git joins; security owner repairs scoped infrastructure and harvests these dedicated original outcomes, Movement/Session own whole parent implementation. Rollback removes only the additive KL036 dispatch/contract entry while retaining originals and all prior lanes; there is no data/protocol migration.

```mermaid
flowchart LR
  A[Exact source and native metadata] --> B[KL036 normal or scalar caller]
  B --> C[23 native supporting cases]
  B --> D[6 genuine Aspire RF3 cases]
  C --> E[Original source image TRX and cleanup verification]
  D --> E
  E --> F[Scoped acceptance only]
```


### TASK-SDK-KESTREL-FAILURE-OBSERVATION-001
REQ-TUNIT-KESTREL-OBSERVATION-001 / AC-TUNIT-KESTREL-OBSERVATION-001 under ADR-117 and existing ClientApi transport requirements freeze test-only bounded observations for the authentic eb0 five failures: KeyLoadClientNullReadTests.AllNativeAbsentReadsRemainSuccessfulBeforeUnavailableStatusAndFullHealthyContinuation (two existing arguments), KeyLoadClientNullWriteTests.UnavailableSuccessfulWriteBodyRetainsUnknownOutcomeStableRetryAndNullableReads (two existing arguments), and KeyLoadClientTransportTests.MidBodyCancellationMapsReadFailureAndClientCanSendNextRequest. Actual Kestrel start/send/handler/response completion/cancellation records carry only closed stages/status/category and actual monotonic elapsed, ThreadPool available worker/IO threads, pending work and native thread count. At most64 records, saturation explicit. No URL/path/header/body/credential/payload/caller value is inspected or emitted.
The existing ephemeral loopback Kestrel/default native HTTP handler chain, actual client/request tokens, five-second bounds, full original negative-to-healthy assertions and cleanup remain unchanged. The optional observation is passed only by these flows; no extra request, warmup, retry, timer, NotInParallel or product/config change. On actual failure only, emit closed JSON to original native stderr before rethrowing the initiating exception; diagnostic-output failures join that same ordered ledger. Success emits nothing. This reveals observed stages only; it cannot infer thread-pool starvation or socket/JIT causation. Root owns guarded join/build and exact Linux normal/scalar original TRX/output qualification. No helper-only test or synthetic outcome qualifies the contract.


## TASK-KL033-NATIVE-TASK-ACCEPTANCE-001

Under REQ/AC-TUNIT-ENTRY-010 and ADR117, the closed native task contract adds KL-033 to the existing independent Linux normal/scalar matrix and manual selection. Exact R863 original native metadata supplies four selections:29 Unit cases,10 derived lexical process-recovery cases and6 genuine Aspire RF3 cases,45 per profile. The complete finite criteria and source map are frozen in [Search](Search.md#task-kl033-native-task-acceptance-001-original-finite-hybrid-rank-and-explain); supporting metric/window controls cannot substitute for the actual database/public flows.

Reuse the same prepared source-bound native server image, PE/PDB before/after, exact typed native discovery and original TRX union, MaximumParallelTests20, bounded1800-second discovery, existing30-minute Unit/Recovery and60-minute RF3 child bounds,180-minute job, native exit/readers/disposal and owned image registry cleanup. Both profiles preserve original artifacts; any failed/partial/missing/extra/duplicate/skipped or mixed-source case refuses acceptance. Scalar RF3 remains caller-only. Existing seven tasks and all mandatory complete jobs remain untouched; no custom runner/workflow, LocalRf3Image or coverage/product promotion.

Ordered stages: docs-first finite scope; guarded canonical contract/closed selectors/workflow join from actual native metadata; root build/format/source guards; commit/push; authenticate exact source/run/attempt/cell/API+archive SHA and all original outcome/source-image/cleanup evidence. Root owns live integration/compiler/Git; security owner prepares guarded tooling and authenticates dedicated KL033 originals. Dependencies/global ANN/ranking/quality/performance/endurance remain separate. Rollback removes only additive KL033 scope and retains immutable originals. This source orchestration is not passing Linux acceptance.


## TASK-KL035-NATIVE-TASK-ACCEPTANCE-001

REQ/AC-TUNIT-ENTRY-010 adds the complete original KL035 snapshot/install task to the accepted native independent Linux normal/scalar matrix and manual selector. [ClusterReplication](ClusterReplication.md#task-kl035-native-task-acceptance-001-complete-original-snapshot-installation) freezes empty follower, full installed native cut/public outcomes, checksum/atomic/interrupted/corrupt installation and post-GC snapshot+tail recovery criteria. Actual R863 native metadata supplies1 Unit,32 Recovery and2 genuine Aspire RF3 cases:35 per profile. Retained-follower public operation is separate from actual erased-follower SDK/official MCP operation; neither supporting controls nor forwarded reads replace native installed-cut assertions.

Keep all prior eight task lanes/full mandatory jobs, same source-bound server-only image producer/PE-PDB prepared-before-after, original exact native discovery/TRX union with no extras/missing/duplicates/skips, native20, existing1800-second discovery/30-minute Unit-Recovery/60-minute RF3/180-minute job, actual joined child exit/readers/disposal and owned image cleanup. Scalar RF3 disables caller intrinsics only. No custom workflow/runner/LocalRf3Image/bound increase or coverage/product promotion.

Ordered stages: docs-first complete criteria; actual filtered native binding; root guarded canonical contract/selectors/workflow join and source verification/build/format; commit/push; authenticate each exact source/run/attempt/profile API+ZIP and original outcome/source-image/cleanup before acceptance. Root is sole live integration/compiler/Git owner; security owner privately maps and harvests dedicated originals. All failed/partial/pending/missing or mixed evidence remains unqualified. Rollback only additive KL035 scope, retain immutable originals. Process-kill qualification does not close power-loss/endurance/performance/full-product gates.

### TASK-KL029-NATIVE-TASK-ACCEPTANCE-001

REQ/AC-TUNIT-ENTRY-010 and REQ/AC-FTS-001–007,
REQ/AC-FTS-INCREMENTAL-001–005/007 and REQ/AC-FTS-INC-009–017
retain the original KL029 architecture predicates: projection deletion plus
canonical replay returns literal results, checkpoint crashes cannot conceal
updates, and stale revisions cannot resurrect. Add only KL-029 normal and
scalar-caller cells to the closed native task matrix/manual choice. Preserve
all nine prior tasks, full mandatory build/rules/unit normal/scalar/recovery/RF3
coverage gates, original source-image identity/admission, native20 slots,
Detailed live output, strict caller environment, retained failure/no-skip checks
and genuine joined Aspire cleanup. Scoped task receipts never replace full
suites or supply coverage/provider/performance/endurance/power-loss evidence.

Three exact closed selectors cover the source-authored58 Unit instances
(including damaged durable original intent and natural original-token expiry),
18 genuine process Recovery instances (ten full-generation and eight incremental,
including all seven required cuts plus retained generic posting control), and
nine genuine RF3 instances (SDK, official MCP and Q1 maintenance paths, persisted
row/field/revocation/read-cut, leader loss/restart, cancellation and healthy
continuation). Source counts/displays/constructor expectations are not native
discovery proof. Root must freshly bind every exact UID, method, instance, typed
parameter, primary-constructor reported class, source span/hash and assembly/PDB
against the final coherent image before the canonical contract is admitted.

The natural expiry flow retains unchanged default five-minute lifetime and
original cancellation, actual signed ExpiresAt, original durable intent/failed
ACK and canonical pin/outbox/documents, then genuine Release and distinct fresh
consumer/native generation Build with full literal bilingual/cold continuation.
Keep thirty-minute Unit/Recovery and sixty-minute RF3 native deadlines unchanged;
scalar means actual caller DOTNET_EnableHWIntrinsic=0, never unproved server mode.

Ordered stages: freeze this complete source scope; prepare guarded additive
contract/closed enums/workflow; root coherent build and actual selected metadata;
strict bind/reseal, root join/commit/push; authenticate both original Linux task
artifacts API/ZIP/source/run/attempt/native TRX/images/environment/readers/cleanup
and every final verifier conclusion. Missing/failed/skipped/changed originals
retain failure. Root owns live source, discovery/build/workflow/Git; Session owns
whole KL029 operation review and dedicated original evidence. Rollback removes
only the additive KL029 lane, preserving every prior task and immutable report.
No data, serializer, public transport, provider or topology migration occurs.

Actual R875 coherent-image metadata originals now contain exactly 58 Unit,
18 Recovery and nine RF3 cases for these three closed selectors. The owning
receipt and eleven-task census plan retain original native process/source/PE/PDB
before-after evidence. Strict native case admission remains required; metadata
is not an execution, coverage, Linux RF3 or task-acceptance result.


## TASK-KL035-COMPLETE-SNAPSHOT-ADMISSION-002

REQ/AC-REP-004 and REQ/AC-TUNIT-ENTRY-010 preserve every original KL035 criterion and all35 prior native case objects. Actual root R883 metadata (native0,14 cases,5742 source inputs/437 image inputs, zero drift; stdout SHA02891a853fc6bda7bf31d7d500de9b8b38587360689e895268de85d3c9066fd2) identifies two distinct omitted classes: ReplicaSnapshotRecoveryTests in ReplicaPersistenceTests.cs and ReplicaSnapshotProcessRecoveryTests in ReplicaProcessRecoveryTests.cs. Their14 exact typed native identities are added to the existing Recovery selection. The complete lane is49/profile:1 Unit+46 Recovery+2 actual Aspire Docker RF3. This is metadata admission, never an outcome claim. Existing historical35 binding and every other task/full suite remain unchanged. The neighboring class names in the earlier table did not admit these separate classes.

AC-KL035-PUBLISHED-REPAIR-001 strengthens the SAME four PublishedImageDamageFailsClosedAndPreservesNewerCanonicalState native process rows (SnapshotVerified/Installed × missing/corrupt): retain the genuine published immutable image within the stopped trial, preserve exact original missing/corrupt failure and newer native canonical cut/NodeId/read generation/full receipt. After the original node is disposed, restore only that exact image, cold recover original snapshot4+tail5 and stable original receipts, submit an independently literal new revision5/cut6 native command through the real log/materializer, require its full original receipt and exact retry without another store position, then cold reopen and verify healthy result/old outcomes again. No fabricated receipt, replacement snapshot, recovery fallback, token/deadline change or product behavior. The helper owns assertions for this actual case, not a standalone test.

AC-KL035-PUBLISHED-REPAIR-001 maps to those four existing cases and feature-local ReplicaSnapshotDamageRecovery. Existing node/materializer observed-owner helpers join primary and cleanup failures before trial files are removed. Fixture exact-image repair is not production repair from missing/corrupt bytes. Root must compile, refresh changed-source native metadata/PDB binding, execute actual operations and authenticate delivered-source Linux normal/scalar exact49 union/source-image/TRX/cleanup before acceptance. Original full-suite556 rows and R883 metadata are retained independently; no current PASS, power-loss, endurance, coverage or whole-product promotion.


## TASK-KL036-COMPLETE-SOURCE-ADMISSION-001 — native Linux expectations

The KL-036 selected scope preserves every original34 case declaration and adds the real operational capacity, retained native-page failure and original Close/SourceBeginAbort whole flows, for37 source-declared cases per profile. ADR-106 owns their REQ/AC and execution contracts; ADR-117 owns native invocation and original-report reconciliation. This is an admission expectation, not discovered UID/count or a passing result. Exact-source Linux native discovery, normal/scalar execution and all mandatory full suites remain open. The other nine task objects and all original selectors/case declarations remain intact.


## TASK-KL015-C1-NATIVE-CAUSE-001 — bounded original failure classification

REQ-C1-OUTCOME-NATIVE-CAUSE-001: the original C1 inspector must retain its first OpenStore failure and identify known native database, serializer and runtime causes using only closed enum labels and already allowed numeric codes. AC-C1-OUTCOME-NATIVE-CAUSE-001: unwrap only original AggregateException first members and TypeInitializationException/TargetInvocationException inner causes, within the unchanged16-level ceiling; preserve original phase, primary failure, native exit, all resource joins and the256-byte canonical stderr contract. Emit no exception message, stack, path, class-name string, caller data or credential. Unknown causes remain Other and remain failures.

AC-C1-OUTCOME-NATIVE-CAUSE-002: the existing real child-process wrong-node/wrong-incarnation flow must return OpenStore/KeyLoad/TokenInvalidated, release the original owner and then read the original literal outcome through the healthy child. All existing malformed-input, scoped-outcome, disposal and RF3 persisted-revocation whole flows remain mandatory. New labels are diagnostic evidence only, never proof of an absent outcome or a reason to accept failed inspection. ADR-117 owns native execution/report evidence; AC-CRS-005 retains request isolation and persisted authorization. No public/persisted/storage/package/topology boundary changes; no additional ADR is required. Rollback removes the added classification/assertions only. Fresh current-source Linux build, normal/scalar process and real RF3 results remain OPEN.

## KL-001 whole source identity and platform gate

[PlatformIdentity](TestInfrastructure/PlatformIdentity.md) is the canonical whole-task supplement to REQ/AC-TEST-004. It maps original clean restore/build, actual archive/source/license closure, managed/native ownership and explicit platform dispositions. Historical local inventories above remain historical observations; current clean delivered-source Linux evidence is pending.


## TASK-TUNIT-SCOPED-QUALIFICATION-ARTIFACT-011 — retain actual task-owned observations

REQ/AC-TUNIT-ENTRY-010 and existing REQ/AC-TEST-004 require scoped task jobs to retain their original `artifacts/qualification/**` alongside native discovery/execution reports, source/DLL/PDB bindings, code-quality and image receipts. Original745f4e37/run37951115844/attempt1 KL021 normal/scalar ZIPs each contain188 members and no authority observation files because the task-job upload omitted this existing fixture-owned directory; that is an artifact retention gap and cannot establish an absent observation or a failure cause. Preserve the immutable original reports and failed outcomes.

The existing bounded native evidence writers retain closed status/error/numeric metadata without credentials or caller payloads; no new observation, report fabrication, parser, runtime behavior, retry, suite selection or permission is introduced. BuildAndTests task-acceptance's existing always-upload step adds exactly that directory, as the full RF3 upload already does. Root owns this documented YAML join and authenticates the next original source/run/attempt/profile/API/ZIP members and native results. Existing complete mapped operations, no-skip, normal/scalar, source/image and joined cleanup gates remain mandatory. Rollback removes the added upload path only, preserving earlier evidence; CI upload success does not close an operation acceptance gate.


TASK-TUNIT-SCOPED-QUALIFICATION-ARTIFACT-011 also preserves the genuine joined B-side frame owner/selection/refusal bytes under the existing fixture-owned artifacts/qualification directory. MovementFrameObservationFixture.PreserveEvidence creates one private session-named proof directory only after the original materializer/AppHost/readers and all18 native locks join; it keeps the exact bounded native bytes through the original owner writer and never changes execution or diagnostic authority. The original temporary controls retain their independent lifecycle. Source/frame/entry/cap equality, whole refusal→same-cohort restore→fresh grant→same-move full SDK/MCP/Q1/cold flows and original Linux evidence remain mandatory.

## TASK-TUNIT-TASK-NATIVE-FIFTY-012 — apply the selected concurrency to task callers

Owner direction2026-10-09 and REQ/AC-TUNIT-ENTRY-010 require the same50 native ordinary functional slots in scoped task execution as in the full caller. The original task contract/producer/verifier still pin20; retain those immutable originals as20-slot evidence. Change the current three owning files coherently to50, including actual run-tests child arguments and strict verifier expectations. Preserve all declared task/case/source identities, discovery-before-execution, native report/image/PDB reconciliation, per-selection deadlines, no retry/skip, actual Aspire ownership and always-upload artifacts. Original selected heavy RF3 fixture scheduling remains exclusive; benchmarks remain separately isolated and serial.

Root owns CodeQuality/task-acceptance.contract.json, task-acceptance.ps1 and task-acceptance.verify.ps1. Existing ADR-117 and the owner's native concurrency selection cover this caller-policy correction; no product API, storage, provider, topology or new ADR is needed. Ordered stages: contract, coherent caller/verifier edit, authentic exact-source Linux task discovery/execution and original argument reports, duration/resource/outcome comparison with earlier20-slot cohorts. Configured50 does not prove50 cases overlapped or establish performance. Rollback removes the three-file stage coherently without relabelling historical originals. Required full solution, normal/scalar, recovery and RF3 qualification remain mandatory.


## TASK-TUNIT-WHOLE-034-042-NATIVE-ADMISSION-015 — source candidates precede exact native identities

REQ/AC-TUNIT-ENTRY-010 and Search REQ/AC-SEARCH-WAIT-001–003, REQ/AC-SEARCH-ANN-WAIT-001–003, BackupRestore REQ/AC-BACKUP-CLUSTER-001–006 and original KL034/KL042 predicates require additive independent Linux normal/scalar lanes. Current source candidates are24 Unit plus2 genuine RF3 for KL034, and39 supporting Unit plus11 Integration for KL042 (three whole cluster restore cases, seven actual native CLI interruption stages and the existing administrator backup parity case). These source counts, attributes and type names are not native UID, display, compiled-count or execution proof. The CLI process cuts execute within the original RF3 fixture; they do not invent a standalone Recovery suite or replace mandatory full recovery. Existing KL036 strict37 remains intact; its fourteen additional source candidates await a separate actual native binding (proposed51 source union).

The canonical contract adds only KL034/KL042 source-census selections, with exact source class/method/parameter declarations, source argument cardinality and source path; it contains no inferred native instance display or UID. Until authenticated native binding replaces censusSelections with the existing strict cases/selections schema, these cells perform only original bounded native JSON discovery against the prepared complete Release/PDB/source image. Check exact source method/typed-parameter union, source cardinality, unique real native UID, original assembly/source ranges and unchanged prepared/before/after images. Do not parse argument displays into authority. Actual native displays/UIDs remain original observations. A census-only manifest explicitly blocks execution and acceptance, exits failed after source verification and is retained by the existing always-upload step. No acceptance receipt is published. Missing, unexpected, duplicate or changed census also fails with all originals retained.

After root authenticates the source/run/attempt/profile/API/archive and original census, bind every actual display/UID/parameter/constructor source/span/image against the declared source and replace only those new census task entries with the original strict native cases schema. The unchanged execution/TRX/no-skip/source/process verifier then qualifies actual full flows. No automatic conversion, guessed enum/bool display or census-only PASS is permitted. Configured ordinary50, preserved heavy shared-resource exclusions/exclusive1, native discovery1800 seconds, Unit/Recovery30 minutes, RF3 child60 minutes and job180 minutes remain unchanged. Scalar means caller only; real persisted authentication, SDK/official MCP/Q1, native RF3, complete original deadlines/failures and fixture/image/registry cleanup remain required.

Ordered ownership: this feature and ADR117 precede the two additive task objects, producer fail-closed census branch, closed verifier/manual/workflow selectors. Root alone joins live files, builds/pushes and captures Linux originals. Existing ten task objects, strict37, strict113, all full suites and coverage obligations remain unchanged. Qualification regression is the real native census refusal followed by exact authenticated binding and complete healthy Linux operation execution; no getter/property or synthetic runner proves it. Rollback removes only the two additive lanes and census branch, preserving all originals. No product API/trust/storage/schema/provider/topology/dependency change. Source-only; native outcomes, full product, coverage, endurance and power-loss gates remain OPEN.


## TASK-TUNIT-KL036-ADDITIONAL-NATIVE-CENSUS-016 — bind the complete movement additions

REQ/AC-TUNIT-ENTRY-010 and original KL036 ClusterRouting acceptance retain the exact existing strict37 task object. The eight additional native case classes are ReceiverIssueFailover, ActiveAdjunct, FinalInstallFrame, FrameObservationScope, CleanupMatrix, ObservedFailureCold, PolicyBusyCold and PolicyEpochCold. Their current attributes declare fourteen additional source instances; proposed51 is source inventory only, with no inferred compiled count, display, constructor identity or UID.

The additive top-level additionalCensuses contract owns exact class/method/source-path/typed-parameter/source-argument declarations for KL036 only. The producer's explicit AdditionalCensus switch chooses this independent census without modifying any of the twelve original task objects. Its fresh kl-036-{profile}-additional-census evidence root retains the original bounded Integration JSON discovery, process/reader settlement, prepared/before/after source/DLL/PDB identity and final source verification. Native50 and heavy shared-resource exclusive1, discovery1800 seconds, settlement30 seconds and all existing operation/job deadlines remain unchanged. The strict verifier reuses owning exact-key/path validators for the additional group, each selection and each candidate; it requires exactly eight distinct canonical RF3 classes/selections, canonical source paths, typed source-argument shapes and fourteen declared source instances. Original native UID/assembly/source-span/image validation remains solely the existing actual discovery validator. Every original strict task/manifest/outcome predicate is retained. No operation executes in this lane; it always refuses acceptance after retaining its original manifest, even when complete discovery succeeds.

The existing KL036 normal/scalar jobs attempt this independently conditioned census after the original strict37 step. Actual complete build and source-preparation success are required; a failed original strict flow does not suppress the additional discovery. All original strict failures remain failures. The existing always-upload and image/registry cleanup own these originals. Missing, duplicate, unexpected or changed native descriptors/image/source fail closed. Root authenticates exact source/run/attempt/profile/API/archive, then binds the actual14 identities against existing37 before a separate strict51 expansion; source arithmetic or successful discovery cannot authorize that expansion or a PASS.

Ordered implementation and ownership: this feature plus ADR117 first; additive contract, exact verifier root-key/descriptor schema, restricted producer switch and independent existing-job step next; root alone joins/builds/commits/pushes/captures original Linux evidence. Verification is actual census refusal followed by authenticated descriptor/source binding and complete original SDK/official MCP/Q1/RF3 flows. No new public/trust/persistence/provider/topology/package seam, fake operation or synthetic getter case. Rollback removes only additionalCensuses, its exact verifier schema, switch and step coherently; all twelve original task objects, prior evidence and required full suites remain. Qualification remains OPEN.


## TASK-TUNIT-WHOLE-034-042-STRICT-NATIVE-BINDING-017 — original Linux census admits execution

REQ/AC-TUNIT-ENTRY-010, Search REQ/AC-SEARCH-WAIT-001–003 and REQ/AC-SEARCH-ANN-WAIT-001–003, BackupRestore REQ/AC-BACKUP-CLUSTER-001–006 and every original KL034/KL042 predicate continue TASK-TUNIT-WHOLE-034-042-NATIVE-ADMISSION-015. Authenticated Linux Build and Tests run37977314792/attempt1 at source d2323ed056153ae8bd2ec833fc316ac5863903a4 supplies both independent normal/scalar original censuses. Each profile actually discovered the same complete26 KL034 identities (24 Unit,2 RF3;11 selectors) and50 KL042 identities (39 Unit,11 RF3;15 selectors), including original parameterized native displays and typed constructor/source identities. These are original native observations, not counts inferred from declarations.

Original job/artifact bindings are KL034 normal113978740486/11639990404, scalar113978740434/11640305082; KL042 normal113978740338/11639129382, scalar113978740660/11640130291. API-authenticated archive digests are respectively bc93a9072e1c876b845c01d7ebde6940070d967aa186aa012bcc20b32ac8bbaa, f4eb890fcccdc26bc4e696f2df2c49141013920803935e427e90714ab9750229, 4f310b085a4efa45cdf48db5e74fad0878000410a803ba89173d54b140d5e2c8 and49507fc48213003256198031b0e2857fbdf9bc6c56f977a108647bf58a0df852. Every original discovery and source-verification process exited0 with joined exit/stdout/stderr/disposal and no retained failure; prepared/before/after images are identical. Native PDB-document hashes match the authenticated source tree. Native DLL/PDB SHA, MVID, CodeView identity and embedded compilation receipts are retained metadata; the archives contain no DLL/PDB bytes for independent binary rehash. No native execution/TRX/outcome or acceptance receipt exists for these census-only cohorts. Their deliberate binding-required failure remains original.

Ordered implementation: freeze this trace in TestInfrastructure and ADR117; replace only KL034/KL042 censusSelections with the existing strict selections/cases schema using those exact original arrays; root guards/joins/builds/commits/pushes; freshly discover and execute both complete Linux normal/scalar scopes through the unchanged canonical Aspire/TUnit entry; authenticate every source/image/UID/TRX/outcome/environment/process/resource-cleanup gate. The strict producer independently rebinds the actual new compiled image before execution. Existing ten task objects and ordinal raw arrays, KL036 strict37 and its separate eight-selection/fourteen-source-instance additional census, all twelve task identities, ordinary50/heavy exclusive1 and every original timeout/auth/RF3/SDK/official MCP/Q1 requirement stay exact. No producer/verifier/workflow bypass, synthesized UID/display, timeout change or classification promotion is introduced.

Root owns live integration and Git/Linux delivery; the qualification owner retains both-profile original API/archive/native-identity ledgers and repairs actual complete-flow failures. Rollback restores only these two original census task objects and this additive trace, retaining immutable originals and every other object. No product/persistence/trust/provider/topology boundary changes. Strict execution admission is prepared; complete current-source operation acceptance, required full suites, coverage, endurance and power-loss gates remain OPEN.
