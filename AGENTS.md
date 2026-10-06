# KeyLoad

Implement the architecture in `docs/design/architecture-v0.3.uk.md`. Treat it as the product specification; user instructions control scope and authorization.

Work in this checkout. Preserve unrelated changes. The first server topology is an RF3 cluster; do not replace it with an in-memory or single-node demo. Keep the atomic partition distinct from physical placement. A node-local PartitionHost owns storage, journals, file locks and the apply gate; Orleans grains route commands.

Orleans is the foundation of the database and its cluster. Do not use DotNext.AspNetCore.Cluster or DotNext.Net.Cluster. Enable the distributed grain directory and activation migration/repartitioning. Each request uses a separate Orleans grain which invokes the required database grains. Keep storage ownership in node-local hosts when routing activations migrate.

Use TUnit for all tests. Run the RF3 database in Docker through Aspire and exercise its public operations through the real .NET SDK client and official MCP C# SDK client. Database credentials and authorization policies are persisted in the database; clients cannot supply trusted roles. Provide chunked blob storage and partial reads, a simple agent API, and an integrated official C# SDK MCP server for all database operations, search and storage. Keep frequent progress updates in Ukrainian stating what is ready and what remains.

Use .NET 10 and centrally pinned packages. Do not generate or commit packages.lock.json; ignore build outputs, local data and test artifacts. Required checks: build the solution, TUnit unit tests, real process recovery tests, and Docker/Aspire RF3 tests through the .NET and MCP clients. Keep docs/implementation/status.json and the README honest about qualification gates. Do not claim power-loss durability from process-kill tests or production readiness without the endurance and fault gates.

- Owner correction 2026-10-03 requires unified Aspire AppHost orchestration for test execution and KeyLoad test deployments. The AppHost MUST own the Docker RF3 resources, their readiness, test-runner dependencies, execution and shutdown. Tests MUST start the intended AppHost topology and run against its discovered endpoints; manually starting or leaving three independent Docker nodes running is not an equivalent test deployment. Preserve real .NET SDK and official MCP SDK clients, explicit RF3 membership, scoped fault injection and cleanup of every owned resource. Unit and process-recovery runners MUST use the same Aspire-owned test entry point without pretending they require an RF3 topology; required CI suites and acceptance gates remain mandatory.

ManagedCode packages are our projects. Fix dependency defects in their owning sibling repository, with regression tests, canonical patch release, successful GitHub publication and verified NuGet availability before updating KeyLoad. Preserve unrelated work; never force-push or bypass repository protections. The user authorizes scoped dependency repair commits, pushes and releases, and stable KeyLoad commits and pushes to main.

## Git workflow
- Never run `git stash` or use automatic stashing through another tool. Keep working-tree changes visible instead of hiding them in a stash.
- When the user requests committing all current changes, commit the full requested working-tree scope on the currently checked-out branch; do not silently omit existing changes or move them to another branch.
- Owner correction 2026-10-03 requires a commit immediately when the owner requests all current code and a further commit after each completed implementation stage. Commit the complete requested scope on the current branch, preserve other agents' work, and state the actual verification status of each checkpoint; a commit alone does not satisfy acceptance or authorize bypassing required delivery gates.

## System-critical SMID and operation efficiency
- Owner selection 2026-10-03 fixes [ZoneTree](https://github.com/ZoneTree/ZoneTree) as KeyLoad's canonical database storage and [ZoneTree.FullTextSearch](https://github.com/ZoneTree/ZoneTree.FullTextSearch) as its selected full-text index implementation. Integrate their actual native APIs with node-local ownership, canonical committed records, bounded replay/rebuild, persisted authorization and RF3 qualification. Provider selection does not waive correctness, privacy, durability, resource or performance acceptance gates.
- Owner correction 2026-10-03 removes Garnet from KeyLoad's active engine choices, dependencies, raw-storage experiments and planned migration work. Remove active Garnet/Tsavorite candidate implementation, dispatch paths and obsolete evaluation plans; do not keep comparing or selecting it as a KeyLoad replacement. Preserve truthful immutable historical results as history, with the active decision and canonical package manifests identifying ZoneTree and ZoneTree.FullTextSearch.
- KeyLoad is one database for AI agents spanning documents, relational tables, graphs, vectors/search, files/blobs, queues and events. SQL MUST be the central versioned language across those capabilities, compiled to the same authorized bounded database operations as SDK/MCP callers; a scalar document-only dialect is an initial stage, not the completed product (owner direction 2026-10-02).
- Full SQL syntax and a defined client connection protocol are mandatory product requirements. Maintain an explicit versioned dialect/conformance inventory and real client interoperability tests; limited SELECT/CALL parsing or an HTTP endpoint alone MUST NOT be advertised as full SQL or SQL-client protocol compatibility. Preserve persisted authorization, RF3, one grain per request, node-local ZoneTree ownership and measured resource bounds across every SQL transport (owner clarification 2026-10-03).
- The owner clarified on 2026-10-02 that all models run in one server and may be linked to each other; this means unified database/model composition, not a new single-physical-file storage format. Preserve canonical entity references so relational rows can participate in graphs, vectors, events and queued workflows.
- The owner clarified on 2026-10-03 that the core product is one composable database for AI agents: documents, typed tables, graphs, blobs, queues, events, vectors/search and time series coexist and reference one another within one database. Logical tables, collections and queues MUST NOT become isolated model silos. One authorized bounded SQL request MUST be able to read queue messages, resolve their referenced entities and write derived knowledge-graph relationships, and perform the reverse graph-to-queue flow, with explicit same-partition atomicity, authorization, read-cut, retry and cross-partition contracts. SQL is the familiar shared language for this composition. Keep this purpose explicit in ADRs, feature requirements, architecture, README and website, while distinguishing delivered syntax and qualification from the complete product target.
- Treat relational integrity and unified SQL as first-class product workstreams alongside SIMD and operation efficiency. Specify typed rows, keys/constraints, joins, atomic boundaries, unsupported diagnostics and measured performance before advertising capability; preserve RF3 and node-local storage ownership. A unified database/file delivery requirement MUST NOT be interpreted as permission to discard replication journals or promise a single physical file without a qualified storage-format ADR.
- Treat the owner's SIMD work (earlier written as SMID) as a first-priority product workstream and keep it visible in implementation planning and status. On 2026-10-02 the owner confirmed SIMD means vectorized CPU operations and directed .NET intrinsics first, with Rust considered only after profiling. Map its canonical slices, REQ/AC criteria, scalar correctness/portability contract and required ADRs before implementation; never claim acceleration without comparable GitHub measurements.
- Orleans provides request isolation, cluster routing and activation movement. Keep the one-grain-per-request boundary and RF3 topology; treat Orleans Streams as a first-class architecture workstream to specify and qualify alongside persisted KeyLoad EventStreams. Keep their distinct delivery/restart contracts explicit, and define bounded resource, concurrency, backpressure, recovery and performance behavior for every operation.
- Owner direction 2026-10-05 requires evaluating and using native Orleans execution/scheduling primitives where their guarantees fit: bounded `[StatelessWorker]` pools for interchangeable independent work, `[OneWay]` only for loss-tolerant advisory signals, durable jobs for persisted at-least-once scheduled work with idempotent effects, and selective `[Reentrant]`, `[AlwaysInterleave]`, `[MayInterleave]` or `[ReadOnly]` scheduling after an await/state-invariant audit. Freeze per-grain/method choices, limits, provider/version maturity, identity isolation and restart/failover contracts in ClusterRouting requirements and an ADR before integration; qualify correctness and resource/performance behavior through Aspire RF3. Preserve the unique request grain, persisted authorization, scoped read cuts, ordered node-local commit/apply ownership and RF3 acknowledgement barriers; these primitives MUST NOT become blanket concurrency attributes or substitutes for durable receipts.
- Owner clarification 2026-10-05 explicitly includes Orleans Grain Services, silo-local DI services and startup/lifecycle facilities alongside Durable Jobs in native capability selection. Choose them by actual per-activation, per-silo, cluster-partitioned or durable-work ownership; freeze startup dependencies, readiness, admission, cancellation and joined shutdown before integration. A service registered on every silo MUST NOT be treated as a cluster singleton or a durable job queue; retain node-local storage ownership and route every database effect through its separately authorized request grain.
- Owner reiteration 2026-10-04 requires Orleans to own database execution and coordination, including search/index work and long operations, through the separate grain-per-request boundary and node-local storage owners. Use our centrally pinned ManagedCode.Communication CQRS APIs and their native IAsyncEnumerable result/operation streaming contracts for streamed results and long operations. Freeze typed commands, queries, progress/results, cancellation, bounded buffering, backpressure, authorization, terminal failures and restart/failover semantics in the feature requirements and ADR before implementation; qualify actual SDK/MCP consumption through Aspire RF3. Do not replace these owned CQRS APIs with a parallel dispatcher or unbounded materialization, and repair dependency defects in the owning Communication repository under the mandatory release policy.
- Owner direction 2026-10-04 selects native Orleans RequestContext for bounded typed request/identity state propagation between grain calls and authorizes adopting our ManagedCode.Orleans.Identity packages for that context path. Reuse the owning native APIs; freeze the exact context contract, trust boundary, registration, cancellation/disposal restoration, concurrent-stream isolation and restart/migration behavior before integration. Verify server-authenticated request identity against the signed request and persisted authorization; propagated context does not authorize caller-supplied roles or replace committed ZoneTree state, node-local storage ownership or RF3 receipts. Keep secrets and user payloads out of diagnostic context, and repair any ManagedCode identity dependency defect in its owning repository under the mandatory release policy.
- Owner direction 2026-10-05 requires our ManagedCode.MCPGateway as KeyLoad's agent-facing MCP authentication and bounded tool-discovery composition. Agents MUST discover the relevant operation tools on demand instead of receiving the complete large operation catalog by default. Preserve actual official MCP SDK interoperability, server-authenticated signed requests, persisted credentials and authorization, exact operation schemas/effect hints, bounded admission/cancellation, and the separate Orleans request grain for every database execution. Define the native gateway registration, catalog visibility, invocation and trust contracts in ClientApi requirements and an ADR before implementation; dependency defects follow the owning-repository repair/release policy.
- Owner direction 2026-10-05 requires our ManagedCode.MarkdownLd.Kb knowledge-graph dependency, owned by the markdown-ld-kb repository, for graph-based MCP tool search. Reuse its native APIs through ManagedCode.MCPGateway for bounded search over canonical tool metadata and relationships; discovery MUST respect current persisted authorization and MUST NOT index credentials or user payloads. Keep tool discovery state disposable and distinct from authoritative ZoneTree database models, freeze privacy, bounds, ranking and rebuild contracts before integration, and qualify the actual gateway/official MCP caller flow through Aspire RF3.
- Owner direction 2026-10-05 requires a bounded MCP resource and ready-to-use prompt explaining KeyLoad and the actual discovery/schema/invocation workflow. Publish them through the same official MCP server with fresh persisted authentication, keep guidance static and free of credentials, caller data and private inventories, and verify resource/prompt interoperability through the real official SDK without expanding the initial operation-tool catalog.
- Optimize every operation and its shared serialization, validation, routing and storage execution paths against their correctness and fault contracts, using representative multi-node GitHub qualification to measure latency, throughput, allocations, memory, contention and backlog where applicable. The owner reiterated on 2026-10-03 that high performance and minimum practical memory use are required across the entire implementation, including shared hot paths. Prioritize avoidable allocations, retained memory and repeated work; verify latency, throughput, allocations and memory before/after without weakening correctness, authorization, bounds or durability. Architecture choices or local builds alone do not prove maximum scalability or performance; select further optimization from actual comparable measurements.
- Use ZoneTree's native storage APIs correctly and Orleans for bounded parallel execution of independent operation work. Preserve node-local storage ownership, the ordered atomic commit/apply gate, scoped read cuts, cancellation and backpressure; qualify the resulting performance in real multi-node GitHub runs (owner direction 2026-10-02).
- All KeyLoad-owned database models MUST use ZoneTree as their canonical storage foundation, including documents, relational rows, graphs, vectors/search, time series, blobs, queues and events. ZoneTree.FullTextSearch is the selected text-index implementation. Qualify native ZoneTree WAL below the unchanged Orleans RF3, node-local ownership and atomic-commit contracts; retain distinct replication and atomic-commit recovery journals until an explicit storage-format ADR and fault qualification prove any migration safe. Comparison databases retain their own real native storage.
- Use native generated Orleans binary serialization with stable `[GenerateSerializer]`, `[Id]` and `[Alias]` contracts for all KeyLoad-owned internal typed persistence and inter-grain/replica payloads, including atomic WAL mutations over ZoneTree. Preserve raw-byte native ZoneTree serialization, sortable keys, public HTTP/MCP JSON, exact user document content, frozen canonical identity digests, synchronous durability barriers, checksums, ordered atomic recovery and RF3 authority. Format changes require an explicit upgrade contract; do not retain runtime JSON fallback or claim speed/fault improvements without actual GitHub evidence (owner clarification 2026-10-03).
- Orleans distribution and Orleans-coordinated caches are mandatory product workstreams. Define bounded memory, admission, eviction, concurrency and backpressure, with cache identity and validation tied to authorized scoped read cuts and policy epochs. Specify invalidation after writes and authorization changes, and recovery after grain migration, failover and restart before implementation; cached state MUST NOT replace persisted authorization, committed ZoneTree state or RF3 durability guarantees (owner direction 2026-10-02).
- The owner confirmed on 2026-10-02 that memory means the KeyLoad RAM layer and Orleans caches. Deliver a bounded node-local RAM acceleration layer over committed ZoneTree data and its native WAL, coordinated through Orleans; RAM contents are disposable across restart or activation movement and MUST NOT become the authority for acknowledged writes, authorization or RF3 recovery.
- Model logical shards and cache coordination as Orleans grains behind the separate grain-per-request boundary. Shard-grain identity MUST remain distinct from physical replica placement; activation movement routes to node-local ZoneTree/WAL owners rather than moving open storage handles (owner clarification 2026-10-02).
- Treat ManagedCode.TimeSeries arithmetic, allocations, SIMD and batch processing as an owning-repository performance workstream. Select improvements from source review and profiling, publish and verify its canonical release before consuming it in KeyLoad, and retain numerical and compatibility regressions.
- Performance qualification MUST compare both single-node and native multi-node configurations against the complete competitor matrix with equivalent workload and acknowledgement/durability contracts. Retain exact-source measurements and regression evidence; the goal of leading competitors MUST NOT be reported as achieved without those results (owner direction 2026-10-02).
- Performance qualification MUST cover actual datasets of 100,000 and 1,000,000 records, with at least 100,000 measured operations per applicable workload cell, sequential and deterministic random reads, ordered/range search, indexing and complex queries. Record dataset size separately from BDN iteration/sample counts, validate actual loaded records and caller-visible results, and retain equal topology, durability, resource and workload contracts across supported competitors. Tiny hot-key fixtures remain explicitly labelled microbenchmark controls and MUST NOT substitute for this scale evidence or justify selecting a performance winner. Repeating more reads over the same tiny corpus does not establish performance at the required record counts. When the owner requests serious performance qualification, complete the representative long workloads rather than presenting short controls as the result; local experiments remain development-only and website figures require authenticated original GitHub artifacts (owner repeated correction 2026-10-03).
- Qualification MUST use Linux runners only; the owner explicitly replaced the three-OS qualification matrix on 2026-10-03. Preserve every required suite, scalar portability check, recovery and RF3 gate while removing redundant macOS/Windows execution.
- Each comparison database MUST run the same complete workload suite in its own isolated GitHub Actions runner/job agent, with only that database's native topology and load generator. KeyLoad RF3 and competitor clusters remain genuine clusters; different databases MUST NOT share a runner, process, containers, volumes or measurement session (owner direction 2026-10-03).
- Owner correction 2026-10-04 requires a separate named GitHub Actions job group and matrix for each comparison database. Keep each database's checks and workload cells together under its readable database name; do not flatten all databases into shared CRUD/specialized matrix groups. Preserve isolated runners per cell, the complete canonical inventory and one authenticated aggregation after every database group finishes.
- Each isolated comparison agent MUST retain its own source/run/attempt/target/topology/profile-bound JSON. After all target agents finish, one aggregation job MUST validate completeness, comparable settings and provenance, collect their results and generate the website metrics from those JSON files. Failed, missing, skipped or mixed-cohort measurements MUST NOT refresh published performance evidence (owner direction 2026-10-03).
- The comparison matrix MUST include actual native one-node, two-node and three-node configurations and intensive read/create/update/delete workloads. Each engine/node-count/scenario measurement MUST run on a separate isolated runner agent; record real membership, acknowledgement, correctness, latency, throughput and resource use. Do not relabel client counts or independent standalone databases as cluster node counts. Unsupported native/community topology remains explicitly unavailable, and benchmark topology changes require a fault/consistency ADR before implementation; the initial production RF3 requirement remains mandatory (owner direction 2026-10-03).

- Owner correction 2026-10-05 reduces every active benchmark scale inventory to exactly 100,000 and 1,000,000 records across all databases and workload families. Remove the 5,000,000-record profile from active planning, dispatch, validation and qualification requirements; preserve immutable historical evidence with its original settings. This rule-specific correction supersedes the earlier three-scale requirement without changing the minimum 100,000 measured operations or any correctness/fairness/provenance gate.
- Owner direction 2026-10-05 adds SurrealDB and HelixDB to the website-producing native Benchmarks pipeline and extends PostgreSQL + pgvector with exact, HNSW and IVFFlat search, filtered search and concurrent updates at 100,000 and 1,000,000 actual records. Compare every applicable database in one workload family with the same corpus, metric, filter, operation schedule, accuracy target and effective resource/durability contract; report latency, p95/p99, server RAM, index-build time and recall@k from authenticated original GitHub measurements. Review and repair existing workload comparability; unavailable native capabilities/topologies remain explicit without fabricated measurements.

## Licensing

- Owner final scope clarification 2026-10-05 requires permitting application use while reserving third-party hosted/managed database services for separate authorization from ManagedCode. Use the unmodified Elastic License 2.0 for new KeyLoad-owned distributions to implement this requirement without automatic expiry. This rule-specific direction supersedes the earlier BSL 1.1 selection, Apache 2.0 Change License and Change Date requests; those parameters MUST NOT remain active licensing promises.
- Keep README, website metadata and packaged license files consistent with the root `LICENSE`; describe Elastic License 2.0 distributions as source available, not open source. Preserve every third-party or independently owned dependency's original license and notices.
- Validate the complete license against its authoritative published text. Identify ManagedCode as KeyLoad's copyright holder and licensor; license names or their authors MUST NOT be presented as database dependencies or ownership of KeyLoad code.

## MCAF repository workflow

Project: KeyLoad
Stack: C# 14, .NET 10, Orleans, ZoneTree, Aspire, TUnit target, Node-built static Pages site.

Follows [MCAF](https://mcaf.managed-code.com/)

---

## Purpose

This file defines how AI agents work in this solution.
- Root `AGENTS.md` holds the global workflow, shared commands, cross-cutting rules, and global skill catalog.
- In multi-project solutions, each project or module root MUST have its own local `AGENTS.md`.
- Local `AGENTS.md` files add project-specific entry points, boundaries, commands, risks, and applicable skills.

## Solution Topology
- Solution root: `KeyLoad.slnx` and this root AGENTS.md.
- Repository boundary: all KeyLoad-owned product surfaces are delivered here; independently owned ManagedCode dependencies remain in their owning repositories under the mandatory repair/release policy above.
- Canonical slice convention: `<project-or-module>/Features/<SliceName>/`, PascalCase slice names, with `docs/Features/<SliceName>.md`.
- Solution-owned surfaces: backend `src/`; frontend `site/`; shared contracts `src/KeyLoad.Abstractions/`; tests `tests/`; benchmarks `benchmarks/`; infrastructure `src/KeyLoad.AppHost/` and `.github/workflows/`; tooling `scripts/`; durable documentation `docs/`.
- Every one of the 24 project roots listed in `docs/Architecture.md`, plus `site/`, `.github/workflows/`, `docs/` and `scripts/`, MUST have local AGENTS.md. Every new project/module MUST receive local policy before its implementation begins.
- Architecture entry point and complete project/slice map: `docs/Architecture.md`. Existing layout migration debt is recorded in ADR-032, with owner and removal date; it is not compliant target structure.

## Mandatory Solution Architecture (`MCAF-ARCH-001`)
- This solution MUST be delivered as one repository. All solution-owned backend, frontend, contracts, tests, infrastructure, and documentation live here and are versioned together.
- Vertical-slice architecture is mandatory across the entire repository.
- Owner correction 2026-10-04 requires responsibility-based structure inside every vertical slice. A `Features/<SliceName>/` directory MUST NOT become a flat dump of unrelated grains, commands, queries, models, contracts, streaming and infrastructure helpers. Keep applicable roles in explicit child folders such as `Grains/`, `Commands/`, `Queries/`, `Models/`, `Contracts/`, `Streaming/`, `Identity/`, `Serialization/` and `Topology/`, all within the owning slice; create only folders with actual owned code. Classify files from their implementation responsibility, not filename tokens; Models and Contracts MUST NOT hide executable readers, codecs, policy checks or lifecycle helpers. Feature-local role folders MUST NOT become repository-wide layers. Preserve stable namespaces, Orleans aliases/field IDs and runtime behavior during structural moves, and update source maps, path-bound checks and local policy in the same change.
- Every feature MUST use one canonical `<SliceName>` across backend, frontend, contracts, tests, and `docs/Features/`.
- Every technical root MUST organize feature-owned work under the same `Features/<SliceName>/` convention, or use one fully colocated executable-artifact convention; durable feature docs remain under `docs/Features/<SliceName>.md`.
- A feature surface that does not apply MUST be recorded as `N/A` with a reason in the feature spec; it must not be silently omitted.
- Layer-first folders such as repository-level `Controllers`, `Services`, or `Repositories` MUST NOT own feature behaviour.
- Only genuinely solution-wide entry points, composition roots, building blocks, infrastructure, and global docs may live outside a feature slice.
- Local `AGENTS.md` files and ADRs may tighten these rules or document time-bounded migration debt, but MUST NOT make split repositories, layer-first feature ownership, or inconsistent slice names compliant.
- Existing deviations require a migration ADR with affected paths, owner, target layout, verification, and removal date. New work MUST use the target slice structure and MUST NOT expand the deviation.

## Rule Precedence
1. Read the solution-root `AGENTS.md` first.
2. Read the nearest local `AGENTS.md` for the area you will edit.
3. Apply the stricter rule when both files speak to the same topic.
4. Local `AGENTS.md` files may refine or tighten root rules, but they must not silently weaken them.
5. A local exception MUST NOT weaken, omit or bypass any existing rule. Record a conflict in the nearest local `AGENTS.md`, ADR or feature doc and require explicit rule-specific owner direction for any actual policy change.
6. `MCAF-ARCH-001` has no target-architecture exception: a durable doc may record only a time-bounded migration deviation and may not declare that deviation compliant.

## MCAF Update Safety (`MCAF-GOV-001`)
- Every rule in this customized root `AGENTS.md` and every project-local `AGENTS.md` is mandatory repository policy.
- An MCAF install or update MUST read all existing root and local `AGENTS.md` files completely before editing them.
- Update by merging current requirements into the existing files. MUST NOT replace, overwrite, truncate, summarize away, or omit existing rules, sections, commands, boundaries, preferences, or exception records.
- MUST NOT weaken a rule by changing mandatory wording into optional guidance, narrowing its scope, reducing its priority, or adding a bypass.
- When incoming guidance overlaps or conflicts, preserve the stricter rule and report the conflict instead of silently rewriting policy.
- Template-only notes and placeholders may be replaced during first customization; a real rule may change only under explicit, rule-specific owner direction.
- Before completion, review the full governance diff and verify that every existing rule remains present and equally or more strict.

## Mandatory Feature Requirements and ADR Implementation (`MCAF-REQ-001`)
- Every non-trivial feature MUST have a real `docs/Features/<SliceName>.md` specification before implementation.
- The feature spec MUST contain stable `REQ-*` requirements and `AC-*` acceptance criteria with measurable pass/fail conditions, positive/negative/edge/error flows, and one canonical slice map.
- Every `REQ-*` MUST map to at least one `AC-*`; every `AC-*` MUST map to automated tests or an explicit exception with required manual evidence.
- The feature spec MUST link each required ADR, or record `ADR: N/A` with a concrete reason why existing architecture and contracts are sufficient.
- Create or update an ADR before implementing changes to boundaries, public contracts, data, dependencies, security/trust boundaries, deployment topology, cross-cutting standards, or migration architecture.
- Every required ADR MUST contain an implementation contract: related `REQ-*`/`AC-*`, ordered stages, exact slice/file ownership, dependencies, migration/rollout/rollback, tests, verification, agent roles, and integration/join points.
- An ADR MUST NOT be marked `Implemented` until its required implementation, migration, tests, docs, and verification evidence exist.
- Keep a traceability chain from `REQ-*` to `AC-*`, ADR, `TASK-*`, automated test, and final evidence. Update the whole chain before continuing when a requirement changes.

## Conversations (Self-Learning)

Learn the user's stable habits, preferences, and corrections. Record durable rules here instead of relying on chat history.

Before doing any non-trivial task, evaluate the latest user message.
If it contains a durable rule, correction, preference, or workflow change, update `AGENTS.md` first.
If it is only task-local scope, do not turn it into a lasting rule.

Update this file when the user gives:
- a repeated correction
- a permanent requirement
- a lasting preference
- a workflow change
- a high-signal frustration that indicates a rule was missed

Extract rules aggressively when the user says things equivalent to:
- "never", "don't", "stop", "avoid"
- "always", "must", "make sure", "should"
- "remember", "keep in mind", "note that"
- "from now on", "going forward"
- "the workflow is", "we do it like this"

Preferences belong in `## Preferences`:
- positive preferences go under `Likes`
- negative preferences go under `Dislikes`
- comparisons should become explicit rules or preferences

Corrections should update an existing rule when possible instead of creating duplicates.

Treat these as strong signals and record them immediately:
- anger, swearing, sarcasm, or explicit frustration
- ALL CAPS, repeated punctuation, or "don't do this again"
- the same mistake happening twice
- the user manually undoing or rejecting a recurring pattern

Do not record:
- one-off instructions for the current task
- temporary exceptions
- requirements that are already captured elsewhere without change

Rule format:
- one instruction per bullet
- place it in the right section
- capture the why, not only the literal wording
- Existing rules MUST remain present and mandatory; change or remove a rule only under explicit rule-specific owner direction, never as framework cleanup.

## Global Skills
The owner's 2026-10-06 approval to implement the reviewed native DurableJobs path also authorizes its matching native Journaling API (`ORLEANSEXP005`). Confine that opt-in to the journal provider, native jobs integration and their registration/tests under ADR-110; do not use a global NoWarn or suppress unrelated diagnostics. Journal metadata remains RF3-authoritative and distinct from creator-authorized business effects.

The explicit owner instruction to enable Orleans distributed directory and activation repartitioning is consent to those two native experimental APIs. Confine compiler opt-in ORLEANSEXP003/ORLEANSEXP001 to their two configuration calls, with ADR-034 evidence; it does not authorize global NoWarn, suppression of quality diagnostics or changing analyzer severity.

- On 2026-10-05 the owner explicitly authorized installing the Managed Code `quality` bundle from https://skills.managed-code.com/bundles/quality/ and running KeyLoad complexity, CRAP and related code-quality analysis. Use the catalog's current bundle skill names and native installation command; this scoped permission supersedes the historical skill-installation prohibition and older `mcaf-*` naming requirement for this bundle only. Preserve existing quality thresholds and use the Aspire-owned entry point for coverage-producing tests.
- The authorized quality bundle is installed globally under `/Users/ksemenenko/.codex/skills/<skill>/SKILL.md`: `quality-ci`, `analyzer-config`, `format`, `csharpier`, `code-analysis`, `roslynator`, `meziantou-analyzer`, `stylecop-analyzers`, `complexity` and `crap-score`. These are installed skills; the historical bootstrap and empty repository-local skill inventory remain distinct.
- On 2026-10-01 the owner explicitly authorized installing the Orleans skill through the `dotnet skills` command. This rule-specific permission applies to the requested Orleans skill; other skill installation remains prohibited unless separately authorized. Read and apply its installed SKILL.md before continuing Orleans implementation.
- Orleans 3.1.1 is installed globally at `/Users/ksemenenko/.codex/skills/orleans/SKILL.md`, from Managed Code catalog `2026.10.1.0` using `dotnet-skills` 0.1.242. It is applied to Orleans design, lifecycle, transport, routing and failure verification. This is the specifically authorized addition; the historical bootstrap installed no skills.
- No MCAF or .NET skills are installed for this bootstrap. The owner explicitly instructed: do not install skills. Do not create skill directories, install tools or modify global agent configuration for this task.
- Catalog links are informational: MCAF `https://mcaf.managed-code.com/skills`; .NET `https://skills.managed-code.com/`. Installed repository skill catalog: empty.
- If the owner later explicitly authorizes skill installation, source .NET skills from the Managed Code Skills catalog, use `mcaf-dotnet` as the routing entry, keep only current `mcaf-*` skills and exactly the TUnit framework skill `mcaf-dotnet-tunit`. Tool-specific skills MUST match tools actually used. Recheck build/test/format/analyze/complexity/coverage commands after an authorized upgrade.
- Local AGENTS.md MUST state applicable skill status honestly; a named catalog entry is not an installed skill.

## Rules to Follow (Mandatory)

### Continuous Quality Review (owner direction 2026-10-06)

- Quality review is a mandatory development loop for every code change. Establish the relevant baseline before non-trivial implementation, review each completed implementation stage, and finish the review before committing that stage or declaring the task complete. Apply this rule to changed code and affected shared execution paths so quality improves throughout development.
- Read the installed `SKILL.md` instructions and review all ten skills sequentially: `quality-ci` → `analyzer-config` → `format` → `csharpier` → `code-analysis` → `roslynator` → `meziantou-analyzer` → `stylecop-analyzers` → `complexity` → `crap-score`. Each applicable skill MUST produce an actual check, finding or improvement; record a concrete reason when a skill or tool is N/A. A checklist alone is not verification.
- Preserve the repository's selected tools and `.editorconfig`. `dotnet format` remains the canonical formatter; CSharpier execution is N/A while that choice remains in force. Review third-party analyzer findings against repository contracts; skill use does not automatically authorize adding analyzer packages, applying conflicting defaults, suppressing diagnostics, reducing severity or weakening existing limits and gates.
- Report every confirmed actionable finding with its source location, severity, consequence and verification status. Keep advisory diagnostics distinct from confirmed defects. Fix confirmed problems within the authorized scope, add meaningful regressions where behaviour is affected, and rerun the affected checks after each fix. ManagedCode dependency defects MUST follow the owning-repository repair, release and verified-publication policy.
- Review actual complexity, coupling, nesting, allocation and maintainability risks. Refactor around cohesive responsibilities and preserve operation contracts; moving branches between helpers alone does not demonstrate a quality improvement. Retain the relevant before/after measurements and obey the existing maintainability limits.
- CRAP calculations MUST combine actual source-mapped complexity with measured functional-test coverage from the same source/build cohort. Retain original coverage evidence collected through the Aspire-owned entry point; performance, load, stress and comparison runs do not contribute. Missing or stale coverage is unmeasured and MUST NOT become a fabricated zero, a passing CRAP score or a passing coverage gate.
- Complete the canonical format, analyzer, final solution build and required test/coverage gates after the final code edits. Tests MUST use the Aspire-owned entry point and preserve every required acceptance suite. A failed or blocked required check keeps implementation incomplete; report the exact remaining problems and blockers. For documentation-only changes, run the relevant static governance and diff checks and record code-execution checks as N/A.

### Commands
- Owner correction 2026-10-03 replaces direct test execution commands here and in local policies with the actual Aspire entry: after restore/build, run `dotnet run --project src/KeyLoad.AppHost --no-build --no-restore --configuration Release -- --KeyLoadTests:Suite=<suite>`. The closed suites are `analyzers`, `unit`, `unit-scalar`, `recovery`, `rf3`, `comparison` and `site`. Preserve all required suites, original MTP/TRX/coverage artifacts, native comparison isolation and exact-source Linux qualification. Optional bounded filters select development cases only; they cannot qualify a complete suite. Direct `dotnet test` is the native child process composed by this AppHost, never an alternative caller entry.
- Owner correction2026-10-03 explicitly authorizes local development tests, recovery checks and performance experiments, using BenchmarkDotNet for .NET microbenchmarks. This rule-specific correction supersedes the historical GitHub-only execution restrictions below and in local AGENTS.md for development verification. Local engine comparisons use separate owned fixtures/processes and sequential measurement sessions on the actual local machine; the different-GitHub-runner rule remains mandatory for global qualification. Keep local evidence clearly labelled with its actual source/settings/machine and do not present it as GitHub qualification. Global comparison measurements published on the website MUST still come exclusively from authenticated GitHub Actions runs and their original artifacts. Required delivered-source CI/fault/topology/endurance gates remain mandatory; local results cannot satisfy the website provenance contract.
- Owner reiteration2026-10-03 distinguishes purpose as well as provenance: run local BenchmarkDotNet freely within bounded owned experiments to profile and optimize code; use exclusively genuine GitHub-native database comparison measurements for the public website, with matched verified hardware/resource/topology/durability/workload conditions for experimental fairness. Separate runners alone do not prove equivalent hardware; bind actual machine metadata and reject a mismatched cohort. Local results remain development evidence even when useful for choosing the next optimization.
- The owner reiterated in this serialization review on 2026-10-03 that tests may run locally. Run local TUnit development regressions and bounded recovery/performance experiments when useful; this explicitly supersedes the former local-execution prohibition. Required delivered-source GitHub qualification still retains the exact SHA, run/job URL and original artifacts. Development builds and static source/governance checks are not runtime test results.
- `restore`: `dotnet restore KeyLoad.slnx` with centrally pinned versions; current root forbids generating/committing package lock files. The historical CI baseline used locked mode; the current workflow is migrating to the no-lock policy.
- `build`: `dotnet build KeyLoad.slnx --no-restore --configuration Release`; warnings-as-errors and current analyzers are configured in Directory.Build.props.
- `test`: dispatch `gh workflow run ci.yml --repo managedcode/KeyLoad --ref main`, inspect `gh run view <run-id> --repo managedcode/KeyLoad`, and download the resulting test/comparison artifacts. CI executes `dotnet test --project tests/<project> --no-build --no-restore --configuration Release` for unit, recovery, RF3 and comparison suites. No skipped suite can count as passing.
- `format`: `dotnet format KeyLoad.slnx --verify-no-changes --no-restore` is the required formatter command and CI gate; source configuration does not establish a green formatter qualification.
- `analyze`: solution Release build with TreatWarningsAsErrors=true, AnalysisLevel=latest-all, SDK/style analysis and centrally attached KeyLoad.Analyzers; compiler SARIF reports are retained under artifacts/code-quality.
- `complexity`: numeric policy limits below are mandatory and are enforced by source-owned KLD0030/KLD0031/KLD0032/KLD0033 during ordinary consumer builds; the analyzer infrastructure has a real compiler source-inventory fixture in CI. Configuration or a scoped build MUST NOT be reported as a passing full gate without the exact-SHA complete build and fixture qualification.
- `coverage`: owner direction 2026-10-05 requires native MTP/TUnit-compatible code-coverage collection through the Aspire-owned test entry point, retained original reports, and module/file/line/branch gap analysis. Only functional operation tests may contribute; load, stress, performance and database-comparison runs MUST NOT contribute to coverage totals. Until a complete source-bound report exists, report coverage as unmeasured rather than inventing a numeric baseline or claiming the thresholds below pass.
- `governance`: `node scripts/Features/RepositoryGovernance/verify.mjs` validates the real inventory and policy-preservation record; it is static installation validation, not a runtime test result.
- Active runner: Microsoft.Testing.Platform in global.json; mandatory framework: TUnit. Existing xUnit/VSTest references are migration debt, not a permitted second framework.
- .NET target is net10.0. C# 14.0 is explicitly pinned in Directory.Build.props. Root .editorconfig is the source of truth for formatting/style/analyzer severity; nested files require a concrete subtree purpose.
- Use the Prostir root `.editorconfig` as the owner-selected baseline. Enable .NET static analyzers and build-time code-style checks for every solution project, with warnings treated as errors.
- Keep repository-owned Roslyn rules in the KeyLoad analyzer project, attach them centrally to consuming projects, and retain compiler diagnostic reports so rules can be authored and verified in code.
- Canonical CI: `.github/workflows/ci.yml`; Pages publication: `.github/workflows/pages.yml`. Skills installed by this bootstrap: none.
- The latest owner correction 2026-10-03 supersedes that historical placement and the five-workflow dispatch: `gh workflow run ci.yml --repo managedcode/KeyLoad --ref main` builds/tests the project; `benchmarks.yml` runs all load/comparison suites then qualifies/publishes the same-run website; `release.yml` builds and publishes real database/packages/images with an immutable dated tag. Only these three workflows remain. All test qualification remains GitHub-only.

### Project AGENTS Policy
- Multi-project solutions MUST keep one root `AGENTS.md` plus one local `AGENTS.md` in each project or module root.
- Each local `AGENTS.md` MUST document:
  - project purpose
  - entry points
  - boundaries
  - project-local commands
  - applicable skills
  - local risks or protected areas
- If a project grows enough that the root file becomes vague, add or tighten the local `AGENTS.md` before continuing implementation.

### Agent Orchestration
- `MCAF-AI-001` is mandatory for non-trivial work: use the strongest suitable large or high-capability model available for planning, architecture, acceptance criteria, decomposition, integration, and final review.
- Do not begin delegated implementation until the planning model has made the scope, boundaries, contracts, acceptance criteria, test strategy, and ordered plan explicit.
- Do not begin write-capable delegated implementation until `MCAF-REQ-001` requirements and every required ADR implementation contract are approved.
- After planning, spawn cost-efficient coding workers for every independent, bounded implementation scope when a suitable cheaper model is available.
- Choose the least expensive model that is still capable of the language, framework, tools, context size, and risk level. Cost MUST NOT override correctness, security, or verification.
- Every coding worker MUST receive the goal, acceptance IDs, exact file/module/slice ownership, architecture and contracts to preserve, constraints, forbidden changes, expected artifacts, verification commands, and escalation conditions.
- Coding workers MUST stop and escalate rather than invent architecture, change public contracts, weaken tests or rules, or expand scope.
- The planning model MUST inspect every delegated diff, compare it with the plan and acceptance criteria, integrate the results, resolve conflicts, and own final quality gates and completion.
- Build an explicit task graph before spawning: task ID, requirements/acceptance IDs, owner, model tier/effort, permissions, dependencies, start condition, artifacts, verification, completion state, and join condition.
- Research and analysis agents SHOULD be read-only and may run in parallel before implementation. Use stronger reasoning for ambiguous architecture/security/review work and cost-efficient capable models for bounded discovery, log/test analysis, and documentation lookup.
- For large, non-trivial, cross-module, research-heavy, or implementation-heavy tasks, the lead agent's primary job is to plan the work, identify all parallelizable workstreams, split them into independent scopes, and spawn subagents to execute those scopes in parallel.
- Before writing the implementation plan, explicitly look for parallel tasks across research, code ownership areas, test creation, verification, documentation, and review.
- Spawn subagents for every independent workstream that can run safely in parallel unless there is a concrete coordination, risk, or ownership reason not to.
- Give each subagent a concrete responsibility, clear file or module ownership, expected output, and verification duty.
- Subagents that write code MUST own disjoint write scopes and must not revert or overwrite work from other agents.
- Shared contracts, solution files, central configuration, migrations, and cross-cutting docs MUST have exactly one integration owner. Same-file edits MUST be serialized.
- Use platform-native agent status, messaging, and wait controls. Monitor agents needing input or drifting; steer, retry, replace, or escalate them explicitly.
- Wait for every required dependency and result before integration. `idle`, partial output, a plan, or an unverified worker claim is not `complete`.
- Every delegated task MUST end as `complete`, `blocked`, `failed`, or `cancelled` with artifacts and evidence appropriate to that state. Blocked or failed tasks MUST NOT unblock dependants.
- The lead MUST verify the combined repository state after joining worker results; worker-local checks are not integrated proof.
- The lead agent remains responsible for the final architecture, integration, conflict resolution, quality gates, and completion criteria.
- Do not serialize independent work when safe parallel execution is available.
- Keep the orchestration lightweight for simple, short, or obvious tasks; do not create subagents when coordination overhead would be larger than the task.
- If non-trivial coding cannot be delegated because model-tier routing is unavailable, no cheaper model is capable, or safe ownership cannot be separated, record the concrete reason before the planning model implements it.

### Maintainability Limits

These limits are repo-configured policy values. They live here so the solution can tune them over time.
- `file_max_loc`: `400`
- `type_max_loc`: `200`
- `function_max_loc`: `64`
- `max_nesting_depth`: `3`
- `exception_policy`: `Document any justified exception in the nearest ADR, feature doc, or local AGENTS.md with the reason, scope, and removal/refactor plan.`

Local `AGENTS.md` files may tighten these values, but they must not loosen them without an explicit root-level exception.

- Owner correction 2026-10-06 sets the solution-wide executable-unit limit to 64 code lines, replacing the earlier 50-line limit. Keep file400, aggregate type200 and nesting3 limits; verify the exact boundary through real compiler/analyzer operations at64 and65 lines.

### Task Delivery
- Start from `docs/Architecture.md` and the nearest local `AGENTS.md`.
- Treat `docs/Architecture.md` as the architecture map for every non-trivial task.
- If the overview is missing, stale, or diagram-free, update it before implementation.
- Apply mandatory policy `MCAF-ARCH-001` before choosing implementation paths.
- Keep each feature in one repository-wide vertical slice, using the same canonical slice name for its backend, frontend, contracts, tests, and documentation.
- Change all affected surfaces of the owning slice together; do not move one surface into another repository or an unrelated layer folder.
- Prefer the smallest relevant feature slice over repo-wide scanning so context stays narrow.
- Define scope before coding:
  - in scope
  - out of scope
- Keep context tight. Do not read the whole repo if the architecture map and local docs are enough.
- If an already available skill fits the task, follow it; do not install skills for this bootstrap. Record required guidance directly in the feature/ADR contract when no skill is installed.
- Analyze first:
  - current state
  - required change
  - constraints and risks
- Before starting design analysis, decide whether the task is actually non-trivial.
- For non-trivial work, record problem framing, options, trade-offs, risks, open questions and the recommended direction in the owning feature specification and required ADR before code or doc implementation.
- For simple, short or obvious work, go directly to execution.
- Keep temporary brainstorms and working checklists in agent context or outside the checkout; do not create separate planning Markdown files in the repository.
- Think through options before committing to implementation details.
- Before ordering implementation, define the acceptance contract in the owning feature specification.
- The acceptance contract MUST be detailed enough that another agent or maintainer can implement and test the task without rereading the conversation.
- The acceptance contract MUST contain:
  - task goal and user-visible outcome
  - in-scope and out-of-scope behaviour
  - assumptions and open questions
  - actors, entry points, permissions and affected boundaries
  - numbered criteria with stable IDs such as `AC-001`, `AC-002` and `AC-003`
  - clear pass and fail conditions for every criterion
  - positive flows, negative flows, edge cases and unexpected/error paths
  - data, contract, API, UI, persistence, performance, security and observability expectations where relevant
  - migration, compatibility and rollback expectations where relevant
  - a criterion-to-test matrix mapping each `AC-*` to automated tests, test level, assertions and verification commands
  - explicit criteria without automated coverage, with the reason and required manual or review evidence
- Do not start implementation until the acceptance contract exists and the test strategy maps back to it.
- Derive the ordered execution contract from the accepted criteria and keep it in the feature specification and required ADR, with one canonical source for each fact.
- The execution contract MUST contain:
  - the chosen design analysis and acceptance contract
  - implementation steps derived from the accepted `AC-*` criteria
  - task goal and scope
  - detailed ordered implementation steps, constraints and risks
  - explicit test steps within the ordered implementation, not as a later add-on
  - a test strategy for every step, covering each `AC-*` with a test or documented exception
  - the testing methodology: flows, commands and quality bar
  - a full relevant test baseline before implementation
  - a tracked list of already failing tests, their failure symptoms, suspected causes and intended fixes
  - checklist completion conditions
  - ordered final validation skills and commands, with the reason for each
- Use the Ralph Loop for every non-trivial task:
  - analyze the problem and options before implementation
  - write detailed acceptance criteria in the owning feature specification
  - map every criterion to automated tests or a documented exception before coding
  - derive the ordered execution contract in the feature specification and required ADR
  - include test creation, updates and verification from the start
  - run the full relevant suite to establish the real baseline once the execution contract is ready
  - record existing failures, symptoms, suspected causes and fix status in the execution contract
  - reproduce and fix failures one by one, rerun the tests and update their status
  - include ordered final validation skills, with each producing a concrete action, artifact or verification outcome
  - execute one step at a time and track its completion
  - when criteria change, update the acceptance contract, mapped tests and execution contract before continuing
  - review findings, apply fixes and rerun relevant verification
  - repeat until all completion conditions pass; document blockers without weakening an acceptance criterion or mandatory rule
- Implement code and tests together.
- Run verification in layers:
  - changed tests
  - related suite
  - broader required regressions
- If `build` is separate from `test`, run `build` before `test`.
- After tests pass, run `format`, then the final required verification commands.
- Run every repo-defined quality gate that is available for the stack and change scope, including analyzers, linters, complexity checks, coverage, architecture checks, security checks, and any other configured tools.
- The task is complete only when every planned checklist item is done, every acceptance criterion is satisfied, and all relevant tests are green.
- Summarize the change, risks, and verification before marking the task complete.

### Documentation
- All durable docs live in `docs/` (or `.wiki/` if the repo already uses it).
- `docs/Architecture.md` is the required global map and the first stop for agents.
- `docs/Architecture.md` MUST contain Mermaid diagrams for:
  - system or module boundaries
  - interfaces or contracts between boundaries
  - key classes or types for the changed area
- Keep one canonical source for each important fact. Link instead of duplicating.
- Public bootstrap templates are limited to root-level agent files. Authoring scaffolds for architecture, features, ADRs, and other workflows live in skills.
- Update feature docs when behaviour changes.
- Update ADRs when architecture, boundaries, or standards change.
- Every non-trivial feature doc MUST satisfy `MCAF-REQ-001`: stable `REQ-*` and `AC-*`, explicit ADR decision, multi-agent execution contract when applicable, and requirement-to-test evidence traceability.
- Every architecture-affecting ADR MUST include its implementation contract and MUST remain `Accepted` until implementation and verification are complete.
- For non-trivial work, the owning feature specification and required ADR MUST document the testing methodology:
  - what flows are covered
  - how they are tested
  - which commands prove them
  - what quality and coverage requirements must hold
- Every feature doc under `docs/Features/` MUST contain at least one Mermaid diagram for the main behaviour or flow.
- Every ADR under `docs/ADR/` MUST contain at least one Mermaid diagram for the decision, boundaries, or interactions.
- Mermaid diagrams are mandatory in architecture docs, feature docs, and ADRs.
- Mermaid diagrams must render. Simplify them until they do.

### Testing
- TDD is the default for new behaviour and bug fixes: write the failing test first, make it pass, then refactor.
- Bug fixes start with a failing regression test that reproduces the issue.
- Tests MUST be written from acceptance criteria, not from implementation details.
- Each `AC-*` criterion in the owning feature specification MUST be covered by one or more automated tests or by an explicit written exception.
- Test names, display names, or comments should reference the relevant `AC-*` ID when that improves traceability.
- Every behaviour change needs new or updated automated tests with meaningful assertions. New tests are mandatory for new behaviour and bug fixes.
- Owner correction 2026-10-05 requires every test to contain a complete real operation or caller-visible workflow: arrange actual preconditions, execute the operation through its real contract, and verify its outcome plus the resulting state or fields changed during that operation. Getter/setter checks, property-only assertions and implementation-mirroring tests do not satisfy this rule or acceptance; a negative flow must execute the rejected operation and verify its error and preserved state.
- Tests should be as realistic as possible and exercise the system through real flows, contracts, and dependencies.
- Tests must cover positive flows, negative flows, edge cases, and unexpected paths from multiple relevant angles when the behaviour can fail in different ways.
- Prefer integration/API/UI tests over isolated unit tests when behaviour crosses boundaries.
- Integration tests are the default primary proof for feature-slice behaviour that spans multiple components.
- Do not use mocks, fakes, stubs, or service doubles in verification.
- Exercise internal and external dependencies through real containers, test instances, or sandbox environments that match the real contract.
- Flaky tests are failures. Fix the cause.
- Changed production code MUST reach at least 80% line coverage, and at least 70% branch coverage where branch coverage is available.
- Critical flows and public contracts MUST reach at least 90% line coverage with explicit success and failure assertions.
- Repository or module coverage must not decrease without an explicit written exception. Coverage after the change must stay at least at the previous baseline or improve.
- Coverage is for finding gaps, not gaming a number. Coverage numbers do not replace scenario coverage or user-flow verification.
- Owner direction 2026-10-05 requires reviewing actual uncovered code when closing each module and adding meaningful complete-flow regressions for the missing success, failure and edge behaviours. Keep load, stress, performance and comparison tests out of the coverage evidence, preserve an explicit contributor inventory, and never add trivial tests solely to raise a percentage.
- The task is not done until the full relevant test suite is green, not only the newly added tests.
- If the stack is `.NET`, document the active framework and runner model explicitly so agents do not mix VSTest and Microsoft.Testing.Platform assumptions.
- If the stack is `.NET`, after changing production code run the repo-defined quality pass: format, build, analyze, focused tests, broader tests, complexity, coverage, and any configured extra gates such as architecture, security, or mutation checks.

### Code and Design
- Owner direction 2026-10-06 requires `TimeProvider` for all current-time reads, elapsed-time measurements, deadlines, delays and timers in KeyLoad-owned code. Inject or explicitly pass the owning provider; select `TimeProvider.System` only at composition/default boundaries, never inside an operation that already has an owning clock. Replace direct `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow`, `DateTime.Today`, `Stopwatch` and equivalent clock access. Tests may use a controlled `TimeProvider` to advance time deterministically; this explicitly authorizes a clock test double without replacing database, transport or storage dependencies. Preserve UTC contracts, monotonic elapsed-time checks, cancellation, persisted timestamps and RF3 authority.
- Owner clarification 2026-10-06 requires the general runtime-literal prohibition, including endpoint routes, function/method names and protocol tokens: use named constants or `nameof` when the actual symbol is available. Migrate existing runtime literals as part of enforcement rather than limiting the rule to selected API arguments.
- Owner direction 2026-10-06 requires application configuration through typed `IOptions<T>` (or native `IOptionsMonitor<T>` / `IOptionsSnapshot<T>` where their lifetime is required). Bind and validate settings at composition boundaries; feature execution must not read raw `IConfiguration` or environment variables. This is mandatory configuration ownership, not optional style.
- Owner correction 2026-10-06 requires centrally registered and validated typed options for operational timeouts, deadlines, retry policies, capacities and resource/admission limits. Moving such parameters to arbitrary per-class `const` fields or static readonly defaults is still hardcoding and is prohibited. Constants remain appropriate for immutable protocol/format identities and mathematical structure; configurable policy must flow from `IOptions<T>` to its execution owner.
- Owner direction 2026-10-05 authorizes adding semantic rules to the owned `KeyLoad.Analyzers` project for confirmed recurring code-quality problems without repeated permission. Enforce named constants for magic strings and numeric policy values, alongside typed synchronization; preserve compiler-valid regression tests, exact diagnostic locations, generated-code boundaries and all existing quality gates. Do not replace semantic analysis with blanket bans on legitimate `object` payloads or mechanically rename literals without their domain meaning.
- Owner correction 2026-10-05 prohibits `object`/`Monitor` synchronization gates. Use `System.Threading.Lock` for necessary synchronous critical sections outside grain-owned state and keep hot-path sections short; use cancellation-aware `SemaphoreSlim.WaitAsync` when mutual exclusion must span asynchronous work, with release in `finally`.
- Orleans activation-owned state MUST rely on native turn scheduling rather than redundant locks. Audit reentrancy, interleaving, escaped callbacks and node-local shared services before removing synchronization; retain real storage/apply, lifecycle and resource-ownership invariants without blocking the grain scheduler across asynchronous work.
- Everything in this solution MUST follow SOLID principles by default.
- Every class, object, module, and service MUST have a clear single responsibility and explicit boundaries.
- SOLID is mandatory.
- SRP and strong cohesion are mandatory for files, types, and functions.
- Vertical-slice architecture and the single-repository boundary are mandatory under `MCAF-ARCH-001`.
- Each feature MUST live in one consistently named repository-wide slice with all feature-owned backend, frontend, contracts, tests, and supporting artifacts colocated or mirrored under the documented slice convention and its durable doc mapped by the same name under `docs/Features/`.
- Local rules and ADRs MUST NOT weaken `MCAF-ARCH-001`; they may only document stricter rules or a time-bounded migration to compliance.
- Prefer composition over inheritance unless inheritance is explicitly justified.
- Do not preserve obsolete, dead, duplicate, or replaced legacy code unless the user explicitly asks for a temporary compatibility path.
- Owner correction 2026-10-06 removes backward compatibility with unreleased development storage formats from the first-release scope. Implement and qualify one current native storage format; remove old-format readers, migration/probe executables, historical-format test preparation and their active acceptance gates instead of developing compatibility for an unfinished product. This explicitly supersedes earlier requirements to migrate native5/native6/native7 development epochs before the first release. Preserve current-format process recovery, backup/restore, RF3 correctness and strict rejection of unsupported or corrupt formats; a later released-format upgrade requires a separate owner-approved contract.
- When replacing an old implementation, remove the old code, tests, configuration, docs, and routing in the same change once the new path is proven.
- Do not leave compatibility shims, placeholder implementations, or fallback paths as a substitute for a complete migration.
- If a temporary transition path is unavoidable, document the reason, owner, scope, verification, and removal plan in the nearest ADR, feature doc, or local `AGENTS.md`.
- Large files, types, functions, and deep nesting are design smells. Split them or document a justified exception under `exception_policy`.
- Hardcoded values are forbidden.
- String literals are forbidden in implementation code. Declare them once as named constants, enums, configuration entries, or dedicated value objects, then reuse those symbols.
- Avoid magic literals. Extract shared values into constants, enums, configuration, or dedicated types.
- Design boundaries so real behaviour can be tested through public interfaces.
- If the stack is `.NET`, the repo-root `.editorconfig` is the source of truth for formatting, naming, style, and analyzer severity. Use nested `.editorconfig` files when they serve a clear subtree-specific purpose. Do not let IDE defaults, pipeline flags, and repo config disagree.

### Critical
- Never commit secrets, keys, or connection strings.
- When the owner requests GitHub delivery, checkpoint the complete authorized KeyLoad code, tests, and durable docs at each coherent verified milestone so work is not left as a large uncommitted tree; preserve the existing main protections and never bypass a failing required gate.
- Never skip tests to make a branch green.
- Never weaken a test or analyzer without explicit justification.
- Never introduce mocks, fakes, stubs, or service doubles to hide real behaviour in tests or local flows.
- Never keep legacy, obsolete, dead, duplicate, shim, placeholder, or fallback code unless an explicit documented exception requires it.
- Never introduce a non-SOLID design unless the exception is explicitly documented under `exception_policy`.
- Never spread one feature across unrelated folders when a vertical slice can keep it isolated.
- Never move solution-owned backend, frontend, tests, infrastructure, or documentation into a separate repository.
- Never use different names or different internal conventions for the same slice across technical roots.
- Never overwrite an existing root or local `AGENTS.md` with a downloaded template or a shortened reconstruction.
- Never delete, omit, or weaken an existing `AGENTS.md` rule during framework installation or update.
- Never force-push to `main`.
- Never approve or merge on behalf of a human maintainer.

### Boundaries

Always:
- Read root and local `AGENTS.md` files before editing code.
- Read the relevant docs before changing behaviour or architecture.
- Run the required verification commands yourself.

For changes outside existing owner authorization, obtain direction before changing public API contracts, adding dependencies, modifying database schema or deleting code files. This MUST NOT revoke the existing scoped ManagedCode repair/release authorization, stable KeyLoad main commit/push authorization or already authorized product work. Complete the concrete reviewable work within authorized scope.

## Preferences

### Likes
- Owner correction 2026-10-04 requires an actual Aspire/AppHost or required topology startup failure to terminate its owned test/workload promptly, retain the original failure and diagnostics, and clean up owned resources instead of waiting for the full workload deadline. Dashboard-disabled warnings alone are not topology failures; planned fault injection and independent database jobs retain their explicit contracts.
- Owner correction 2026-10-04 requires bounded live benchmark phase and attempt progress in GitHub Actions so a long workload cannot appear idle for tens of minutes. Retain cancellation diagnostics before teardown; keep original failures, required workload sizes, native topology and qualification gates intact.
- Owner correction 2026-10-04 requires Benchmarks to finish every independent database workload and publish authenticated successful measurements even when another benchmark, including unfinished KeyLoad, fails. Record each failed workload as unavailable with no numeric data and its actual failure/job provenance; never invent results or label a failed job successful. This explicitly supersedes the all-success comparison-cohort publication restriction while preserving complete planned-cell accounting, source/run/attempt identity, native topology, fairness, site qualification and least-privilege deployment. Benchmark repair does not authorize taking over concurrent KeyLoad engine implementation.
- Owner correction 2026-10-04 separates database measurements from website testing: Benchmarks owns native database preparation, independent workloads, result validation and authenticated JSON aggregation. Chrome, browser/site tests and website qualification MUST NOT be benchmark dependencies. A separate website-build action may run after the JSON is ready or be triggered through CI; the owner explicitly permits static site building at the end of Benchmarks. The selected implementation uses independent CI website jobs on own-main push/manual and completed Benchmarks events. Every website build MUST use the newest completed own-main Benchmarks run and its successful authenticated aggregate, across push/manual producers, rather than blindly using the triggering run or falling back after invalid latest evidence. Preserve exactly three workflows, failed/null cells, original provenance, website gates and predeploy freshness. Website failure cannot block database metrics production. This supersedes earlier integrated qualification placement and the interim static-build prohibition. KeyLoad engine implementation remains owned by the other agent.
- Complete the coherent task's implementation before executing its runtime tests or benchmarks. Author the mapped tests with the code, then run the required validation after the implementation and self-review; do not let repeated intermediate verification replace implementation progress. This owner correction2026-10-03 supersedes test-first execution for the current work while preserving every final correctness, CI, fault and performance gate.
- Owner reiteration 2026-10-04 requires concurrent feature implementation with three to five capable Luna coding agents when slots permit. Keep independent pending product tasks moving while the lead integrates and verifies completed stages; a long test run or unfinished qualification of one feature MUST NOT block implementation of unrelated features. Use disjoint write scopes and stable compiled test snapshots, retain every acceptance gate, and close each task as soon as its own complete acceptance evidence exists rather than waiting for the entire plan.
- Each agent MUST follow the execution plan for its own user-assigned task and complete only that task; change its scope only when the owner explicitly redirects it (owner correction 2026-10-03).
- Other chats' plans, progress, requests and concurrent changes MUST NOT expand an agent's task or redirect its implementation. Preserve their work and leave their tasks to their owners (owner correction 2026-10-03).
- Repairs and verification MUST stay within the assigned task. Report an unrelated failure to the owner without taking over its implementation; direct user instructions remain authoritative (owner correction 2026-10-03).
- Frequent concise Ukrainian progress stating completed work, remaining work and real blockers.
- The root README MUST introduce KeyLoad as a product: its purpose, connected data models, agent use cases, getting started, and direct credits to the projects it actually uses. Keep dependency versions in their canonical manifests and link to detailed technical documentation (owner correction 2026-10-03). The README MUST read as an engaging, plain-language product page that a newcomer understands on first read and can act on: a clear problem/pitch, how it works, a runnable quick start, real SDK/SQL/MCP examples, an honest available-versus-in-progress status and a repository map for contributors. Internal specification jargon belongs in the linked docs, not the README. Every rewrite MUST retain all existing credits and add any actually used project that is missing (repeated owner correction 2026-10-04).
- Shared-checkout work that preserves unrelated changes and completes authorized delivery.
- Real Docker/Aspire multi-node comparisons using the same data, oracle and workload contract.
- All published test results and chart values MUST come from JSON produced by successful GitHub Actions runs. Include raw GitHub JSON links, measured source SHA, options, topology and acknowledgement/read guarantees. Missing or unsupported measurements MUST remain unavailable; never invent winners, zeros or sample performance values.
- Website publication MUST be the downstream qualification/deployment stages of Benchmarks, consuming the exact same run/attempt/source's complete successful comparison JSON and verifying its job/artifact provenance. Performance figures MUST be read from that evidence; never hardcode them in website or README charts. A failed, skipped, incomplete, expired or unauthenticated comparison cannot refresh the published evidence (explicit owner layout correction 2026-10-03 supersedes the separate-workflow placement).
- Benchmarks MUST execute all load/performance/comparison suites and aggregate their data before website qualification and deployment. Main/site source changes and manual dispatch use this pipeline; preserve all existing site tests, coverage, browser, freshness and least-privilege publication gates. This owner-authorized workflow integration does not authorize unrelated database implementation or DNS changes (owner correction 2026-10-03).
- Owner direction on 2026-10-02 resolves the earlier website-source and producer-success policy conflict for REQ-BC-028: qualify the current trusted-main website separately from the measured-source checkout, and require the actual successful comparison job and its measurement steps. Unrelated database jobs or whole-workflow failure MUST NOT block this website-only delivery. Preserve accurate site/measured/control revisions and all website qualification gates. Earlier successful-run and equal-source guidance remains recorded as the superseded task boundary; never label a failed whole run successful.
- Free community features only for comparison engines. No Enterprise images, license activation or paid cluster features.
- KeyLoad's visual style is serious, adult Apple-style Liquid Glass in the Managed Code family: editorial off-white surfaces, bold black typography and Managed Code's pastel iridescent accent (peach → lilac → periwinkle), plus Prostir graphite. Do not use green or lime as the brand accent, and do not return to the rejected light-blue look. No Material Design, colour blobs, rainbow logos or playful decoration. The `/admin` console and the public site share it (owner direction 2026-10-02).
- The public landing must be a radically better product page for KeyLoad as the database for AI agents, on .NET and Orleans. Its Three.js scene must be live and moving as soon as it loads, except under reduced-motion preferences, within the scene lifecycle, budget and honesty contracts.
- Every public KeyLoad page ends with "Developed by Managed Code" and a normal followed (dofollow) link to https://www.managed-code.com/.
- The central KeyLoad K MUST use the canonical true SVG outside the bounded 3D raster buffer so it stays sharp on Retina displays. An SVG wrapper around a bitmap or a CanvasTexture logo does not satisfy this requirement (repeated owner correction 2026-10-03).
### Dislikes
- Owner reiteration 2026-10-03 requires immediate cleanup: delete obsolete, unused, superseded, duplicate and abandoned code, legacy implementations, temporary artifacts and discarded implementation/evaluation plans in the same coherent change. Remove their obsolete tests, configuration, dependencies, routes and documentation references together; do not retain dead branches, compatibility shims or fallback implementations for an unspecified later cleanup. Preserve required behavior with the real replacement and its mapped acceptance tests, unrelated active work and persisted user data. Existing historical migration-debt records do not authorize retaining a replaced implementation.
- Repeated permission questions for already authorized work.
- Working `*.plan.md`, `*.brainstorm.md` and `*.acceptance.md` files MUST NOT be created or committed anywhere in this checkout. Remove existing copies and ignore these patterns without exceptions. Keep durable requirements, acceptance criteria and execution/verification contracts in the owning `docs/Features/` specification and required ADR; agent working notes stay outside the repository. This explicit owner correction on 2026-10-03 supersedes every root/local instruction requiring separate planning files without weakening feature, test, architecture or qualification requirements.
- The root README MUST NOT accumulate CI run histories, test counts, internal implementation receipts, governance procedures, dependency-maintenance instructions, generic dependency-license boilerplate, or package/version dumps. Credits MUST name the projects actually used and explain their contribution. Keep a concise honest development-status statement and link to the canonical status and qualification records instead (repeated owner correction 2026-10-03).
- GitHub Actions MUST expose exactly three plainly named workflows: CI combines build and ordinary project tests for PR/push/manual checks; Benchmarks runs all load/comparison suites, produces complete authenticated measurements and then qualifies/publishes the website; Release builds the solution, real database images/distribution and packages, then creates a GitHub Release and immutable version tag. Do not split Tests or Website into additional workflows (explicit owner correction 2026-10-03 supersedes the earlier five-workflow layout).
- Release tags MUST be `v<major>.<minor>.<yyMMdd>.<daily-build>`: major/minor are configured in source, the third component is the UTC date and the fourth starts at 1 each day and increases without tag reuse or overwrite. Build/package/image/manifest identities MUST agree; reruns recover their original reservation. Release automation is authorized to create the corresponding Git tag, GitHub Release and versioned database image/package assets; preserve protections and qualification gates (owner direction 2026-10-03).
- KeyLoad product release packaging and publication are deferred until the owner explicitly requests a release or confirms product readiness. Keep Release prepared and manual; do not dispatch package/image builds, create release tags or publish releases merely to prove this workflow while the product is unfinished (owner correction 2026-10-03).
- Repository-rule checks MUST always run inside the standard CI pipeline, never as a separate governance workflow (owner correction 2026-10-03).
- All comparative performance tests, including TimeSeries image checks, MUST run in one separate Benchmarks pipeline; do not name it Comparisons or create feature-specific comparison workflows. Preserve isolated Linux runners, real native topology, complete workloads and authenticated artifacts (owner clarification 2026-10-03).
- `benchmarks.yml` MUST contain only end-to-end database comparisons of KeyLoad against other databases, their required preparation/validation, aggregation and website publication. Internal code, serialization, raw storage/cache engine microbenchmarks, phase profiling and KeyLoad-only optimization measurements MUST NOT appear as jobs, dispatch modes or dependencies in that workflow. Run those measurements locally or in a separately authorized internal benchmark context; ordinary correctness tests belong in CI. This owner correction on 2026-10-03 supersedes any earlier instruction that placed internal performance work in the database-comparison pipeline, while preserving the complete database workload suite and public GitHub provenance requirements.
- TimeSeries MUST be one workload among the complete shared performance suite, never a separate feature-specific branch in the Benchmarks job graph. Qualify its pinned image in the common native-image preparation and retain all other workloads, isolated measurement runners and complete aggregation before publication (repeated owner correction 2026-10-03).
- Every GitHub Actions job and authored step MUST have a short explicit human-readable name describing its actual action. Name checkout, SDK setup, restore, build, tests and artifact uploads; avoid exposed machine IDs, raw commands and internal qualification/provenance jargon. Update source-bound name validators together when displayed names change (owner correction 2026-10-03).
- Independent benchmark preparation and measurement jobs MUST run in parallel. Do not chain preflight, CRUD and specialized workload matrices or impose an arbitrary workflow max-parallel cap. Retain only actual input dependencies, isolated runners and bounded native operation resources; complete aggregation waits for every preparation/check/measurement gate before website qualification and publication (owner correction 2026-10-03).
- Fake production readiness, power-loss claims from process-kill tests, or unsupported performance supremacy.
- Installing skills when the owner explicitly prohibited it.
- For bug-fix and optimization requests, do not spend turns on plans/status documentation without promptly making concrete source-level repairs once the scope is clear.
- Recolouring tokens or restyling the old layout when the owner asks for a redesign. A landing or `/admin` redesign means a complete new layout, information architecture, visual concept and interaction patterns, delivered for both surfaces (repeated owner correction 2026-10-02).
- Visible disclaimer chrome on the landing or `/admin`, such as "Conceptual illustration · not live data" pills, captions or poster micro-text, or visible scene status pills. The hero illustration is self-evidently illustrative; keep that honesty in alt/aria/live-region text only. Real evidence provenance labels on measured data remain required (owner correction 2026-10-03).
- Internal process jargon such as "Evidence" as a visible navigation, button, heading or column label on public surfaces. Use plain product words a visitor expects ("Benchmarks", "Methodology", "GitHub run"); provenance links stay, but are named for what they open (owner correction 2026-10-03).

## Current additive project inventory

The original 22-project rule above remains mandatory and preserved. The current inventory additionally contains `tests/KeyLoad.SiteTests` (ADR-040), bringing the project count to 23. Its local AGENTS.md was created before implementation; every original and new project/module remains covered by the same mandatory policy. See `docs/implementation/mcaf-installation.json` for the exact current paths and the preserved installation baseline.

ADR-043 additionally introduces `benchmarks/KeyLoad.ComparisonHost`, bringing the
current project count to 24. Its local policy preceded implementation; the existing
KeyLoad.Comparisons public library and every prior mandatory policy remain covered.

ADR-047 additionally introduces `benchmarks/KeyLoad.BenchmarkScenarios`, bringing the shared workspace count to25. Its local policy preceded implementation. Every existing root/local rule remains mandatory and unchanged.

ADR-063 additionally introduces `src/KeyLoad.Diagnostics`, bringing the shared workspace count to26. Its local policy precedes implementation. The BCL-only ResourceExecution project owns fixed callback-free phase diagnostics; every prior root/local policy, preserved installation prefix and native qualification gate remains mandatory.

ADR-077 additionally introduces `src/KeyLoad.Storage.IO`, bringing the shared workspace count to27. Its local policy precedes implementation. The internal StorageRecovery primitive uses public OS interfaces to reject non-regular migration inputs without blocking and preserves existing owner-lock interoperability. It owns no engine, database state or replication; all prior policies, immutable formats and qualification gates remain mandatory.

A bounded website qualification candidate contains the20-project historical runtime base plus KeyLoad.Analyzers, KeyLoad.Analyzers.Tests and KeyLoad.SiteTests (23projects). Its derived inventory MUST record the shared25-project scope and omitted concurrent projects explicitly, preserve all28 included root/local policy blobs exactly, and qualify only the actual included website/analyzer source. This is an evidence-scope record, never an exception to any solution-wide architecture, project-policy or required product qualification rule.

## Latest completed result eligibility, 2026-10-04
- Latest benchmark metrics means the newest completed own-main push/manual Benchmarks producer with success/failure conclusion. Pending, skipped and canceled workflows have no completed comparison cohort and MUST NOT displace ready JSON. Exclude them before choosing the latest eligible producer; then reject its missing/corrupt/failed aggregate without older fallback. A canceled workflow_run event still cannot authorize publication. This refines the latest-result rule without accepting incomplete or fabricated measurements.

## Independent website queue, 2026-10-04
- CI website generation MUST NOT wait for unrelated ordinary CI/RF3 execution from another run. Push/manual CI runs use their own run identity; PR keeps its existing ref-based cancellation. Website qualification and deployment use separate bounded job concurrency groups with cancel-in-progress=false, preserving source/latest-evidence freshness and all gates. This implements the owner-authorized independent website action without canceling database or test work.

## Independent website publication, owner correction 2026-10-06
- Website source changes MUST independently build, qualify and publish the website in CI without waiting for a benchmark run. Use the newest completed own-main Benchmarks producer whose authenticated aggregate is ready when one exists; a run with no successful aggregate is not available website data. If no ready producer exists, publish the qualified product website with no benchmark figures or synthetic measurement artifacts. New ready benchmark JSON MUST automatically trigger a fresh website build/publication. This rule-specific correction supersedes mandatory benchmark availability and newest-failed-producer blocking in the earlier publication rules; corrupt/expired/unauthenticated evidence, provider errors, website test failures and stale source still fail closed. Preserve exactly three workflows, independent CI website jobs, original metric provenance and full metric/archive/browser/coverage gates whenever metrics are included. The no-metrics route has its own complete asset/browser/source/coverage qualification; measurement-only tests and archive gates are N/A to an artifact which includes no measurements, never reported as passed or replaced with fabricated inputs.

## Behavioural test ownership, owner correction 2026-10-06

- Tests MUST NOT establish behaviour by reading repository source and asserting that particular words, calls, attributes or implementation shapes are present. Execute the real operation and assert its observable result and resulting state instead; remove obsolete source-text checks and their unused helpers. Compiler/analyzer tests must execute the real compiler/analyzer and assert emitted diagnostics, rather than scan source text. Static repository governance remains a CI check, not functional product-test evidence.

## Separate Website workflow, owner correction 2026-10-06

- GitHub Actions MUST expose four plainly named workflows: `Build and Tests` builds the complete solution and runs repository checks and ordinary tests; `Benchmarks` produces authenticated database-comparison JSON; `Website` independently builds, qualifies and deploys the site; `Release` remains the prepared manual product-release workflow. Website jobs and Benchmarks completion triggers MUST NOT be part of Build and Tests. This explicit correction supersedes the earlier three-workflow limit, the CI display name and every placement of website publication inside CI or Benchmarks.
- Website MUST publish after trusted main source changes or manual dispatch independently of Build and Tests and benchmark success. Consume the newest ready authenticated Benchmarks aggregate when available; otherwise qualify and publish the site without benchmark figures. New ready benchmark data triggers a fresh Website run. Preserve source freshness, original metric provenance, complete applicable website qualification and least-privilege Pages deployment; absence of measurements is not a website failure.

## Final Benchmarks Website trigger, owner clarification 2026-10-06

- Benchmarks MUST finish with only a bounded dispatch of the separate Website workflow; it MUST NOT build, test or deploy the site itself. This supersedes Website's `workflow_run` completion subscription. Confine dispatch permission to that final trigger job, keep main/manual Website publication independent, and authenticate any supplied producer run through GitHub before bounded completion waiting and newest-ready selection. A trigger input is not metric provenance or permission to bypass website qualification.

## Super rule: owner-only migration and legacy authorization

- Owner correction 2026-10-06: KeyLoad is a new product. Do not implement or retain data, schema, storage-format, persisted-record, configuration or prior-KeyLoad-version migrations, legacy readers, compatibility shims, dual paths or fallbacks unless the owner directly requests that specific migration or legacy path.
- This prohibition is the highest project rule for migration/legacy scope and explicitly supersedes every earlier plan, ADR, feature requirement, acceptance gate, local policy and inferred authorization to maintain unreleased or previous KeyLoad implementations. A generic instruction to finish the product, an agent proposal or a recorded migration contract is not the owner's direct permission.
- Delete existing KeyLoad-owned migration and legacy implementations now, together with their exclusive tests, helpers, prior-binary/image preparation, configuration, routes, dependencies and active documentation/plan references. Do not preserve them as dead branches or deferred cleanup and do not replace them with a new migration mechanism.
- Implement and qualify the current product contract. Preserve current-format crash recovery, verified backup/restore, persisted authorization, strict unsupported/corrupt-version rejection and genuine Aspire RF3. Native Orleans activation movement and SQL interoperability with external clients are current product capabilities, not authorization for legacy KeyLoad code or data conversion.
