# Isolated Linux comparisons acceptance

## Goal, scope and authority

Owner direction 2026-10-03: one isolated Linux runner per engine × actual node count × workload scenario; shared serious workload; JSON per runner; collect the complete cohort, then regenerate and qualify the performance website. Preserve production RF3, Orleans request isolation, node-local stores, persisted authorization, real SDK/MCP clients, all existing qualification suites and historical evidence. This is explicit authorization for benchmark-only fixed-membership RF1/RF2, not a production topology or availability claim.

In scope: existing nine-engine native comparison catalog; actual nodes1/2/3; eight existing scenarios plus document update/delete (DocumentWrite means unique create); common bounded intensive options; typed selected-cell composition, native topology proof, raw per-worker schema4 envelope, strict manifest aggregation, authenticated aggregate provenance and site ingestion/qualification. TimeSeries retains its separately specified common workload and must receive isolated target/node/scenario cells before its data joins the same publication cohort; no silent omission or merging incompatible data/oracles. Out of scope: Enterprise licensing, independent nodes relabelled as clusters, local benchmarks, power-loss/production superiority claims, brand/layout redesign and dependency substitutes.

Actors: trusted GitHub workflow owns case identity; Aspire owns only the selected engine and load generator; comparison library owns data/oracles/measurement; authenticated GitHub API owns job/artifact proof; aggregator owns immutable bytes and completeness; independently qualified current trusted-main site owns rendering; Pages/OIDC writes belong only to the final deploy job. Clients never choose trusted database roles.

## Frozen initial intensive profile

`intensive-1k-c16`: seed1729, documents4096, operations10000 per repetition, warmup256, repetitions5, concurrency16, payloadBytes1024, dimensions128, topK10, graphVertices256, graphFanOut3, graphDepth3, timeoutSeconds30. These are reproducible workload settings, not measured values or a maximum-performance assertion. Each measured case gets fresh isolated storage; warmup/preparation/initial state/oracle are outside timing. CPU/allocation/RSS remain labelled generator counters; server/container resource facts require separate measured counters.

## Acceptance criteria and automated strategy

| ID / requirement | Pass / fail contract | Tests / verification |
|---|---|---|
| AC-ISO-001 / REQ-BC-050 | CI and every case use Linux; full build, analyzers, formatter, unit/scalar, recovery, RF3 remain. macOS/Windows no longer scheduled. | source-owned TUnit workflow/matrix inventory + exact-SHA CI jobs |
| AC-ISO-002 / REQ-BC-051 | Closed nine-engine × native1/2/3 × ten-scenario plan. Exactly one cell on each fresh runner; no foreign engine resources/credentials/volume. Unknown/duplicate identities fail before allocations. Split matrix families below256 each without dropping cells. | strict cell/plan TUnit, real Aspire model and Docker/resource inventory for every case |
| AC-ISO-003 / REQ-BC-052 | Genuine membership/copies and native ACK/read contracts prove requested count. Neo4j Community2/3 is explicitly unsupported with reason/null data. Missing native proof/startup failure is failure, never unsupported. | native adapter receipts + actual engine cases and topology mismatch rejection |
| AC-ISO-004 / REQ-BC-053 | Production default rejectsRF1/RF2. Explicit benchmark fixed membership1/2/3 retains majority=floor(n/2)+1, same Orleans/ZoneTree/journals/auth/storage. RF2 loses read/write quorum if either voter stops; RF1 has no fault tolerance, but process restart restores acknowledged data. RF3 regression gates unchanged. | configuration TUnit negative/positive + real Docker/Aspire SDK/MCP topology/restart/quorum cases |
| AC-ISO-005 / REQ-BC-054 | Intensive scenario uses same options, corpus and independently checked operation plan. Create requires absent unique ID; update requires existing ID and changed exact body; delete requires present ID and verifies absence; unaffected records remain unchanged. Fresh unique operation IDs prevent unintended revision races. Failed attempts and latency stay retained; no retries inside timed operations. | deterministic corpus/operation-plan tests + real native CRUD cardinality/body/absence/negative assertions |
| AC-ISO-006 / REQ-BC-055 | Worker emits schema4 envelope with closed cell identity and original schema3 single-target/single-scenario raw report. Preserve actual per-runner host/runtime, source/run/attempt/job/profile, image digests, membership and samples. Legitimate report GUIDs differ; shared cohort identity/options/corpus must match. Unsupported topology has reason and no report/measurements. | real host configuration/process tests, exact report identity checks and malformed JSON negatives |
| AC-ISO-007 / REQ-BC-056 | Aggregation waits for all cells. Authenticate exact successful jobs/artifacts and digest bytes; reject missing/duplicate/foreign/stale/mixed/failed/skipped/cancelled results or inconsistent sample cardinality/metrics. Preserve every raw JSON byte and per-worker facts; no aggregate host or pooled percentile invention. Failure leaves publication unchanged. | TUnit real Node/files/CLI success and corruption matrix; actual GitHub join manifest |
| AC-ISO-008 / REQ-BC-057 | Site builds metrics solely from complete validated aggregate. Actual1/2/3-node and scenario controls correspond to raw cases. Unsupported cells are unavailable/unranked. Preserve median/range arithmetic, raw downloads, accurate generator labels and site/measured/control revisions. | existing full SiteTests plus new aggregate/node/scenario validator/oracle/browser cases; native coverage thresholds |
| AC-ISO-009 / REQ-BC-058 | Separate Pages workflow verifies successful aggregate and every closed worker proof, raw/archive hashes, current attempt/freshness; runs all site/TUnit/browser/coverage gates before deploy. Failed cohort cannot refresh metrics. Historical schema2/3 retain original source/contracts and cannot substitute for current qualification. | stateful GitHub selection/archive/freshness regressions and actual successful provider/live publication receipt |

Every criterion fails if its required evidence is absent. Manual evidence exceptions: visual judgment of new node/scenario controls and inspection of native membership/ACK semantics against primary engine documentation; automated real browser assertions still mandatory. Root numeric coverage80/70 and critical90 remain open until authentic collection; configuration/static checks do not pass them.

## Frozen wire/join contract for first source stage

Root owns shared selection `ComparisonWorkerSelection(Target, NodeCount, Scenario, Profile)`; configuration keys `Benchmarks:Target`, `Benchmarks:NodeCount`, `Benchmarks:Scenario`, `Benchmarks:EvidenceProfile`. Target names are existing exact profile names. Valid node counts1..3 and defined scenario/profile are mandatory. ComparisonTopology adds native `TwoNode` representation; old Standalone/Replicated remain historical1/3. Generic runner optional selected scenario reuses identical common measurer; legacy callers preserve existing overload behavior.

Raw worker layout: `workers/<safe-cell-id>/worker.json`, image/runtime/test receipts in the same worker directory. Envelope has `schemaVersion:4`, `worker:{target,nodeCount,scenario,profile,sourceRevision,runId,attempt,repository,ref,workflow,jobId}`, `disposition:measured|unsupportedTopology`, `reason:null|string`, `report:<original single-target single-scenario ComparisonReport>|null`. No missing failed worker is fabricated as an unsupported record. Aggregate manifest has `schemaVersion:4`, exact cohort/workload identity, expected cell plan, complete authenticated job/artifact/hash entries and relative raw paths; preserves each worker report rather than flattening it into one schema3 host.

## Compatibility and rollout

No acknowledged database format or SQL API changes. Explicit benchmark-only voter validation is opt-in and cannot silently reopen an existing store with changed voters/incarnation. Source rollback restores producer/validator/site together; previously published evidence remains immutable. The new producer must not refresh latest until its complete raw cohort, site qualification and provenance checks pass. Every future change to selection/envelope/plan must update this file, mapped tests, ADR-056 and ordered plan before work continues.

### Planner/proof/aggregate wire freeze (TASK-ISO-006/007)

Canonical source is `benchmarks/KeyLoad.Comparisons/Features/BenchmarkComparisons/isolated-contract.json`; C# and Node read the same feature-owned versioned artifact. Plan schema1: `{schemaVersion:1,workerSchemaVersion:4,profile,options,cells:[{id,target,nodeCount,scenario,profile,family}],matrices:{crud:{include:[cells]},specialized:{include:[cells]}}`. `id` is stable safe target/node/scenario slug; `family` is crud|specialized. Exactly108/162 entries, all270 unique; cohort context is separate. Planner exports `readIsolatedContract`, `createIsolatedPlan`, `validateIsolatedPlan`; CLI optional `--output=<newpath>`, `--github-output=<existingpath>` emits fullplan/stdout and crud_matrix/specialized_matrix. No measurement or actual-node claim comes from plan configuration.

Authenticated transport supplies proof schema1: `{schemaVersion:1,cohort:{sourceRevision,runId,attempt,repository,ref,workflow,profile},cells:[{id,job:{id,name,url,conclusion,steps:[{name,conclusion}]},artifact:{id,name,sizeInBytes,digest,expired},workerSha256}]}`. Job names are `case / <id>`; required steps `Run isolated native case` and `Retain isolated worker evidence` must succeed. Artifact name `comparison-worker-<id>`, nonzero size, unexpired, immutable sha256 ZIPdigest verified by transport; workerSha256 is actual extracted raw byte hash. Transport authenticates GitHub run/attempt/job/artifact response and exact archive bytes; validating supplied proof alone cannot establish GitHub authenticity. All cells/IDs must match plan; job/artifact identities are distinct and correspond to envelope worker.jobId/cohort.

Aggregate schema4: `{schemaVersion:4,cohort,profile,options,datasetSha256,workers:[{id,target,nodeCount,scenario,profile,disposition,reason,rawPath,rawSha256,job,artifact}]}`. Preserve raw bytes under `workers/<id>/worker.json`; collect one input worker at a time. No global host/runtime field or recomputed pooled percentile. Options omit varying topology; nested report topology is Single|TwoNode|Replicated for actual1|2|3. Existing cluster fields `{nodes,dataCopies,state,observations}` prove requested count when topology/scenario is measured or capabilityunsupported with a real target report. Unsupported scenario still has five cases with detail/nonempty reason, measurement:null,samples:[]; only expected Neo4j native2/3 topology uses nullreport. Source/run/attempt/profile/architecture/Linux/corpus match across reports; inner report GUID may differ.

Supported scenario contract: KeyLoad/PostgreSQL all10; Redis read/create/update/delete; Neo4j native1 read/create/update/delete/graph-neighbors/graph-traverse; MongoDB those6 plusstreamappend/read; OpenSearch read/create/update/delete/vector; Qdrant vector only; RabbitMQ queue only; KurrentDB streamappend/read only. Whole worker fails on failed measured attempts, missingnativeproof or incorrectcardinality; no failed cells can masquerade as unsupported. Exact options/repetition/sample/metric consistency applies to supported cases. Producer/adapters must implement this support before the full cohort can qualify.

AC007 bounded test refinement: pure actual production validators receive one complete supported50k-sample contract input plus legitimate unsupported topology/capability cases and controlled corrupt copies; real CLI tests prove path/proof/identity/missing/duplicate/failure atomicity without repeatedly manufacturing severalGiB of unit data. These are controlled contract inputs, never performance artifacts. Complete270-cell positive merge is mandatory real GitHub qualification using actual measured workers, not an exception to completeness. Aggregator API exports `validateWorkerEnvelope(value,cell,cohort,contract)`, `validateAggregateProof(value,plan)`, `aggregateEvidence({input,output,plan,proof})`; CLI `aggregate-cli.mjs --input=<absolute> --output=<new isolated absolute> --plan=<file> --proof=<file>`.

### Persisted fixed-membership safety refinement

Audit found existing replica hard state fences incarnation but does not persist the full voter set. AC004 therefore requires a versioned private benchmark-membership record in the existing node-owned replica ZoneTree IAtomicStore. On first fresh benchmark open, commit incarnation plus exact ordered voters atomically WITH initial hardstate using the existing native store commit/WAL path. If hardstate already exists and benchmark guard is absent, benchmark opt-in fails: no legacy store is silently re-bound. When a guard exists, every open (also opt-out) validates it against incarnation/voters and rejects drift or corruption. This is additive private authority metadata, no acknowledged data rewrite, separate flat-file authority or new consensus protocol. TASK005 may own new ReplicaBenchmarkMembership-prefixed feature files plus the existing ReplicaLogValidation.cs join and focused real-ZoneTree TUnit regressions. Local replica metadata remains owned independently of canonical snapshot installation; reopening/restoring it must preserve and revalidate the guard. Source rollback cannot reopen benchmark1/2 with the old runtime; no qualification may bypass identity checks.

### Selected host/resource join settings

TASK008/009 root freezes one-native-engine wiring. Native configuration is `Benchmarks:Native:Image`, `Benchmarks:Native:ConnectionString`, `Benchmarks:Native:Endpoints:<0..n-1>`, `Benchmarks:Native:User`, `Benchmarks:Native:Password`, `Benchmarks:Native:ApiKey`, and Redis `Benchmarks:Native:ReplicaConnections:<0..n-2>`. Only the selected engine's required values are read/validated and only its clients are allocated. KeyLoad uses endpoint array plus persisted bootstrap admin credential `Benchmarks:AdminKey`; expectedNodes=selection.NodeCount. Existing immutable server/runner execution identity remains mandatory for container runs, with current intense profile accepted only through an explicit isolated route. Exact cell job ID comes from trusted workflow `KEYLOAD_COMPARISON_JOB_ID`; run/attempt/repository/ref/workflow/source comes from actual GitHub context and cannot silently default.

Host route `Benchmarks:Target` selects a strict isolated application that validates selection/cohort/image before clients; one target is passed to existing common runner with one selected scenario. Explicit known unsupported Community topology writes null-report envelope without any engine allocation. Output directory is the job-owned cell directory bound through the existing `/reports` mount; raw `worker.json` uses fixed safe error output. Cleanup owns only selected native clients/targets and remains bounded; no untrusted URI credentials/connection strings in errors. Existing all-engine route is temporary migration debt for preserved baseline tests, removed with coupled tests/workflows once replacement full qualification exists; it cannot qualify new isolated performance. Removal owner root, date2026-10-10, ADR056.
## TASK-ISO-009QR approved native adapter contract

Qdrant and RabbitMQ keep their existing public constructor signatures. Native
count is `ComparisonTopologies.NodeCount(topology)`; all multi-node behavior
applies to TwoNode and Replicated. Qdrant creates one shard with RF=count and
WCF=floor(count/2)+1, acknowledged strong-ordering seed writes and exact queries
with majority read consistency for counts2/3. Every configured native endpoint
must independently report the same exact peer set, distinct own peer identity,
same version, exact collection RF/WCF and exactly one active full seeded local
shard. No extra/missing/duplicate peers/copies pass. RabbitMQ declares a durable
native quorum queue with initial group size=count and verifies exact running disc
broker/queue member/online sets, publisher confirms, persistent body and ordered
manual-ACK barrier. TwoNode therefore requires both quorum members. Profiles
record actual count/majority, no single-node-loss availability claim. Existing
bounded setup deadlines and timed operation/oracle contracts remain.

TASK-ISO-009QR owns only QdrantTarget.cs, QdrantReplicaProof.cs,
QdrantNodeProof.cs, RabbitTarget.cs, RabbitReplicaProof.cs, RabbitQuorumProbe.cs
and new NativeQuorumTopology-prefixed TUnit tests. Root owns native Aspire
composition and real container qualification. Acceptance-derived pure native
response validation/config tests precede edits; no HTTP fake or local execution.
## TASK-ISO-008M common mutation implementation contract

The shared corpus adds `BenchmarkDataset.InitialMutationState(scenario,input)`:
update uses the same owned id with a distinct exact-length text value, delete
uses the final input itself, other scenarios reject. Mutation input id ranges
are disjoint from seeded documents, DocumentWrite, all repetitions and warmups;
existing operation inputs/corpus hash remain unchanged. No session API change
or synthetic affected count is introduced. Each document adapter enforces native
affected cardinality exactly1 (KeyLoad uses required revision1); no upsert update.

Root owns dataset, common preparation/measurer/validation and corpus regressions.
Before any mutation timing, bounded workers create and read back their fresh
initial records through existing public operations and independent sessions,
under the ordinary operation deadline. Warmup state is prepared separately.
After timing, bounded independent session workers verify update exact final body
or delete absence; a seeded sentinel is verified unchanged. Any failed oracle
marks its existing raw attempt failed; sentinel failure invalidates successful
attempts, preserving times, attempts and failed samples. Native retries are not
introduced. Serious real native positive/missing/duplicate/edge CRUD acceptance
remains part of GitHub integration; corpus tests alone do not qualify it.
TASK009QR singleton refinement: native disabled Qdrant1 keeps its genuine
standalone contract: exactly one configured endpoint, disabled native cluster,
RF1/WCF1 and exact seeded collection count. Distributed2/3 and enabled singleton
must verify exact peer/own identity and active full local shard. No invented peer
set is attached to a disabled engine. Historical genuine standalone remains valid.
## TASK-ISO-009PMOK approved adapter contract

PostgreSQL, MongoDB, OpenSearch and KurrentDB retain their current constructors
and resource/client ownership. Native count is the closed topology mapping.
PostgreSQL n2 is primary+one synchronous standby, n3 is primary+two synchronous
standbys with ANY1; verify exact distinct standby/copy sets, synchronous flush,
observed identities and seeded schema data outside timing. n1 retains local WAL.
MongoDB uses the genuine same replica-set election/majority write contract at
n2/3; exact1 primary plus n-1 healthy distinct data-bearing secondaries, same
set/version, journal/majority policy and direct seeded copies are verified. n1
keeps the current genuine standalone behavior. OpenSearch observes exact n
connected data/manager nodes, one primary shard plus n-1 replicas, active exact
placements and checked request-durable acknowledgement for all n copies.
Kurrent verifies exact same n alive native gossip members and versions,
one leader, n-1 followers, no extra member/copy and existing append/read revision
and body probe; n2 quorum requires both nodes. No paid clustering substitute.

DocumentWrite/Update/Delete on PostgreSQL, MongoDB and OpenSearch enforce native
create uniqueness, existing update exactly1 changed record, existing delete
exactly1 record, and public absence/exact body readback. No upsert or retry.
Postgres returns affected1; Mongo matched1+modified1 / deleted1; OpenSearch update
result=updated (doc_as_upsert=false) and delete result=deleted with all-copy shard
acknowledgements. Kurrent remains stream-only unsupported CRUD.

TASK009PMOK owns only existing Postgres/Mongo/OpenSearch/Kurrent-prefixed files
in the BenchmarkComparisons library slice, PostgresTarget.cs, and NEW
NativeDocumentTopology-prefixed TUnit tests. Root owns contracts/dataset/measurer,
KeyLoad/Redis/Neo4j, host/composition/workflows and genuine native runtime proof.
Tests precede implementation, pure real response/SQL/config/contract checks
without HTTP/native doubles. Ambiguous primary-source behavior escalates.
TASK008M/009 mutation failure refinement: document adapters normalize only
verified native semantic outcomes to shared internal codes DocumentCreateConflict,
DocumentUpdateMissing and DocumentDeleteMissing. Other cardinality/policy/transport
faults remain failures and may not masquerade as expected missing/conflict.
Root adds common codes and an untimed real-session probe before CRUD timing:
fresh absence, create/read, rejected duplicate, rejected missing update/delete,
exact update, exact delete, sentinel unchanged. Expected negative outcomes must
carry these precise native-derived codes. Probe IDs use a separate reserved
range beyond all create/update/delete inputs. Native regression tests cover these
flows on real containers in the isolated worker, not doubles.
## Selected Aspire composition join contract

Root owns the early AppHost selection route and `IsolatedResourceContext`.
The selected target is validated before any cluster creation. It constructs
only the selected native engine group plus one source-bound runner; KeyLoad
alone calls the existing trusted fixed-group ClusterResources. Unsupported
Neo4j2/3 constructs the runner alone for a reason envelope. No foreign database.

The internal context freezes `Builder`, `Selection`, `Runner`, `Root`;
`DataDirectory(name)` returns a fresh owned path under Root/native;
`BindEndpoint(index,node,endpointName)` binds Native:Endpoints:index and runner
WaitFor(node); `BindSetting(name,string|ParameterResource|ReferenceExpression)`
binds Native settings only; `BindImage(reference)` binds immutable Native:Image.
Feature-owned engine helpers expose static `Add(IsolatedResourceContext)` and
return void, using pinned Community native images, independent data directories,
native aliases and source-controlled bootstrap scripts outside timing. Root
serializes shared bindings/dispatcher/project changes. Native helpers may not
allocate foreign engines, alter credentials trust or relax observed proof.
OpenSearch TASK009PMOK replacement refinement: native _update recursively merges
`doc`, which cannot prove exact replacement when prior fields disappear. Use the
documented native script replacing ctx._source from params.source, no upsert or
scripted-upsert, retry_on_conflict=0, result=updated and all-copy ACK. Request
regressions include removed prior/nested fields; partial merge is not acceptable.

## TASK-ISO-009PMOKR native composition join

REQ-BC-052/055, AC-ISO-002/003/006: only selected PostgreSQL, MongoDB,
OpenSearch or KurrentDB native nodes plus an explicitly labelled one-shot native
bootstrap client may be added. Root owns dispatcher and shared image/settings.
The worker owns NEW `IsolatedPostgres*`, `IsolatedMongo*`, `IsolatedOpenSearch*`,
`IsolatedKurrent*` AppHost slice files and NEW `IsolatedDocumentResource*` comparison
tests. Existing adapter constructors and all frozen settings remain unchanged.

PostgreSQL uses the pinned pgvector18 image, one primary, zero/one/two physical
streaming standbys, exact application names `benchmark_standby1/2`, PG18 native
data mount, fsync/on and synchronous_commit/on. The primary's official init path
adds a secret-free physical replication pg_hba rule. The superuser password is a
random secret Aspire parameter. Standby pg_basebackup uses PGPASSWORD, an owned
fresh directory and native -R/streaming WAL configuration. Quorum is established
by the existing untimed native adapter before seeding. No setup DDL waits for
standbys before they can bootstrap. Native bootstrap waits are bounded.

MongoDB uses official 8.3.9 digest
`81a1c8842a09589fc8d5f285266f3340bf4abdf66700ba22988f14cc9b2b3118`, verified
against official Registry OCI index on2026-10-03. One node is genuine authenticated
standalone; two/three use exactly one authenticated replica set. Random shared
root password and replication key are secret parameters/environment, never
command arguments or retained logs. A source-owned wrapper may write/chmod/chown
its private in-container key file before exec of official docker-entrypoint.
A same-image one-shot mongosh client establishes the native replica-set members
and bounded readiness before the runner. It is a bootstrap client, not a fourth
database node. No fake replicas, force reconfig or hidden operation retries.

OpenSearch uses pinned3.6 native discovery seed and cluster-manager lists, one
primary shard and n-1 copies qualified by the adapter. Heap512MiB per native
node, official entrypoint, private data volumes. Benchmark-only security-disabled
HTTP is explicit in report transport/auth metadata; no admin credentials are
claimed. Kurrent uses pinned free26.1.2 native cluster_size n, replication1112,
HTTP2113, all-interface binds and exact native advertised aliases/gossip seeds;
benchmark-only insecure transport is accurately reported. Two-node majorities
require both nodes and do not claim single-failure availability.

Tests precede implementation: actual Aspire CreateBuilder/Build resource models,
ExecutionConfigurationBuilder native callbacks, exact selected resource counts,
private paths, images, aliases, secret/env/argument boundaries, bootstrap wait
edges, single-node exclusions and rejection before allocation. They run in
GitHub only. Model assertions do not qualify a running cluster; root's genuine
isolated270-cell tests and raw native proof remain required. Worker may not edit
shared context, dispatch, projects, central pins, contracts, workflows, docs,
existing tests or other resource prefixes. Escalate any missing native setting
or image-specific UID requirement rather than weakening security/native proof.

PMOKR image-specific permission refinement: official PostgreSQL18 entrypoint
chowns PGDATA but does not chown its mounted parent. A source-owned wrapper may
chown only this cell's `/var/lib/postgresql` mount to image postgres user/group,
keep0700, then exec the official entrypoint with native args. The standby wrapper
owns the same narrowly scoped permission initialization before pg_basebackup.
Do not relax directory modes or impose a host UID on the PostgreSQL process.
Root approves this native startup refinement under AC-ISO-002/003/006; actual
GitHub container permission/copy proof remains mandatory.

PMOKR volume refinement: OpenSearch and Kurrent retain their official image user
and entrypoint. They may use new uniquely named Docker volumes scoped to this
one cell rather than unreadable host0700 binds. Volume names include an owned
random cell token; no volume is reused or shared. Root's runtime fixture records
actual mount names and removes only these owned volumes after stopping this app.
Model tests assert volume type, distinct per-node/per-cell names and official
user/entrypoint; actual native GitHub storage/copy proof remains mandatory.

## TASK-ISO-010I immutable image bundle

REQ-BC-050/056, AC-ISO-001/006/007: one trusted Linux job builds the server and
load generator once. All native cells import those exact image bytes into their
own local registry; no database or live process is shared. Frozen portable v1:
`{schemaVersion:1,sourceRevision,runId,attempt,repository,ref,images:{server:{archive:"server.tar",bytes,sha256},comparisons:{archive:"comparisons.tar",bytes,sha256}}}`.
A bundle contains original image-receipt.json, server/comparisons-manifest.json,
these two native Docker save archives and image-bundle.json; native command/
registry/cleanup evidence may also be retained. JSON must be strict, duplicate
keys rejected, SHA/IDs/source/cohort/constant base pins bound to the existing
receipt; no symlinks or existing output overwrite. Hash archives with bounded
streaming reads (4GiB maximum each); never parse a Docker archive in custom code.

`export-images.mjs` accepts no CLI options, uses the existing Linux GitHub run
context and prepared verified receipt, invokes native Docker image save with
bounded owned output paths, hashes regular files and exclusively writes the v1
bundle. `import-images.mjs --bundle=<absolute-existing-directory>` verifies the
input regular files/hashes/receipt/cohort and current clean source before native
Docker load. It starts only the existing owned local registry, verifies native
loaded config IDs/source labels, pushes the original tags, retrieves native
registry manifests and requires exact byte/digest equivalence to the original
manifests. Then it writes the existing local image receipt and original immutable
outputs. Any mismatch fails before Aspire/native database allocation. Original
build and import receipts are retained separately; supplied JSON never claims to
authenticate GitHub transport.

Root owns actual trusted GitHub job/artifact binding, workflows, image-contracts
and shared source exports. gates_audit owns only NEW `image-bundle-*`,
`export-images.mjs`, `import-images.mjs`, NEW UnitTests `ImageBundle*` and NEW
ComparisonTests `ImageBundle*` files. Root exposed verifySourceCheckout and
makeImageRecord from prepare-images for reuse; preserve all native checks.
TUnit tests precede implementation: real Node/file validation and negative
hash/source/manifest/unsafe-path/duplicate/output-preservation inputs, no HTTP
or Docker doubles. A real Docker export/registry-stop/remove-own-tags/import
round-trip in the trusted build job verifies archive consumption, native config,
source, manifest identity and outputs. Test filtering uses TUnit tree-node paths.
No local tests/build/start. Root review and exact-SHA GitHub proof are required.

TASK-ISO-010I retention filename: import exclusively retains original receipt bytes as `build-image-receipt.json` in its fresh owned local evidence directory; existing `image-receipt.json` remains the native verified import receipt. Existing strict aggregate-json parser may be reused as a generic duplicate-key reader; it does not authenticate transport.

TASK-ISO-010I directory join: export exclusively creates sibling `RUNNER_TEMP/keyload-image-bundle` for original receipt/manifests, tar archives and v1 bundle. `keyload-images` retains registry evidence. The real round-trip retains the original evidence as a distinct owned sibling, cleans only its registry/verified tags, then import recreates `keyload-images` and final native receipts/outputs. No supplied paths may overwrite either directory.

TASK-ISO-010I archive sha256 is raw lowercase64-hex; bytes is a positive safe integer at most4294967296. Existing native manifest/config digest IDs retain sha256: prefix. Export/import require no success stdout; workflow uses the fixed bundle/evidence paths and existing GITHUB_OUTPUT image outputs.

## TASK-ISO-010C native preflight before full cohort

Before allocating270 intense cells, qualify27 isolated Linux native cells, one
supported representative scenario per engine at each native node count. Each
preflight gets its own VM, the same frozen intensive options/image and all native
membership/copy and untimed CRUD negative probes where supported. Representatives:
KeyLoad/PostgreSQL/Redis/Neo4j/MongoDB/OpenSearch PointRead; Qdrant VectorExact;
RabbitMQ QueueCycle; KurrentDB StreamAppend. Neo4j2/3 remain runner-only explicit
unsupported topology. Preflight artifacts are `comparison-preflight-<id>` and
jobs `preflight / <id>`; they are retained but excluded from final aggregation.
The108CRUD/162specialized full matrices depend on successful complete preflight.
Every full cell is still measured on its own new VM and must pass; preflight never
substitutes for the270-cell cohort, RF3/public clients or remaining quality gates.
The closed270-cell plan schema remains unchanged. A derived preflight matrix has
exactly these27 existing canonical cells and is validated against that plan.

## TASK-ISO-010K public client qualification

AC-ISO-003/004/005: NEW ComparisonTests `IsolatedKeyLoadPublicRegression*` files
are owned by sql_audit. Entry `VerifyAsync(DistributedApplication app,int nodeCount,
CancellationToken token)` runs untimed after KeyLoad PointRead measurements for
actual RF1/2/3. Read only native model endpoints and persisted admin authority;
own native HTTP + official C# MCP SDK clients. Use a unique persisted partition.
Verify SDK create/MCP read/revision-fenced MCP update/SDK exact read/SQL CALL delete
and SDK/MCP absence, duplicate create/missing update+delete, persisted read-only
credential denial across SDK/MCP/SQL with exact canonical errors, stable CommandId
retry/receipt, atomic stream/queue plus SQL logical parity/nonconsuming inspect/
fenced ACK, genuine two-part blob hash and crossing range via SDK+official MCP,
and invalid hash rejected without publication. No transport/fixture copying or
mocks. Root adds explicit SDK project and centrally pinned official MCP package
references; worker may not edit contracts/projects/workflows/old fixtures/docs.

Root owns subsequent real native fault phase: RF1 native SIGKILL/restart restores
prior ACK, RF2 loses quorum with either voter stopped and rejects new quorum work,
restart preserves exact membership/authority and prior ACK; normal RF3 retained
snapshot/catch-up public-client gate remains mandatory. No snapshot-threshold
change or claimed process proof from activation restarts. The public helper alone
does not qualify process recovery, power loss or snapshot installation. Worker
writes meaningful actual caller assertions first; source inspection and exact-SHA
GitHub cases/fault receipts are required before the task joins as qualified.

## TASK-ISO-010G authenticated GitHub transport

AC-ISO-001/006/007: gates_audit owns only NEW `isolated-github-*` scripts and NEW
UnitTests/ComparisonTests `IsolatedGitHub*`. Root owns workflows and fixed shared
schemas. Use actual authenticated GitHub CLI REST (GH_TOKEN only in environment,
never argv/logs), fixed managedcode/KeyLoad canonical ci.yml/run/current attempt,
actual clean source/GITHUB_SHA and Linux. Retain exact raw run-attempt/jobs-pages/
artifacts-pages JSON. Complete bounded pagination, unique IDs and exact native
source/run/attempt/job/artifact associations are mandatory. Supplied metadata
never claims to authenticate transport. Reuse genuine existing hash/strict JSON/
process primitives where suitable; do not invent a ZIP parser or HTTP doubles.

`isolated-github-job.mjs` accepts no args, reads exact trusted
KEYLOAD_COMPARISON_JOB_NAME (`case / <canonical-id>`, `preflight / <canonical-id>`
or comparison-images for a native API test), captures current in-progress own
job, and appends only actual KEYLOAD_COMPARISON_JOB_ID to existing GITHUB_ENV.
Closed source/run/repo/ref/workflow context is checked against actual provider
response. Metadata capture lives under fresh RUNNER_TEMP/keyload-cell-github.
The derived canonical cell is checked against a strict plan when applicable.

`isolated-github-collect.mjs --plan=<absolute> --input=<NEW-absolute>` collects
all270 successful exact `case / <id>` jobs and all270 unique immutable unexpired
`comparison-worker-<id>` artifacts for this same current source/run/attempt,
plus successful `comparison-images` job and exact `comparison-image-bundle`.
Worker jobs must each contain exactly one successful Run isolated native case and
Retain isolated worker evidence step. Image job must contain successful Qualify
native image export and import and Retain common image bundle steps. Keep full
provider metadata; frozen schema1 proof projects only the two required worker
steps. Validate artifact workflow_run source/run and timestamps within own job.
Download native artifact ZIP bytes via bounded authenticated gh api; verify exact
API size and SHA256 digest before extraction. For workers use native `unzip -p`
for exactly worker.json into `input/workers/<id>/worker.json` (no archive paths
extracted); stream raw bytes with64MiB bound/hash and reject absent/duplicate raw
entry through native filename inventory. Retain verified ZIP archives separately
under input/archives and full API capture under input/github. Root aggregate
validator consumes strict input/workers and input/github/proof.json.

The image ZIP is separately bounded by both4GiB archives plus metadata, streamed
and retained with actual provider digest. Native unzip reads only its fixed JSON
receipt/bundle/manifests to establish source/config/manifest/cohort and common
report image identity; write input/github/image-proof.json with authenticated
actual image job/artifact/ZIP and raw-file hashes. Raw workers must name exactly
the verified image-receipt generator and KeyLoad server image where applicable.
Do not extend frozen aggregate proof/envelope shape. Fail closed before returning
success or before aggregate invocation if any cell/image evidence is missing,
foreign, partial, duplicate, expired, failed, changed or corrupt. Existing input
must remain unchanged; newly created partial capture is retained as failed
read-only evidence and cannot publish. Do not redownload/fallback to another run.

Tests first: actual Node/files strict projection and corrupt/foreign/duplicate/
missing/hash/step/source/cohort failures with controlled metadata clearly labelled
as parser inputs, no fake network. One real read-only GitHub current-job capture
TUnit case in trusted image job exercises authenticated provider CLI and exact
job identity; complete actual270 collection is final native positive proof. No
local tests/build/network qualification, commits, shared edits or secrets output.
Escalate ambiguous provider fields/ZIP entries rather than weaken proof. Root
retains least-privilege actions:read and trusted same-run artifact-ID download.

TASK-ISO-010G owning join refinements (before implementation): keep aggregate
inventory strict. Collector's --input is the NEW capture root; workers live at
`input/data/workers/<id>/worker.json`, full API proof at input/github/proof.json,
ZIPs at input/archives. Aggregate is invoked with --input=input/data and proof
outside it; no extra directory is admitted into aggregate source inventory.

Image-proof exact schema1:
`{schemaVersion:1,cohort,job,artifact,archive:{path:"archives/comparison-image-bundle.zip",bytes,sha256},files:[{path,bytes,sha256}],images:{server,loadGenerator}}`.
Cohort and job/artifact projections use the frozen worker-proof field shapes;
image job name comparison-images, exact two successful image steps. files are
exactly the four strict fixed JSONs under github/images: image-bundle.json,
image-receipt.json, server-manifest.json, comparisons-manifest.json. Paths are
relative to capture root; hashes lowercase64hex. images are actual native
verified receipt references. Capture all raw metadata and actual provider URLs.

Bounds:100 items/page, max20 pages and2000 unique jobs/artifacts each; metadata
16MiB per capture, worker ZIP128MiB, worker raw64MiB, image ZIP9GiB (two archives
individually at most4GiB plus fixed metadata/ZIP overhead), all worker ZIPs total
16GiB; gh metadata120s, downloads/native image ZIP600s, worker unzip120s. Stream
binary outputs/hashes, never unbounded buffering or native secret output.

An authenticated exact `/runs/{run}/attempts/{attempt}/jobs` route plus verified
native run-attempt response binds attempt when native jobs omit run_attempt;
if returned it must equal current. Artifact workflow_run has no attempt field:
exact own source/run/repository IDs, unique name/ID, immutable digest and created
within the exact current successful job interval bind it; another attempt's
artifact cannot qualify. Provider job html_url may be native actions/runs/run/job/id
or documented legacy runs/run/jobs/id; verify actual own IDs/repository and retain
raw URL, project the same actual IDs to frozen canonical actions URL. No alternate
run/attempt lookup or fallback. A changed pagination snapshot fails closed; setup
may make explicitly recorded bounded same-route fresh captures before allocation,
at most30s, never change source/cohort or retry measured database operations.

TASK-ISO-010 cleanup/privacy refinement: native constructor/configuration failures
must produce fixed safe host stderr, never a driver's connection text. Real
malformed Mongo/Kurrent parser TUnit cases cover the boundary. Session teardown
attempts every owned native session under the ordinary timeout outside timing;
cleanup failure marks a completed case failed while preserving its original
measurement/raw samples. It cannot erase earlier attempts or authorize metrics.
Caller/app teardown independently attempts raw retention, log closure, bounded
native Stop and owned cleanup; a stop failure preserves live mounted data and
fails the job. Native success paths are qualified by every genuine cell. Rare
unforced native-driver close failure has source control-flow review as explicit
manual exception until an actual reproducible native driver fault exists; do not
invent an IComparisonSession service double to produce that branch.

TASK-ISO-013V closes the same cleanup contract over explicit capture/application
disposal, each bounded30s outside measurement and attempted independently. No
implicit await-using disposer may subsequently override a primary assertion or
wait without that bound. Observe eventual failed disposal tasks after a timeout;
retain fixed disposal-stage names in the existing teardown receipt. Every real
native cell exercises successful disposal; unforced simultaneous native disposal
failure keeps the same control-flow review exception above.
The compiler-owned capture scope retains its mandatory ownership check: after
the explicit teardown attempt, its stopped guard makes final disposal an
immediate idempotent token-source release, with no native wait. Application
disposal has no implicit second invocation. Preserve CA2000 without suppression.

TASK-ISO-012P resource refinement: corruption-only fixture scopes may use native
filesystem hardlinks to the immutable genuine ZIPs/277 prepared files. Before
mutating any one file, replace its private link with an exclusive real copy;
never mutate original linked bytes. Keep private original receipt authority and
whole-source unchanged checks. The genuine native capture test still downloads
the actual two ZIPs, checks authenticated digest/metadata, and cleans its owned
scope; it does not duplicate277-file extraction. Serialize that positive test
and require the actual two ZIP sizes plus1GiB available disk before downloads.
Metadata/parser cases copy only metadata; no synthetic positive or fake provider.

## TASK-ISO-011 approved compact publication contract, 2026-10-03

AC-ISO-008/009: the complete schema4 aggregate and its270 byte-preserved workers
remain GitHub Actions evidence. Pages publishes the exact original aggregate
manifest plus an explicitly derived schema1 projection, each hash-bound in a
separate isolated catalog. Raw samples are never copied to Pages or preloaded by
the browser. Each row links its actual successful job and the exact authenticated
artifact ID/digest/name; the UI labels raw evidence as a GitHub artifact requiring
GitHub download/access and subject to provider retention. It must not pretend
that an artifact page is a direct raw JSON download. Expired/missing artifacts
cannot qualify a new publication. This satisfies the native16GiB raw ceiling and
the official published Pages1GB ceiling without losing raw evidence.

Projection schema1 exact top-level fields: schemaVersion, cohort, profile,
options, datasetSha256, workers. It retains each original aggregate worker
verbatim plus one report field. report is the fully validated original schema3
report except each case omits samples; no other reported value is changed.
UnsupportedTopology has report:null. Projection output is at most4MiB. Producer
revalidates all270 raw envelopes/hashes/native proof sequentially using the
existing aggregate validators before stripping samples; never pool nodes or
engines. Browser validates closed270 IDs, cohort, options, target/node/scenario,
all5cases, native nodes/copies/ack metadata, exact measured/unsupported contracts,
finite consistent metrics and original job/artifact identity. Negative parser
inputs are explicitly validation inputs, never synthetic performance evidence.

Separate catalog schema1 fields: schemaVersion, generatedAt, siteSourceRevision,
measuredSourceRevision, evidenceUrl, cohort, aggregate, projection, rawLocation.
Website SHA is trusted main website source; measured SHA/cohort remain exact.
evidenceUrl is the exact managedcode/KeyLoad run URL. aggregate/projection each
have path and sha256 only, with paths isolated/aggregate.json and
isolated/projection.json; rawLocation is githubActionsArtifacts. Every fetched
projection is byte-hash verified before JSON validation. Catalog at most64KiB.

Approved exports: isolated-projection.mjs produceIsolatedProjection({input})
returns the validated derived projection from the aggregate directory;
isolated-loader.mjs validateIsolatedCatalog(value),
validateIsolatedProjection(value, catalog), loadIsolatedCatalog({catalogUrl,signal}),
loadIsolatedProjection({entry,baseUrl,signal}); isolated-measurements.mjs
selectedIsolatedRows(projection,scenario,nodeCount,repetition,metric,target)
returns selected engine rows, where target is exact engine or all, repetitions
are all or exact0..4, and metric keys preserve existing metrics. Every median
uses only that worker's five cases. isolated-lab.mjs
mountIsolatedLab({root,catalogUrl}) returns synchronously {dispose}; async native
fetch has owned errors, abort/generation control, atomic dependent-surface clearing.

Task graph: TASK-ISO-011W models_audit high-capability integration worker owns
ONLY NEW site Features/BenchmarkComparisons/isolated-*.mjs and NEW SiteIsolated*
TUnit test files; may split private helpers under the same prefixes. Root owns
HTML/bootstrap/build assets/shared inventories/Pages/source catalog joins. Start
condition: this contract, existing ADR056, acceptance and plan are approved under
the owner's requested work. Tests precede implementation. Preserve legacy schema2
contracts and all real-browser/TUnit gates; add no third-party packages, fake
fetch/server/measurement, framework, local tests or qualification. New sources
enter exact coverage inventory,80/70 aggregate and90 critical validation/UI gates.
Worker delivers exact source list, integration patch proposal, test mapping and
static evidence; root reviews and qualifies actual complete cohort in GitHub.

TASK-ISO-010G provider-budget refinement: current-job capture relies on genuine
fixed GitHub runner source/run/attempt/repository/main/workflow environment and
queries only its exact-attempt native jobs route, retaining visited pages and
stopping after the unique expected in-progress job. Final collector verifies
workflow and run-attempt provider source afresh and complete jobs/artifacts.
Preflight, CRUD and specialized phases are ordered for the repository-wide
1000/hour REST budget; every cell still has its own VM and independent native
resources. No alternate token, unauthenticated fallback, or measured retry.

Primary constraints: https://docs.github.com/en/pages/getting-started-with-github-pages/github-pages-limits
and https://docs.github.com/en/rest/using-the-rest-api/rate-limits-for-the-rest-api.

TASK-ISO-010G native rate-limit handling refinement: authenticated metadata and
artifact requests retain provider headers as well as exact bodies. Only an
actual403/429 with native x-ratelimit-remaining:0 and valid x-ratelimit-reset, or
a valid native Retry-After, may wait then repeat the identical route. This is
provider setup/collection outside every measured database interval; never retry
database operations or choose another source/token/run. Record every rejected
provider attempt and exact wait. Bound accumulated rate waits per CLI to3700s,
at most3 repeats per identical request and existing request/download bounds.
Other authorization/errors fail immediately. Respect reset/Retry-After and
prevent concurrent retry flooding; workflow matrix max-parallel6. Actual rate
headers are evidence, not an inferred fixed remaining budget. Cancellation kills
owned native process/wait; exhausted bounds retain failure and cannot publish.
Static/parser tests cover invalid headers, absent429 retry permission and bound
calculation; actual current-job and complete270 collector remain native proof.

Current-job context additionally requires the native GITHUB_WORKFLOW_REF value managedcode/KeyLoad/.github/workflows/ci.yml@refs/heads/main. Planner retains its exact schema1 wire while appending derived preflight_matrix to GitHub output; native TUnit validates all27 original cells/options and rejects drift.

## TASK-ISO-010F approved genuine fixed-membership fault qualification

AC-ISO-003/004/005: after KeyLoad PointRead timing and010K public regression,
call IsolatedKeyLoadFaultRegression.VerifyAsync(app,nodeCount,evidenceDirectory,
token). sql_audit owns ONLY NEW ComparisonTests IsolatedKeyLoadFaultRegression*
files. Reuse010K public SDK/MCP/identity assertions read-only; no old integration
fixture/transport copy or mock. Tests are caller-visible assertions in the real
isolated native test. Root owns call and evidence join; no shared edits/commits.

Create unique persisted collection/queue, ACK a sentinel and queue delivery,
retain full command receipts and scoped read-only credential in memory. Capture
every native Dashboard ordered Voters/LocalVoter/NodeId/incarnation/process ID.
Select actual leader/follower through native Dashboard, never assume node1 leader.
Close owned official MCP before faults and reconnect genuine client after recovery.
Resolve closed node1..n ContainerNameAnnotation to bounded native Docker inspect
full ID/configured image/image ID/start time/mount identity; inspect only selected
safe fields, never environment/whole inspect/secrets. SIGKILL the exact inspected
full ID and prove exited. Restart only a verified killed resource using native
Aspire StartCommand and WaitOnResourceUnavailable health wait, then native running
and new process start time with unchanged image and retained bind-mount authority.

RF1: sole native process exited before failed SDK attempts; read OwnershipLost
and write UnknownWriteOutcome cannot ACK. After restore, prior ACK data persists
and command not sent to any live process is absent before explicit same-ID retry.
RF2: separate stop/restore phases for EACH actual voter prove either stop loses
majority. Survivor public quorum reads/commands fail only the existing closed
OwnershipLost/UnknownWriteOutcome contracts; official MCP may fail genuine503
at its persisted auth/read barrier. Never demand canonical apply statistics from
quorum-blocked public status. While minority exists no successful public cut/ACK
is allowed. An unknown write may remain durable/uncommitted and later commit:
restore resolves that SAME retained command ID to one exact receipt/revision,
never asserts permanent absence or retries with a fresh ID. RF3: follower kill
preserves actual live majority ACK/read; restart/catch-up proves ordered membership
and Applied>=ACK plus exact data. No snapshot-threshold/storage modification.
Existing RF3 retained snapshot/cursor/outbox gate remains separate required proof;
process restore and append catch-up are not snapshot-install or power-loss proof.

After every restore, exact completed ACK same-ID/token returns stored receipt;
fresh-ID reuse of the old ACK token yields StaleLease, never TokenInvalidated.
Same persisted scoped credential succeeds through SDK/reconnected official MCP
and still rejects writes. Compare actual ordered voters/local authority and
incarnation with baseline. SDK unknown outcomes follow existing public contract.

Bounds: overall10min, native CLI30s with bounded readers/kill+exit/drain, native
exited20s, restart120s, public attempt30s, readiness reads90s/250ms. Recovery polling
and same-command resolution are untimed fault qualification, not benchmark retries.
Finally attempt every killed resource restoration with independent bounded cleanup
even if an earlier restoration fails; preserve the primary safe failure. Atomic
owned no-overwrite JSON retains actual cell/SHA/run/attempt/job, safe native IDs/
image/start/mount hashes, ordered membership and fixed failure categories, not
credentials/tokens/raw exception strings. Root reviews every prefix diff and
qualifies all1/2/3 actual native jobs at final SHA before completion.

TASK-ISO-011W clarified joins: projection loader entry is the whole validated
catalog. Rows preserve legacy row fields plus nodeCount and verbatim worker
metadata/report. isolated-contracts.mjs browser constants must match canonical
isolated-contract.json structurally in every producer invocation. Defaults are
PointRead/node3/target all/repetition all/throughput. DOM IDs: isolated-lab,
isolated-scenario, isolated-node-count, isolated-target, isolated-metric,
isolated-repetition, isolated-results, isolated-error, isolated-retry,
isolated-announcement; native labelled selects, role alert/error and polite
status announcement. Root joins HTML/bootstrap and --isolated aggregate option.
Genuine input env KEYLOAD_SITE_ISOLATED_AGGREGATE; native producer deadline300s,
max4MiB output, V8 coverage env retained. Reuse one immutable validated projection
within suite; real270 raw positive is mandatory, never skip/fabricate fallback.
New site modules join exact80/70 aggregate/90 critical source inventory. Imported
authoritative aggregate/planner modules retain their own TUnit tooling/native
cohort gates; site coverage is scoped to site-authored modules and existing
evidence tools without claiming unmeasured tooling coverage.

## TASK-ISO-012P approved isolated Pages evidence join

REQ-BC-056/057/058, AC-ISO-007/008/009. Root approves this additive contract
before implementation. Retain the independent genuine legacy comparison job,
12-file archive, three historical profiles and their complete tests. Their
actual earlier SHA is independent of the new isolated cohort; unavailable or
expired historical evidence fails rather than fabricating profiles.

Pages keeps its actual trusted native executor environment. A separate explicit
Pages context captures authenticated main-push CI metadata; never forge the
CI-only current-job environment. Selection is highest run number, then descending
attempt, with an actual completed/successful comparison-aggregate job. Missing or
failed aggregate permits bounded earlier search. Once success is selected,
missing/expired/ambiguous/invalid evidence fails without older fallback. Native
repository IDs, workflow path/ID, source, exact attempt route, jobs, artifacts,
creation intervals and closed successful steps are mandatory. Search is bounded
to2000 run records,2000 exact-attempt jobs/artifacts and2000 run-attempt pairs.
Retain every requested history page/pair. No alternate token/source/retry route.

Exact successful aggregate steps are Recreate the canonical complete intensive
plan; Collect exact authenticated native jobs and immutable raw artifacts;
Validate and retain all270 byte-preserved workers; Generate bounded native
performance metrics; Retain the generated native metrics candidate; Retain the
complete qualified comparison cohort; Retain authenticated provider and archive
integrity evidence. Provider artifact existence from always() alone proves nothing.

Download only authenticated comparison-isolated-suite and
comparison-isolated-provider-evidence ZIPs. Freshly verify all270 canonical worker
job/artifact identities and common image job/artifact against retained proof.json
and image-proof.json. Reuse strict image contracts to verify the four original
image JSONs, their hashes, source/cohort/config/manifests/references and every
worker image. Do not redownload270 worker ZIPs or the common9GiB image ZIP:
successful exact-step collector and immutable aggregate artifacts bind that chain.

Closed receipt schema1 fields: schemaVersion,state,mode,publishEligible,source
{website,measured,control},repository{id,fullName},workflow{id,path},run
{id,number,attempt,url},cohort,aggregateJob{id,name,url,startedAt,completedAt,steps},
artifacts{suite,provider},workers[{id,job,artifact}] exactly270,image{job,artifact},
metadataFiles[{path,bytes,sha256}],archives,inputFiles. Native artifacts retain
id,name,sizeInBytes,digest,expired,createdAt. Archives retain confined path,bytes,
sha256. metadata_verified has null archives/inputFiles; archive_verified has both
ZIP receipts and exactly277 extracted-file receipts. Website/measured/control
revisions remain independent. Fail on extra/duplicate fields or changed source.

Capture layout: metadata/; archives/comparison-isolated-suite.zip and
comparison-isolated-provider-evidence.zip; input/aggregate/aggregate.json plus
workers/<canonical ID>/worker.json; input/provider/proof.json,image-proof.json,
images/<four fixed original JSON files>; metadata-proof.json; archive-receipt.json.
Root supplies KEYLOAD_SITE_ISOLATED_CAPTURE and
KEYLOAD_SITE_ISOLATED_ARCHIVE_RECEIPT; the mandatory before-session BCL preparation
sets KEYLOAD_SITE_ISOLATED_AGGREGATE to its verified input/aggregate directory.

BCL ZipArchive inspects the complete native archive before creating output.
Suite file set is EXACT aggregate.json plus270 canonical worker paths, with only
their parent directory entries allowed. Provider must include the exact six
selected original proof/image files; other retained native capture entries are
bounded metadata only under the collector's closed capture naming convention.
Reject traversal, case-insensitive collisions, links/special files, unexpected
directories/types, duplicated entries, declared/streamed length drift and excess
bounds. Suite ZIP18GiB, provider ZIP128MiB, each raw64MiB, JSON4MiB, total raw16GiB;
metadata captures16MiB/page with finite inventories. Check required available disk
from actual declared extraction lengths plus1GiB reserve before extracting.
Exclusive fixed-path extraction retains all277 byte/hash receipts. No generic
unzip-to-directory or pre-extracted fallback. Verify archives/files unchanged
after the full suite and immediately before/after the final builder.

Public new CLI:
capture --input=<NEW absolute root> --mode=validate|publish
 --site-revision=<website SHA> --workflow-revision=<control SHA>
 [--requested-run=<validation-only run ID>]
verify-inputs --input=<capture root> --receipt=<archive receipt>
fresh --before=<qualified archive receipt> --after=<fresh metadata receipt>.
Fresh capture reselects latest eligibility and compares source/cohort, aggregate,
all270 workers, common image and both aggregate artifacts. Deletion/expiry,
changed identity or newer successful evidence blocks deployment. No ZIP download
for freshness. Native metadata120s/download600s/producer300s remain bounded.
Rate wait is the approved identical-route actual403/429 protocol,3700s accumulated,
3 repeats. Pages qualification timeout180min and deploy freshness90min accommodate
this wait without changing operation clocks.

gates_audit owns ONLY NEW scripts site-isolated-github-*.mjs (contract,context,
runs,proof,capture,fresh,CLI, plus narrowly split private helpers if400/200/50
requires) and NEW SiteIsolatedGitHub*.cs (archive setup/reader/receipt/file operations,
selection/proof/archive/freshness/native capture tests and bounded Node helper).
Root alone owns shared hooks/workflows/coverage inventories/source joins/docs.
Disjoint from models_audit SiteIsolated* UI/producer prefixes: NEW delegated names
must begin SiteIsolatedGitHub. No shared transport mutation, commits, local builds,
tests, providers, suppressions, doubles or skipped inputs. Escalate contract drift.

Tests-first methodology: genuine complete native metadata+two ZIPs and same
277 raw files are the positive input; independent C# assertions verify exact
identities/hash/source/fields, actual BCL extraction and native Node capture.
Controlled corrupt copies cover missing/extra/duplicate/path/type/hash/length/
cohort/image/job/artifact/source/expiry/newer-success/freshness failures, never
positive measurements or provider doubles. Root joins every new production module
to actual site80/70 aggregate and individual critical90 coverage and hashes the
executed dependency closure between control and website checkout. Existing tests
and denominators remain. Final builder takes both real archives, publication
receipt gains a separate isolated field, both native freshness checks precede the
same needs-gated Pages artifact deployment. Actual successful workflow/provider/
live metrics proof is required before AC completion.

TASK012P freshness entry refinement: add closed capture-metadata CLI verb with
the same capture arguments and actual provider environment. Export
captureSiteIsolatedMetadata({environment,args}); it performs the identical bounded
selection/native proof capture, emits metadata_verified and downloads no ZIPs.
capture uses that selection before downloading the two immutable ZIPs. Deploy
calls capture-metadata in publish mode, then fresh with the two receipt paths.

TASK012P provider archive refinement: bound the total declared AND streamed
decompressed provider metadata to128MiB,4096 entries, and16MiB per metadata entry
before any extraction. Selected original JSONs retain4MiB limits. Safe names and
actual lengths are checked for the entire archive, including metadata not emitted
to the fixed six-file output. Reject excess totals rather than ignoring them.

TASK-ISO-011R approved responsive refinement under AC-ISO-008/009: tests first
use genuine standalone native Chrome/270 projection at1440/768/390/320 widths.
Keep document scrollWidth<=clientWidth, all9 independent-oracle rows/statuses and
opened long provenance details. Table overflow belongs only to a labelled,
keyboard-focusable role=region wrapper (table-scroll isolated-table-scroll,
tabIndex0). Chart class isolated-chart and details isolated-worker use existing
brand tokens. models_audit owns only isolated-view.mjs, a NEW SiteIsolated layout
assertion helper and standalone test call; root owns scoped shared CSS. No value,
validation/provenance/source/schema/public contract changes or test omissions.
Root reviews complete prefix diff; actual GitHub Chrome/coverage remains required.

TASK012P original archive authority: BCL unchanged verification compares its
private in-memory original receipts and files, never a rewritten disk baseline.
CLI verify-inputs also revalidates retained authenticated metadata/proof identity
and hashes both original ZIPs against the selected native artifact digests.
Three bounded native unzip -p reads select ONLY original aggregate.json,
proof.json and image-proof.json into exclusively owned temporary files,4MiB each.
Their SHA256 must equal extraction receipts; original proof binds all270 actual
worker hashes and original image-proof binds all four actual image JSON hashes.
Reject rewritten receipts/raw/proofs rather than trusting their mutually changed
values. No new download, custom ZIP parser, general extraction or270 repeats.
Fresh predeploy authenticated metadata remains the final remote identity anchor.

## Native b474 canonical public read expectations

AC-ISO-004/005 public document checks preserve deliberately unsorted mutation
input and require independently specified ordinal canonical stored JSON. Compare
the actual SDK/MCP returned body exactly, without normalizing the actual result.
Initial/replaced bodies retain exact properties, values, revisions, identity and
redaction. Command replay, failed mutations, delete absence and sentinel checks
remain mandatory. The existing native failure37087909605/job111105163526 proves
the old oracle mismatch; repaired full1/2/3 public regressions prove the correction.


## TASK-ISO-020RM accepted diagnostics-first implementation contract

AC-ISO-003/005/006 retain every existing native identity, role, acknowledgement,
readiness, deadline and failure assertion. At b474/run37087909605, Redis2/3
failed RedisReplicaPrimaryIdentityMismatch; Mongo2/3 failed bootstrap after
genuine election. Their exact failed predicates were discarded. This stage
adds bounded evidence only; it cannot claim those failures repaired.

Ordered stages: acceptance-derived diagnostic tests; strict closed projections
at the original failure boundary; root source review/build; exact-SHA Linux
native Redis/Mongo1/2/3 preflights; inspect original failed predicate evidence
before authorizing any behavior repair; full270 remains a dependent gate.

Disjoint worker build_action_review owns Comparisons RedisReplicaProof.cs and
NEW RedisReplicaDiagnostics.cs; AppHost IsolatedMongoInitiate.js; ComparisonTests
NEW RedisReplicaDiagnosticTests.cs and MongoBootstrapDiagnosticTests.cs, plus
existing IsolatedDocumentResourceMongoTests.cs where necessary. Root owns docs,
Git, artifacts and integration. These are BenchmarkComparisons slice paths.
No connection-string/password/URI/environment/exception-message/stack/raw-body
logging. Redis projects only validated bounded configured/native host/port,
role/link/state/cardinality and a closed failed-predicate identifier. Mongo
retains only last bounded configured-member projection: stage/predicate, typed
nullable counts, validated expected member names/IDs/states/health/set, native
numeric code and bounded codeName. Print once on terminal failure alongside
existing classification; exceptions remain failed probes, never readiness.

No hostname normalization, polling/retry addition, budget extension, native
image/config/durability/schema/worker-provenance change, or assertion relaxation.
Keep original120s bootstrap promise barrier, zero-sample failures and owned
cleanup. Pure projection/privacy edge tests complement real native failures;
no mocked service/native success. All tests execute in GitHub only. Diagnostic
records use existing retained native logs; no new durable/wire format. Rollback
removes these diagnostics together without changing native state. ADR remains
Accepted; fresh original artifacts and all required gates are the join.


## TASK-ISO-021 accepted application/control read separation

REQ/AC-REP-006 and AC-ISO-003/004/005 remain the correctness/qualification gates.
Source b474 proves application quorum rounds spend Critical capacity via empty
Append. Actual native RF2 OwnershipLost/RF3 ResourceExhausted do not yet prove
which replay pool first overflowed; preserve that uncertainty and failed samples.

Append signed method ordinals ReadProbe=7 and ControlReadBarrier=8; old0..6,
envelope version, exact HMAC method binding, generation, nonce, freshness,
full-lifetime cross-method replay and durable formats stay unchanged. ReadProbe
uses the existing strict AppendRequest parser with initialized empty Entries
and declared LeaderId equal to authenticated sender. Any data/control entry,
default/null entries, malformed/duplicate/unknown/trailing field, invalid header
or over-budget body fails before receiver/ObserveLeader/state mutation. Valid
ReadProbe consumes ReadBarrier. Ordinary heartbeat/control empty Append stays
Critical. Application-origin nonempty catchup fallback uses empty ReadProbe;
control/write fallback keeps ordinary Append. Snapshot/data semantics unchanged.

ReplicaLeader uses an internal closed purpose, never a public caller flag.
ReplicaConsensus adds trusted ReadControlBarrierAsync sharing the actual bounded
read core/readiness/activity/lifetime/10s/quorum/term/cut/apply semantics. Remote
control uses signed strict-empty-string ControlReadBarrier/Critical; local
control round uses ordinary empty Append. Existing public ReadBarrier retains
application purpose. No ICommitCoordinator/HTTP/SQL/SDK/MCP/authorization change.

ReplicaMembershipTable constructor now requires the actual ReplicaConsensus
instead of IReplicaEndpoint; the sole production construction already supplies
partition.Consensus. Change the source signature coherently, with no legacy
overload/cast/compatibility fallback. ReplicaMembershipStore receives that same
node-owned consensus and calls trusted control read; membership writes retain
coordinator/atomic CAS. Preserve true caller cancellation; independent native
deadline/lifetime cancellation maps OwnershipLost as the original coordinator.

Disjoint ownership: root enum, strict Orleans replay/sender classification,
membership source join, fixed quota diagnostics, benchmark-only profile/env,
shared docs and integration. gates_audit owns Replication ReplicaConsensus.cs,
ReplicaLeader.cs, ReplicaFollowerSender.cs, ReplicaRequestDispatcher.cs and NEW
cohesive prefixed purpose/guard helpers plus NEW focused real-node tests under
RecoveryTests/Features/ClusterReplication. Escalate if a test needs a shared
fixture edit; no synthetic successful transport, gate/quorum/term/retry relaxation
or public/shared abstraction edits. Tests first use actual stored nodes and
real protocol. Root new security TUnit tests cover valid signed methods, empty
guards, wrong sender/MAC, malformed/nonempty/null fields, cross-method replay,
separate capacity denial and unchanged old method/data/snapshot classification.

Quota diagnostic snapshot is captured atomically at capacity denial and emitted
after the lock: configured numeric sender index, closed method/pool, counts and
limits, Unix timestamp/oldest expiry. No identity/address/nonce/payload/credential/
exception text. Fixed per-sender/pool state emits first then at most once30s,
with saturating suppressed count; no success-path allocation/unbounded keys/IO.
Original authenticated ResourceExhausted classification remains unchanged.

Benchmark-only ReadBarrierPerVoter196608 with original Critical16384/Forward32768/
DataAppend32768 yields278528 per voter,835584 per RF3 node, below1048576.
Production defaults stay unchanged. Retain actual rolling occupancy/expiry and
exact node config; this arithmetic does not guarantee complete CRUD/queue cells
or throughput. Every public successful call still performs two authorized
quorum barriers; do not remove persisted authorization or cache it here.

Ordered stages: failing acceptance-derived tests; bounded source implementation;
root review/build/format/governance; exact-SHA full normal/scalar/138 recovery/
RF3 and native KeyLoad1/2/3; then genuine control-pressure/failover proof and
all270 before aggregation/site. Native control execution/membership-under-load
observer remains a separately frozen required join, not inferred from admission
or absence of warnings. Public auth/lease reserve liveness remains unqualified.
All test/runtime execution is GitHub only. Homogeneous all-voter rollout and
rollback preserves local stores/journals; old nodes fail closed on new methods,
no mixed-version availability promise. ADR remains Accepted pending all evidence.


## TASK-ISO-022K accepted pinned native gossip build identity repair

AC-ISO-003/005/006 retain strict native membership/endpoint/role/acknowledgement.
The three original b474 Kurrent jobs fail membership with zero samples. Native
logs report26.1.2.3778 at server commit1eb5f66721e2a3848933485df8bd586016c16de1;
exact official MemberInfo.ToString prints ESVersion, ClientClusterInfo assigns
that same ESVersion and JsonCodec emits esVersion. Existing health comparison
against image tag26.1.2 therefore rejects the pinned native build identity. This
is a source-proved rejection; absent retained HTTP body means other readiness
conditions and a sole-cause claim remain unproved.

Disjoint build_action_review owns KurrentConstants.cs, KurrentClusterMembers.cs
and bounded existing/new Kurrent native-identity tests in the BenchmarkComparisons
slices (inventory exact test paths before writing). Add separate named exact
ExpectedGossipVersion26.1.2.3778 and use it only for native member health equality.
ExpectedServerVersion26.1.2 remains the exact image-tag/TargetProfile.Version
contract; raw ClusterEvidence retains four-part observed native versions. No
SemVer-prefix/range/suffix normalization, image/digest change, endpoint/role/
member-count/copy/checkpoint weakening, extra retry or timeout extension.

Tests first independently prove exact pinned gossip acceptance and wrong image/
build/neighboring version rejection, preserving all topology and unhealthy-member
negative cases. Pure member DTO values are algorithm inputs, not native HTTP
proof. Root full diff/build/format/governance joins exact-SHA native1/2/3 genuine
HTTP/member/copy flows and full270 before any site metrics. All tests GitHub only.
Additive constant and strict comparison have no schema/state/wire migration;
rollback restores the old source as a coherent unit and retains failed evidence.
ADR remains Accepted until every required gate is genuinely satisfied.


TASK-ISO-021R source review closes the new-method index overflow boundary:
ReadProbe rejects PreviousIndex=Int64.MaxValue before replay admission, even
with otherwise valid positive term/previousTerm and empty entries. Genuine
signed MaximumPreviousIndexFailsBeforeReplayAdmission asserts Validation and
both one-slot pools remain available. Existing Append semantics are unchanged.
Source review also confirms fixed rate state/atomic snapshots and actual native
DI/Consensus ownership; native control-under-load and provider-failure evidence
remain open, with no inferred successful runtime result.


TASK-ISO-020RM root process-ownership join: after the worker's terminal source
packet, root serially refines MongoBootstrapDiagnosticTests only. Cleanup
independently collects child cancellation/reaping and both original stdout/
stderr tasks under finite bounds, observes unsettled originals, and preserves
the initiating failure first alongside cleanup failures. This is real owned
Node process lifecycle coverage for pure projection inputs; native Mongo
readiness/cause remains the exact-SHA real-container gate. No foreign logger/
teardown/subscriber source is part of this checkpoint.


## TASK-ISO-023L accepted bounded original replay diagnostic retention

REQ-BC-055 / AC-ISO-006 retain authentic original resource bytes in the existing
node .log entries and artifact/schema. Actual b474 RF2/RF3 node1 logs reached
2000 lines; a first rate-limited denial/configuration can otherwise be displaced
by thousands of ordinary errors before capture stops. Stop precedes app teardown,
so a disposal flush cannot prove this evidence retained.

Root freezes this disjoint three-file scope before writes: gates_audit owns only
ComparisonTests/Features/BenchmarkComparisons/ComparisonResourceLogBuffer.cs,
NEW ComparisonReplayDiagnosticLog.cs and NEW ComparisonReplayDiagnosticLogTests.cs.
Existing buffer tests, capture/teardown/Stop/subscriber files, logger/product
runtime, schemas, selectors, config, workflow and docs are excluded. Root owns
integration/docs/Git. Inherited capable model handles bounded collection/parser
risk; existing active slots and disjoint scope avoid another route.

Only closed full original lines can claim protected slots: strictly validated
28-character UTC yyyy-MM-ddTHH:mm:ss.fffffffZ followed by exactly seven ASCII
spaces, then the exact numeric ReplicaReplayConfigured or ReplicaReplayCapacity
message grammar. No arbitrary prefix/substring/category/header association,
secret-bearing trailing text or generated/reformatted line. b474 proves this
native console prefix; the new record itself awaits original native observation.
Unknown/truncated/malformed/extra/overflow/negative fields remain ordinary tail.
Configuration validates voters1..3, four positive capacities, exact summed
capacity*voters=nodeMaximum<=1048576. Capacity validates sender0..2/pool0..3/
method0..8, nonnegative counts/time/suppression, selected count=capacity>0,
counts sum<=voterMaximum, exact nodeMaximum/voterMaximum ratio1..3 and sender
within that ratio; all integer arithmetic rejects overflow. Where a retained
configuration exists, capacities/voter count must also agree. Do not infer
readiness, role or authenticated identity from this diagnostic grammar.

One chronological retained list plus fixed13 slots (one config and12 configured
sender/pool pairs) preserves the latest original line for each key. Replacement
keeps actual capture order. Evict oldest ordinary lines first under the original
maximumLines/maximumBytes/per-line UTF8 bounds; protected records additionally
have an8KiB cap and must also be evicted if tiny original budgets cannot hold
them. Never expand budgets, retain unbounded keys, discard failures from JSON,
change timing or append invented log metadata. Closed numeric lines contain no
payload/nonce/MAC/credentials/identity/address/exception text.

AC-ISO-006L pass/fail tests first:10k ordinary lines preserve original config/
latest quota bytes; repeated same key replaces while independent12 keys stay
bounded and ordered; exact total/protected/UTF8/tiny limits; malformed timestamp/
indent/unknown/duplicate/trailing/secret text/overflow/inconsistent counts/
capacity/voter/sender cannot displace protected evidence. Pure buffer inputs are
algorithm data, not mocked native services. Original independent resource/tail
regressions remain. Source full diff/build/format/governance precede exact-SHA
GitHub normal/scalar/comparison and real native1/2/3 original log proof. No local
tests or fabricated recorded success. Rollback removes this coherent source/test
unit; no persisted/wire migration. ADR stays Accepted until required evidence.


TASK-ISO-023L config-transition edge: a newly retained valid config demotes any
incompatible previously protected quota to ordinary in place, clears its fixed
slot and applies the unchanged budgets. Keep its original capture bytes/order;
future mismatching quotas remain ordinary. Protection requires full original
input grammar before truncation (or exact equality with bounded input); a tiny
line cap must never turn a valid prefix with extra/secret trailing text into a
protected record. Acceptance tests cover both arrival orders and truncation.


TASK-ISO-023L exact token hardening: explicitly require every numeric character
to be ASCII0..9 before parsing, rather than relying on BCL NumberStyles.None
alone (which is not the declared byte grammar). Embedded/trailing NUL, tabs
and non-ASCII digits fail recognition and cannot displace protected originals.
Same three-file ownership, no native format or pool behavior change; GitHub
cases and actual original prefix/record qualification remain required.


## TASK-ISO-023Q accepted actual GitHub regression selection join

REQ-BC-055 / AC-ISO-003/005/006/006L require execution of every new comparison
acceptance regression, not merely compilation. The existing comparison-images
job selects exact old admission/buffer classes and would omit the new replay
profile, Redis/Mongo projection and closed-retention classes. Root is sole
serialized owner of .github/workflows/ci.yml (currently no foreign diff) and adds
one explicit needs-gated contract step invoking those five exact TUnit classes
with the existing Release/no-build/no-restore command. Preserve all prior steps,
permissions, pinned actions, topology matrices, image semantics and no-skipped
suite requirement; no new test framework/local execution or green inference.
Tests continue against actual AppHost models, real owned Node projection process
and pure buffer/native DTO algorithm inputs; genuine native1/2/3 stays separate.
The job's retained original results/logs must show every selected case executed
at the repaired SHA before the dependent27 preflights and full270 can qualify.
Rollback removes only this additive selection step with its coherent test unit.
ADR remains Accepted until every source and actual runtime gate exists.


TASK-ISO-026K AC-ISO-004/005 clarification: deliberately unsorted submitted
JSON remains the request fixture, while exact persisted document, stream,
message inspection and leased-delivery payloads must all match the independently
specified canonical stored constant. Never normalize returned output to hide
defects. Real SDK/MCP/SQL native1/2/3 regression is mandatory; original2f RF1
stream assertion fails after50000 successful reads and is retained as baseline.


TASK-ISO-026R AC-ISO-002/003/006: initial native Redis sync may be pending
inside the existing bounded setup observation. Positive readiness requires
actual copies followed by every strict INFO/ROLE/AOF/native identity check.
Link-down does not permit GET or success; malformed/native errors still fail.
Real isolated1/2/3 preflights plus RedisNativeReadinessRegression exercise
a private native probe and replicated absent-probe caller cancellation without
configuration changes, fake dependencies or measured retries.


TASK-ISO-026PG AC-ISO-002/003/006: actual isolated PostgreSQL topology must
reserve exactly nodeCount-1 named permanent physical slots before initial
parallel backups, use them in standby recovery, and limit slot retention via
actual512MB max_slot_wal_keep_size. Native bootstrap1/2/3 and new slot/WAL-switch/
checkpoint/flush/replay regressions are required; model settings alone do not
prove native retention. Invalid selections, lost slots and missed copy cuts fail.


TASK-ISO-026M AC-ISO-002/003/006: actual Mongo bootstrap must renew
authorization on bounded cached shell objects after initial-sync user UUID
replacement, retaining original timeout and strict voting/copy contracts.
Isolated1/2/3 native setup and MongoNativeAuthenticationRegression require
actual authenticated ping/usersInfo on every direct node and equal persisted
user UUIDs, never credentials or fake/normalized auth success.


TASK-ISO-026KC AC-ISO-005/006: complete native append samples do not qualify
a job with failed target disposal. Cleanup uses <=16 original workers,20s
aggregate/30s host bounds, actual native deletion ACKs and drained tasks; every
owned client disposal is attempted with original failure retained. Pure closed
diagnostic tests plus actual55378-stream teardown and small genuine SDK delete/
foreign-preservation regression execute on each1/2/3 topology. Pre-ACK ownership
collision and genuine quorum-loss fault drain remain separately tracked gaps.
