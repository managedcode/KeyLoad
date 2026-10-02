# Orleans foundation execution plan

Objective: fulfill ADR-036 and the owner's Orleans/TUnit/Docker/client corrections before continuing BlobStorage and MCP ClientApi implementation. Original architecture v0.3 scope remains active.

Testing methodology and acceptance are in [acceptance](orleans-foundation.acceptance.md). Tests are CI-only. Lead performs development build, static analysis, format and governance checks; a successful historical test run cannot qualify these changes.

| Task | ACs | Owner/model/permissions | Dependencies and start condition | Artifacts and join condition | State |
|---|---|---|---|---|---|
| TASK-REP-REVIEW | REP-001/003; ROUTE-003 | Strong inherited architect, read-only | Current policy/source; may run before writes | Exact lifecycle/public API review; lead resolves hazards | complete; reviewed exact Orleans 10.3.1 source, early Init and shutdown ordering |
| TASK-TEST-MIGRATE | TEST-001 | Cost-efficient capable TUnit worker; existing tests only | Accepted owner correction, feature/ADR/acceptance contract | Preserve every assertion/scenario; compile fixes and full diff packet; no central edits or local tests | source complete; integrated compile and CI pending |
| TASK-REP-CONTRACT | REP-001 through 005 | Lead/integration owner | Bootstrap review and fixed-voter invariants | Shared log/transport/snapshot DTO/interface contract and focused failure tests first | source frozen and extracted into configuration/message/snapshot/log contracts; integrated protocol qualification pending |
| TASK-REP-LOG | REP-002/004 | Capable worker; only new durable-log/snapshot feature paths | Frozen lead contracts and scenario names | Real atomic-store metadata and bounded snapshot implementation; no transport or central edits | source joined; log-reader/apply/dispatcher extractions reviewed; strict Replication build succeeded, recovery CI pending |
| TASK-REP-TRANSPORT | REP-001/003; ROUTE-001 through 003 | Lead, Orleans feature paths | Review and log contracts | Grain Services, replica coordination, unique request grains, secure envelopes, directory/migration | native source and unique request routing integrated; strict Orleans/Server build and RF3 CI pending |
| TASK-REP-ORLEANS | REP-001; ROUTE-003 | Strong reviewed Orleans worker; only new Orleans ClusterReplication files | Accepted lifecycle review, IReplicaEndpoint/IReplicaTransport contracts, installed Orleans skill | Early Init service and lazy direct-address client, authenticated byte[] envelopes, generation rediscovery, stop at RuntimeStorageServices; lead integrates host/config | native service/client/MAC/discovery lifecycle source joined; early system-target dependency repaired and published; consumer CI pending |
| TASK-REP-REPLAY | REP-006 | Orleans worker; prior transport files and new native security tests only | Accepted bounded replay/control isolation requirement | Configured fixed capacities and authenticated payload classification; retain cross-method nonce rejection, signed ResourceExhausted throttle; no fake dependencies | bounded pool and genuine security regression source joined; native strict build and actual load/RF3 CI pending |
| TASK-REP-PROCESS | REP-002/004 | Durable-log worker; new CrashHost/Recovery ClusterReplication files only | Stable log/snapshot contracts, real-process scenario mapping | Real SIGKILL/TerminateProcess vote/append/commit and verified/install interruptions, exact reopen assertions; lead adds one process dispatch hook and removes obsolete provider tests during integration | real CrashHost/reopen test source joined; legacy dispatch removed; current-schema recovery CI pending |
| TASK-REP-PROCESS-TRANSFERS | REP-004 | Durable-log worker; own new log/snapshot and process slice files only | Root appends four stable crash-boundary enum names; initial process scope complete | Real kill after transfer intent, acknowledged chunk, rejected image before cleanup and published snapshot; resume/reclaim/reopen canonical-cut proof | all four additional real crash-boundary sources joined; current-schema recovery CI pending |
| TASK-QA-ADMISSION | TEST-003; CodeQuality policy | Cost-efficient worker; only Abstractions Admission.cs/HttpAdmission.cs/Storage/KeyCodec.cs | Current mandatory diagnostics from integrated development build | Meaningful XML contract docs, braces/named keys, null validation where already specified; no type/API/policy changes or suppression | complete source/static review; integrated build and CI pending |
| TASK-QA-ABSDOC | TEST-003; CodeQuality policy | Documentation worker; only Abstractions Queries.cs/QueryAst.cs/Subscriptions.cs/ChangeFeeds.cs | Mandatory public XML and style diagnostics; fixed DTO contracts | Meaningful public XML docs, named discriminator constants and braces only; preserve collection shapes, enum values, constructor signatures and serialized forms | source joined; Abstractions strict build succeeded; serializer/regression CI pending |
| TASK-ROUTE-MEMBERSHIP | ROUTE-003; REP-003 | Strong Orleans worker; new Orleans ClusterRouting membership files only | Reviewed early GrainService attachment and fixed node-host contracts | Cancellation-aware initial transport/quorum acquisition; persisted CAS membership with monotonic heartbeats and bounded subsequent calls; root alone replaces old entry point/registration | protected persisted CAS provider source integrated; strict Orleans/Server build and native bootstrap CI pending |
| TASK-ROUTE-AUTH | AUTH-002/003; REP-005 | Capable policy worker; new Core/UnitTests Authorization files only | Protected internal membership authority decision in ADR-036 | Persisted no-key principal, immutable public boundary, membership-only operation guards and real-store regressions; lead integrates existing engine/host guards | source integrated; build and CI pending |
| TASK-ROUTE-REQUEST | ROUTE-001/002; AUTH-001 | Orleans worker; new ClusterRouting request/capability files only | Lead freezes signed request/reply/read-kind and node-administration contracts below | Distinct non-reentrant request grains, atomic-partition command actors, independent bounded read actors, persisted authorization and typed replies; real regression sources; lead replaces existing HTTP/host routing | signed unique request/capability and HTTP dispatch source integrated; Graph edges joined; SDK/MCP RF3 qualification pending |
| TASK-REP-LIFECYCLE | REP-001/004; ROUTE-003 | Durable worker; only ReplicaConsensus lifecycle/activity and new focused real-store regression sources | Independent review identifies attach/stop/dispose races | Cached shutdown, serialized attach/stop, complete draining before physical-store release even on worker failure; no public contract changes | cached stop/attach/activity drain source integrated and reviewed; protocol disposal join source present; active peer/checkpoint CI pending |
| TASK-REP-HOSTRECOVERY | REP-004; AUTH-002 | Durable worker; new UnitTests StorageRecovery files only | Root orders snapshot recovery before final protected catalog validation and adds Server test reference | Real verified pending image lacking or modifying protected identity must fail host construction after installation; reopen proves no leaked physical locks | snapshot-before-protected-catalog source and real-store regression source joined; combined strict build and CI pending |
| TASK-QA-CONTRACTDOC | TEST-003; CodeQuality policy | TUnit documentation worker; Abstractions Contracts.cs and canonical extracted contract files only | Mandatory public XML/braces/key diagnostics and approved shape-preserving extraction | Document all public contracts, named discriminator constants, standard exception constructors; feature-colocated files within limits; JSON and original signatures unchanged | canonical shared contract documentation/source joined; Abstractions strict build succeeded; full golden/regression CI pending |
| TASK-TEST-DOCKER | TEST-002; REP-003/004; ROUTE-001 | TUnit fixture worker; IntegrationTests ClusterFixture/ClusterTests/AdmissionClusterTests and new canonical helpers/tests only | Root freezes real node1/node2/node3 container topology, stable Docker-network origins and SDK response headers | Replace host PID kills with real Docker kills/restarts via Aspire-owned resources; preserve every original scenario; bounded diagnostics and topology/kill receipts | actual container kill/start, topology receipts and real SDK request-ID test source joined; fixture compile/quality integration and CI pending |
| TASK-APPHOST-DOCKER | TEST-002/003; REP-001; ROUTE-003 | AppHost worker; only new AppHost ClusterReplication resource/profile helpers | Frozen Docker graph and private unchanged LocalProfile schema | ClusterResources.Add returns actual three ContainerResource builders; bounded private profile initialization; no central Program/Dockerfile/benchmark/config edits | three Docker resources, private profile and source composition joined; AppHost strict build and Docker RF3 CI pending |
| TASK-REP-INTEGRATE | TEST-002/003; REP-001/004 | Lead, central config/AppHost/Server/docs | Reviewed complete required worker results | Docker/Aspire topology, real clients, obsolete-code/package removal | source integration in progress; DotNext product/packages removed, Docker CI job configured, native strict build and fixture joins pending |
| TASK-REP-VERIFY | all | Lead with independent strong review, CI-only tests | Integrated build/analyze/format/governance clean | Full CI, exact SHA/run/artifacts, coverage/complexity gaps resolved or reported honestly; stable main delivery | pending |
| TASK-GRAPH-OWNING-PREFLIGHT | ROUTE-003/006 | Orleans review worker; owning sibling repository read-only | Exact package/native source exposes system-target tracking defect | Read full owning policy, patch/release mechanism and real regression plan; no writes before accepted handoff | complete; clean fcc4cb7c/main, owning AGENTS/read APIs verified; release guards and inactive CodeQL identified |
| TASK-GRAPH-OWNING | ROUTE-003/006 | Upstream worker; Orleans.Graph scoped source/tests/version/release guards only | Accepted detailed ADR-036 handoff after owning preflight | Regression, required owning checks, patch commit/push, successful canonical GitHub publication and verified NuGet receipt; no workaround or protection bypass | complete; main 61746651, CI 36948546730 82/82 with no skips, CodeQL 36948546882 and Release 36948546753 successful; NuGet 10.0.6 verified and central consumer pin joined |
| TASK-GRAPH-EDGES | ROUTE-001/006 | Lead; OrleansSiloConfiguration only | Exact Graph10.0.5 builder API verified | Two ExecuteAsync request->capability edges, client entry unchanged; real RF3 flow and negative policy proof in CI | source complete; combined build and CI pending |
| TASK-REP-DISCOVERY | REP-001/005/006 | Durable worker; new Replication discovery helpers, obsolete PeerSecurity deletion and replacement recovery security cases only | ADR-036 freezes bodyless discovery contract | Bounded signed GET and nonce admission, real request tests, no legacy spool or fake handler; lead integrates native client/server constructors | bodyless discovery implementation and 28 generated genuine request/replay cases source joined; strict Replication build succeeded, CI pending |
| TASK-REP-LEGACY-TESTS | REP-001/002/004; TEST-001 | TUnit worker; four obsolete DotNext recovery files and new ClusterReplication recovery cases only | New actual log/snapshot/process source exists | Old->new assertion/scenario matrix, fill identified gaps, then remove obsolete four suites; no central/source/runtime edits | old-to-new 22-case matrix reviewed; obsolete four suites removed and foreign/truncated input plus exact canonical-cut gaps filled; CI pending |
| TASK-REP-DATA-BOUNDS | REP-005/006 | TUnit worker; new UnitTests ClusterReplication data-bound cases only | Accepted native MaximumPayloadBytes/nonce-admission contract | Actual MAC/authenticator outbound/receive/signed-failure and same-nonce valid retry assertions, no fake handler; lead integrates current byte-view schema | actual MAC/signed-failure/nonce-capacity test source joined; immutable/byte-span caller join reviewed; CI pending |
| TASK-REP-SCHEMA | REP-001/002/004/005/006; TEST-003; AC-ROC-002/005 | Lead; native protocol contracts and owned typed callers | Accepted ADR-041 + undeployed native schema handoff in ADR-036 | Read-only collections/buffers, ReadEntry naming, real immutable log batch; exact JSON/MAC/persisted identity, integrated build and regressions | production schema source joined; native test consumers, combined strict build and CI pending |
| TASK-QA-REPLICA | TEST-003; CodeQuality AC-CQ-001/005/006 | Durable worker; new Replication slice implementation files excluding frozen root schema and protocol files | Root typed log consumers joined | Meaningful XML/style/named-key/typed-catch corrections and required pre-effect null guards only; no algorithms/signatures/suppressions | source/extractions reviewed and strict Replication build succeeded; formatter and runtime CI remain pending |
| TASK-QA-ORLEANS | TEST-003; CodeQuality AC-CQ-001/005/006 | TUnit worker; new Orleans routing/replica implementation files excluding frozen root schema/protocol files | Root typed native byte consumers joined | Meaningful XML/style/named-key/typed-catch corrections and required pre-effect null guards only; no algorithms/signatures/suppressions | source and exact-file whitespace complete; integrated strict build and CI pending |
| TASK-REP-SCHEMA-TESTS | REP-002/004/005/006; AC-ROC-002/005 | Durable worker; existing native ClusterReplication unit/recovery/CrashHost test sources only | Frozen ADR-041 and TASK-REP-SCHEMA production signatures joined | Adapt immutable entries, read-only payload spans and ReadEntry/ReadOwnedValue calls; retain every MAC, bound, replay, ownership and actual-process assertion; no production/config changes | seven-file typed test join reviewed; original test/argument/assertion inventory preserved; integrated test build and CI pending |
| TASK-RF3-FAILURE-REVIEW | REP-001/004; ROUTE-001/002/003; TEST-002 | TUnit reviewer, read-only Server/native lifecycle and Docker SDK tests | Current integrated source and released Graph dependency | Exact failure/cancellation/migration hazards and missing real-CI proof matrix; lead owns decisions before implementation | read-only source/API reviews complete; hard stop requires process supervision, direct-client/migration and empty-replica CI scenarios still pending |
| TASK-REP-STRICT | TEST-003; CodeQuality | Replica worker; seven exact existing diagnostic files | Actual integrated build log and accepted mechanical corrections in ADR-036 | Fix typed applied key, shadowed name, null simplification, explicit owned-lifetime scheduling, cached bounded log and two unused values/imports; no behavior/suppression changes | source joined and reviewed; strict Replication build succeeded; interrupted silent formatter is unverified and CI pending |
| TASK-REP-JITTER | REP-003; CodeQuality | Lead helper/state; worker owns one new pure Recovery test file | Existing configuration timing invariants and accepted secure tick sampler | Real RNG range tests first, exact tick interval helper, integrated strict build and CI; RF3 failover remains mandatory | tests-first source and production helper joined; strict integrated build and CI pending |
| TASK-REP-DISPOSE | REP-004; CodeQuality | Lead lifecycle files; worker owns two new real-store regression files | Independent extraction/ownership review and accepted disposal contract | Idempotent terminal cleanup after complete drain, worker fault still releases gates; tests first; active RF3 shutdown qualification remains open | real-store blocked/repeated/provider-failure tests and source joined; complementary helper-fault regression, strict build and actual CI pending |
| TASK-REP-DISPOSE-HELPER | REP-004; CodeQuality | Review worker; one new Recovery helper test file only | Frozen internal shutdown-helper signature and actual provider conversion limitation | Genuine filesystem task failure plus real CTS/channel/gates; prove terminal cleanup and preserved error, no substituted application dependency or active-node claim | test source joined and reviewed; exact-file whitespace passed; integrated compilation and CI pending |
| TASK-QA-ORLEANS-STRICT | TEST-003; CodeQuality; ROUTE-001/003 | TUnit worker exact imports/guards/check only; root owns error/scheduler/schema decisions | Actual 25 diagnostic build and accepted ADR-036 correction contract | Preserve context, cancellation, signed wire shape, typed recoverable errors and bounded diagnostics; strict build and genuine clients in CI | mechanical source joined and imports corrected against the actual compiler; root scheduler/error/schema changes joined; strict build and CI pending |
| TASK-ROUTE-WIRE-GOLDEN | ROUTE-001; AC-ROC-002 | Replica worker; one new UnitTests ClusterRouting file only | Frozen original payloadBase64Url wire name/Id 7 and upcoming EncodedPayload CLR rename | Handcrafted envelope JSON round-trip and SHA equality, real database signing/verification and JSON required-field rejection; no runtime actors claimed | seven tests-first generated cases reviewed and CLR rename joined; exact-file whitespace passed; integrated compilation and CI pending |
| TASK-REP-DISPOSE-REVIEW | REP-004; CodeQuality | Strong review worker, read-only joined lifecycle/helper/PartitionHost files | Root cached complete Consensus disposal and reviewed Materializer sources | Independent concurrent/fault/ownership review; exact actionable findings and source/CI distinction; root alone resolves findings | source review complete; concurrent disposal, stop publication and physical cleanup findings fixed; active-node CI pending |
| TASK-QA-SERVER-STRICT | ROUTE-001/003; AUTH-001; TEST-003; CodeQuality | TUnit worker; QueryApi, ApiEndpoints, DatabaseIdentityMiddleware, OrleansSiloConfiguration and PartitionHost imports only | Orleans strict build succeeds; actual Server diagnostic log and frozen root correction contract | Add missing Query namespace, bind four IResult routes as typed route handlers, explicitly preserve default path comparison, remove three proven unused imports; no lifecycle/auth/schema/suppression changes | reviewed source joined and strict Server build succeeded; actual SDK response/denial CI pending |
| TASK-SERVER-FAILURE-OBSERVER | ROUTE-003; REP-004; CodeQuality | Durable worker; new Server ClientApi helper and one new UnitTests ClientApi helper test file only | Frozen actual-stage task-observation contract in ADR-036 | Observe synchronous/async actual filesystem errors and task cancellation without Task.Run, lost faults or exception suppression; tests-first source and exact whitespace; root integrates lifecycle | reviewed source and eight real filesystem/cancellation cases joined; helper strict compilation succeeded, TUnit compilation and CI pending |
| TASK-SERVER-LIFECYCLE-JOIN | ROUTE-001/003; REP-004; AUTH-001; CodeQuality | Root; Server composition/visibility/lifecycle and central friend declaration | Accepted strict lifecycle contract plus independent review; root ownership cannot overlap native worker scopes | Cached startup/stop task ownership, explicit middleware construction, safe typed error mapping, preserved full cleanup and internal server types; combined strict build then real failure CI | source joined and strict Server build succeeded; review findings fixed with stop prepublication and explicit uncancellable drain; actual lifecycle RF3 CI pending |
| TASK-SERVER-CLEANUP-ERRORS | REP-004; ROUTE-003; CodeQuality | Durable worker owns only observer helper/test extension; root owns PartitionHost/PartitionStores callers | Independent P2 masking finding; accepted inline Action observation and exact terminal throw contract | Preserve original single error or every ordered aggregate error through real helper tests; replace masking cleanup while retaining ownership order and cached completion; strict build and CI | helper/tests and explicit physical close barriers joined/reviewed; strict Server build succeeded; actual regression CI pending |
| TASK-TEST-DOCKER-DISCOVERY | REP-001/005; TEST-002; CodeQuality | TUnit worker; ClusterFixture diagnostic path and cohesive new TestInfrastructure/ClusterReplication helpers only | Frozen bodyless discovery route and actual PeerSecurity constructor; root owns model/central files | Real signed discovery diagnostics bounded before materialization, system clock, exact RF3 model and every existing test scenario retained; source then strict compilation and CI | bounded signed discovery source joined and reviewed; exact-file whitespace passed; integrated compilation and CI pending |
| TASK-MCP-PREFLIGHT | AC-CLIENT-006/007; TEST-002 | Strong Orleans/SDK reviewer, read-only | Proposed ADR-039, canonical typed operation and auth source | Current official SDK package/source/transport/auth/schema/cancellation and exact all-operation adapter recommendation; root freezes contract before any implementation | complete; SDK 2.2.0 source/feed and 37-operation inventory reviewed; accepted follow-on contract and tasks are in mcp-agent-api.plan.md |
| TASK-TEST-CRASH-STRICT | REP-002/004; TEST-001/003; CodeQuality | Durable worker; CrashHost Program/native scenario files and new canonical StorageRecovery helpers; three legacy Recovery imports only | Actual 65 CrashHost compile diagnostics and accepted exact process-contract preservation | Internal namespaced executable types with Recovery friend, thin dispatcher, named unchanged marker/mode/path/JSON constants, system clock, actual async flush/terminal pause outside synchronous durable hooks; every existing crash boundary and argv preserved | complete source joined and reviewed with original scenario/marker/operation order retained; integrated compilation currently blocked in independently owned analyzers; CI pending |
| TASK-TEST-DOCKER-FIXTURE-STRICT | REP-001/004/005; TEST-002/003; CodeQuality | TUnit worker; ClusterFixture and cohesive new IntegrationTests ClusterReplication/TestInfrastructure helpers only | Joined signed bounded diagnostics; accepted exact scenario/topology contract below | Meaningful XML, system clock, named existing fixture constants and cohesive extraction within size limits; preserve actual resource/SDK/diagnostic/lifetime behavior and every test; root owns build/CI | approved; source pending |

Baseline: main CI [36932045983](https://github.com/managedcode/KeyLoad/actions/runs/36932045983) succeeded for unit, process recovery and RF3 on Linux/Windows/macOS, plus comparison smoke, at 9c570f8c33a7a9667507a8e1c0ca68860de3be45. This is the committed historical implementation, not qualification of the current Orleans/TUnit migration. Current-policy tests remain CI-only.

Transport handoff contract: `IReplicaEndpoint` is node-owned; `TransportReady`, synchronous `AttachTransport(IReplicaTransport)`, `HandleAsync(ReplicaRpc,string,CancellationToken)` and `StopAsync(CancellationToken)` are frozen. `AttachTransport` starts maintenance and signals local transport readiness without waiting for quorum. `IReplicaTransport` keeps its frozen string payload API internally, while the Orleans wire envelope carries the exact UTF8 payload as `byte[]` with generated serializers and stable field IDs. ReplicaProtocolCodec preserves command payload bytes via base64 instead of recursively escaped JSON; both durable entries and replica RPC payloads use it. Shared JSON/public-client encoding is unchanged. The Orleans worker owns only new `src/KeyLoad.Orleans/Features/ClusterReplication/` service, client, envelope/security, discovery DTO/state/options and lifecycle files; it must not edit existing routing/membership, central config, tests or host composition. Lead owns consensus, PartitionHost and integration. New transport types are the approved frozen handoff boundary, not permission to invent application APIs.

TASK-REP-REPLAY explicitly extends that worker's test ownership to `tests/KeyLoad.UnitTests/Features/ClusterReplication/ReplicaTransportSecurityTests.cs`; no other test/central files. Critical consensus capacity is reserved independently from Forward, ReadBarrier and data Append. Authenticated Append with no entries/noop entries or only existing CommandAdmissionGovernor control kinds is critical; untrusted/invalid payload classification cannot enter the reserve. Saturation is a typed retryable capacity error, while an actual nonce replay remains authentication failure. The lead's follower sender sends a bounded empty heartbeat when a data Append is throttled, so pending data cannot suppress liveness. All capacities remain configured and memory-bounded.

TASK-REP-PROCESS owns only new `tests/KeyLoad.CrashHost/Features/ClusterReplication/ReplicaCrashScenario.cs` and cohesive new helpers there, plus new `tests/KeyLoad.RecoveryTests/Features/ClusterReplication/ReplicaProcessRecoveryTests.cs` and cohesive process-harness helpers. Lead owns existing CrashHost.Program dispatch and all obsolete DotNext source/test removal. Every scenario pauses the real helper at a durable boundary, the parent actually kills it, and reopen validates the promised metadata/canonical cut. Callback-only tests remain complementary. No local helper/test launch is authorized.

Known current compile failures introduced by automatic TUnit conversion: nullable assertion results, lost collection assertion loops, async helper calls and fixture lifecycle interface namespace. These are TASK-TEST-MIGRATE fixes; every original check must remain. Shared checkout also has independent governance/analyzer/comparison work; preserve it and serialize central-file edits. No required dependency may join as idle/partial/unverified.

Current development-build prerequisite failure: the first UnitTests compilation
stops in independently owned KeyLoad.Comparisons at 16 diagnostics (public arrays,
exception naming/constructors, enum naming and dynamic PostgreSQL CommandText).
Comparison/quality leads actively own those files. Do not bypass analyzers or build
UnitTests against stale comparison outputs; preserve their ownership and rejoin the
actual prerequisite after its repair. This is a compilation result, not a failing
TUnit runtime case or permission to skip the suite. Recovery/native source work
continues independently.

The second RecoveryTests development build stopped before CrashHost in eight new
KeyLoad.Analyzers diagnostics introduced by the independent quality task. Preserve
that owner's files and rejoin their actual repair. Neither this prerequisite nor
the comparison prerequisite permits stale-output builds or analyzer suppression.
The integrated Server dependency chain previously compiled with zero warnings and
errors; it must be rebuilt against the completed quality rules before delivery.

The third build now passes the analyzer project and stops in seven independently
owned Security/ZoneTree diagnostics: control nesting, an 88-line install method,
488-line file and 1051-line aggregate storage type. The quality owner has begun
actual structural repairs. Native tests still have no compilation or runtime
qualification against these completed rules.

Independent MCP contract/catalog work may proceed under the accepted
[MCP plan](mcp-agent-api.plan.md) while native qualification prerequisites are
repaired. Its rollout and real-client qualification still depend on this native
foundation. This does not close any pending Orleans/RF3 acceptance item.

TASK-TEST-DOCKER-FIXTURE-STRICT is a source-quality correction of the existing real
fixture, not a new test topology. Keep its exact Docker/Aspire resources, private
profile/path permissions, SDK keys/requests, signed bounded diagnostics, test
markers and initialization/disposal sequence. Extract only cohesive log capture
or diagnostic ownership when necessary for 400/200/50 limits; do not use partial
types or change tests to doubles. Hard startup/shutdown supervision remains a
separate pending contract and must not be invented during this quality task.

Final ordered commands: solution restore; Release build/analyze with current warnings-as-errors; formatter verification; static governance inventory; validation-ref GitHub Actions unit/recovery/Docker RF3/comparison gates and artifacts; independent diff/evidence review; stable main push and exact remote verification. The owner authorized installing Orleans through `dotnet skills`; Orleans 3.1.1 from catalog 2026.10.1.0 is now installed and read, including scheduling/services and hosting guidance. Apply it to lifecycle/bootstrap/state ownership, then review those guarantees before CI. Other installation remains prohibited. Build and static results are not runtime qualification.

TASK-ROUTE-REQUEST handoff: `GrainRequestEnvelope` signs purpose, unique request
GUID, incarnation, persisted principal ID, exactly one read/command kind, stable
write command GUID, exact UTF8 payload encoded as base64url, and expiry. Generated
serializer member IDs and aliases are stable. `GrainOperationReply` carries bounded
`ReadOnlyMemory<byte>` JSON or a typed safe error; never serialize exception objects.
`IRequestGrain` and `IDatabaseReadGrain` have GUID keys; `ICommandPartitionGrain`
has the canonical atomic-partition string key, or a reserved catalog key for global
administration. All expose `ExecuteAsync(string,CancellationToken)`. Request actors
route once and deactivate on idle. Read actors acquire their own quorum barrier,
reload persisted authorization and execute existing typed Core/Query/Search APIs;
there is no trusted client role or direct HTTP engine bypass. `INodeAdministration`
is a borrowed node-local interface, not a storage-owning grain. It exposes backup,
admission and status only after a persisted administrator check. This milestone
routes many atomic partitions within one fixed RF3 replica group; it does not claim
physical sharding or distributed-query qualification.

Root owns the shared contracts, Query project reference, composition, HTTP adapters
and docs. Worker owns only new request/capability helpers in the Orleans
ClusterRouting slice and new ClusterRouting unit regression files. Preserve all
existing public JSON contracts. Tests cover distinct request IDs, exact large
Unicode/quote payloads, wrong purpose/incarnation/expiry/kind/key, forged or revoked
principal, stable write retries, bounded replies, and cancellation; real Docker
SDK/MCP flow and activation movement remain required CI join gates.

The request codec's approved public creation helpers accept exact UTF8 memory and
produce signed read/command strings. No-DTO read kinds accept JSON null only. Every
RF3 voter shares the configured immutable database signing key and incarnation so
tokens remain valid after routing movement. The peer option `MaxControlPayloadBytes`
is bound to the actual command governor's configured control budget; classifier
checks each decoded control payload before admitting it into the Critical pool.
Invalid oversized control payloads cannot exhaust reserved vote/heartbeat capacity.

Docker fixture contract: AppHost owns three `ContainerResource`s named node1/node2/
node3, one repository Dockerfile image, individual writable host directories mounted
at `/data`, internal HTTP port8080 and silo port11111. Each voter origin is the
container-network origin `http://nodeN:8080`; each silo resolves its own matching DNS
alias. External HTTP endpoints are obtained solely through Aspire's actual endpoint
allocation. The fixed shared signing/incarnation/peer/admin profile stays private
at the fixture root. No peer waits for another's health: all start concurrently,
and readiness requires native silo plus quorum. Test kills are actual Docker
SIGKILL of the inspected Aspire-managed container, never the container's namespace
PID on the host. Restart retains the same bind-mounted directory and new native
silo generation. Capture actual container IDs, source SHA, image inspection and
kill/restart acknowledgements in JSON artifacts. Unit/recovery gates stay on three
OSes; the required Linux Docker RF3 job is separate, never silently skipped.

### Native composition and test-source strict join

The actual Integration development build2 stopped at14 AppHost diagnostics, before
the RF3 test assembly: Program's feature dispatch violates KLD0020, the private
profile is unnecessarily public/global, cached CompositeFormat and existing
benchmark braces/one named configuration key are missing. This preserving stage
derives from AC-REP-001/003 and AC-CQ-001/005/008; ADR036 topology and ADR033 strict
composition remain exact. It adds no endpoint, schema, format, dependency or test
double. Move only startup composition into one typed app-owned aggregate; Program
creates the builder, calls that aggregate, builds and runs. Profile JSON field names
and generated credentials, all three Docker resources, networking, configuration,
benchmark resource order and endpoint allocation remain identical.

| Task | Owner / tier and exact write permissions | Start / dependencies | Artifacts, verification and join |
|---|---|---|---|
| TASK-REP-APPHOST-STRICT | Economical capable worker; Program.cs, LocalProfile.cs, ClusterResources.cs, ClusterProfileStore.cs and one new Hosting/KeyLoadAppHostApplication.cs only | Actual build2 inventory above; frozen preserving composition | Same RF3 composition in a thin runner, private namespaced profile, actual cached native CompositeFormat; root reviews all source then strict Integration build and genuine RF3 CI |
| TASK-REP-TESTSOURCE-STRICT | Existing capable native/TUnit worker; only Unit Features/ClusterRouting/GrainRoutingAuthorizationTests.cs, GrainRequestCodecTests.cs, GrainRequestEnvelopeWireTests.cs and Features/ClusterReplication/ReplicaTransportSecurityTests.cs, ReplicaControlPayloadSecurityTests.cs, ReplicaApplicationPayloadBoundsTests.cs, ReplicaConsensusLifecycleTests.cs | Actual Unit build1's18 native test diagnostics; ADR036 and ADR041 stable contracts | Preserve every original test/case and exact signed bytes; null-safe throws, internal fixture types, ordinal string operation, observed CancelAsync, unused imports; source/assertion map then root actual Unit build and exact-SHA CI |
| TASK-REP-APPHOST-BENCHMARK-STRICT | Root only; existing BenchmarkResources.cs | Disjoint source inventory, existing ADR033 preserving style rule | Braces, removal of verified unused import and one named key only; original resources/order/values intact; integrated build/format then real comparison CI |

These workers perform no builds, tests, recovery helper or AppHost/container run,
no policy/config/contract edits, no suppression or assertion deletion. Stop for
unsupported actual API, changed cancellation ordering, overlap or new boundary.
Source-private visibility and aggregate extraction need no invented test mirroring
their implementation: existing genuine Docker SDK/MCP/comparison suites remain
the mandatory observable behavior proof. Native test migration retains every
security/replay/lifecycle assertion and does not bypass real runtime qualification.
Root serializes final graph builds, formatter and governance, joins every complete
packet, then qualifies only an exact delivered GitHub SHA. Current Unit build2
also observed incomplete independent BenchmarkScenarios reference publication;
leave that active owner's project/central/inventory work untouched and re-restore
after its completed join. Neither development failure is a failing executed test.

AppHost build3 refinement: actual KLD0020 accepts named AddKeyLoad/BuildKeyLoad/
UseKeyLoad/MapKeyLoad/RunKeyLoad aggregate prefixes; use the existing rule's
AddKeyLoadApplication name and remove the verified implicit-global unused import.
Do not weaken or change its analyzer. Explicit braces brought BenchmarkResources
Add to57 code lines; root extracts only the unchanged two runner-wait/configuration
loops to one cohesive private helper in that same owned file, naming the existing
configuration/environment prefixes. This is preserving composition with the same
order and conditional values. No quality exception, public API or topology change.

Integration build4 reached the real test assembly after the AppHost source join.
Its actual static inventory includes typed ContainerResource environment selection,
Aspire.Testing endpoint-extension imports, the native obsolete command error member,
fixture ownership/observer disposal, native test visibility, named keys and braces.
These are development diagnostics, not failed executed scenarios. The disjoint
preserving prerequisites below retain all original RF3 test methods and assertions.

| Task | Owner / permissions / ACs | Dependencies / artifacts / join |
|---|---|---|
| TASK-MCP-CALLER-STRICT4 | Economical capable worker; only22 originally authored MCP Integration caller files; AC-MCP-001/002/003/005/007 | Actual build4 native nullable Throws and private fixture diagnostics; fix exact nullable results/internal test classes, preserve all16 original caller cases and real SDK APIs; root reviews/builds/CI |
| TASK-REP-CONTAINER-STRICT4 | Capable native worker; ContainerRuntimeControl.cs and new ContainerRuntime-prefixed private helpers only; AC-REP-001/003 | Actual strict diagnostic inventory; native Message instead of obsolete ErrorMessage, exact constants/braces/static/private coherent extraction <=400/200/50/3; same genuine inspect/kill/start/health/timestamp/ID/SHA and receipt; root reviews/builds/CI |
| TASK-REP-CALLER-STRICT4 | Economical TUnit worker; only ClusterTests.cs, AdmissionClusterTests.cs, Features/ClusterRouting/RequestIdReceiptTests.cs and RequestIdResponseRecorder.cs; AC-REP-003/006, AC-ROUTE-001 | Actual strict inventory; existing Aspire.Testing public extensions, exact named keys/braces/visibility, null-safe native failure assertions, same atomic/admission/request-ID oracles; root reviews/builds/CI |
| TASK-REP-FIXTURE-STRICT4 | Root only; ClusterFixture.cs, ClusterFixtureDiagnostics.cs and feature-owned fixture helpers; AC-REP-001/003/004 | Fix actual typed container selection/HTTP ownership and diagnose lifetime faults separately with first genuine regressions before changed cleanup semantics; native external supervision remains mandatory pending work |
| TASK-MCP-UNIT-STRICT3 | Economical TUnit worker; only Features/ClientApi/McpFrameBodyTests.cs and ServerFailureObserverTests.cs; AC-MCP-003/004/005, AC-ROUTE-003/REP-004 | Actual Unit build3 has native nullable Task, internal visibility, FileStream transfer ownership and async disposal findings; preserve every original real-file/task case and assertion, no policy/analyzer changes; root reviews/builds/CI |

No source worker launches a build, test, Docker CLI, AppHost or helper, changes
policy/dependencies/central settings/contracts, deletes assertions, or reports
runtime success. Existing test methods/cases and genuine OS/Aspire/network
dependencies are the behavior proof; preserving source syntax needs no fake or
implementation-mirroring test. Stop on new behavior, structural scope overlap,
unsupported actual API or changed native supervision/receipt semantics. Root joins
each packet and owns formatter/actual compilation and exact-SHA GitHub qualification.
