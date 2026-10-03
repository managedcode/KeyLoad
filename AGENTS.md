# KeyLoad

Implement the architecture in `docs/design/architecture-v0.3.uk.md`. Treat it as the product specification; user instructions control scope and authorization.

Work in this checkout. Preserve unrelated changes. The first server topology is an RF3 cluster; do not replace it with an in-memory or single-node demo. Keep the atomic partition distinct from physical placement. A node-local PartitionHost owns storage, journals, file locks and the apply gate; Orleans grains route commands.

Orleans is the foundation of the database and its cluster. Do not use DotNext.AspNetCore.Cluster or DotNext.Net.Cluster. Enable the distributed grain directory and activation migration/repartitioning. Each request uses a separate Orleans grain which invokes the required database grains. Keep storage ownership in node-local hosts when routing activations migrate.

Use TUnit for all tests. Run the RF3 database in Docker through Aspire and exercise its public operations through the real .NET SDK client and official MCP C# SDK client. Database credentials and authorization policies are persisted in the database; clients cannot supply trusted roles. Provide chunked blob storage and partial reads, a simple agent API, and an integrated official C# SDK MCP server for all database operations, search and storage. Keep frequent progress updates in Ukrainian stating what is ready and what remains.

Use .NET 10 and centrally pinned packages. Do not generate or commit packages.lock.json; ignore build outputs, local data and test artifacts. Required checks: build the solution, TUnit unit tests, real process recovery tests, and Docker/Aspire RF3 tests through the .NET and MCP clients. Keep docs/implementation/status.json and the README honest about qualification gates. Do not claim power-loss durability from process-kill tests or production readiness without the endurance and fault gates.

ManagedCode packages are our projects. Fix dependency defects in their owning sibling repository, with regression tests, canonical patch release, successful GitHub publication and verified NuGet availability before updating KeyLoad. Preserve unrelated work; never force-push or bypass repository protections. The user authorizes scoped dependency repair commits, pushes and releases, and stable KeyLoad commits and pushes to main.

## Git workflow
- Never run `git stash` or use automatic stashing through another tool. Keep working-tree changes visible instead of hiding them in a stash.
- When the user requests committing all current changes, commit the full requested working-tree scope on the currently checked-out branch; do not silently omit existing changes or move them to another branch.

## System-critical SMID and operation efficiency
- KeyLoad is one database for AI agents spanning documents, relational tables, graphs, vectors/search, files/blobs, queues and events. SQL MUST be the central versioned language across those capabilities, compiled to the same authorized bounded database operations as SDK/MCP callers; a scalar document-only dialect is an initial stage, not the completed product (owner direction 2026-10-02).
- The owner clarified on 2026-10-02 that all models run in one server and may be linked to each other; this means unified database/model composition, not a new single-physical-file storage format. Preserve canonical entity references so relational rows can participate in graphs, vectors, events and queued workflows.
- Treat relational integrity and unified SQL as first-class product workstreams alongside SIMD and operation efficiency. Specify typed rows, keys/constraints, joins, atomic boundaries, unsupported diagnostics and measured performance before advertising capability; preserve RF3 and node-local storage ownership. A unified database/file delivery requirement MUST NOT be interpreted as permission to discard replication journals or promise a single physical file without a qualified storage-format ADR.
- Treat the owner's SIMD work (earlier written as SMID) as a first-priority product workstream and keep it visible in implementation planning and status. On 2026-10-02 the owner confirmed SIMD means vectorized CPU operations and directed .NET intrinsics first, with Rust considered only after profiling. Map its canonical slices, REQ/AC criteria, scalar correctness/portability contract and required ADRs before implementation; never claim acceleration without comparable GitHub measurements.
- Orleans provides request isolation, cluster routing and activation movement. Keep the one-grain-per-request boundary and RF3 topology; treat Orleans Streams as a first-class architecture workstream to specify and qualify alongside persisted KeyLoad EventStreams. Keep their distinct delivery/restart contracts explicit, and define bounded resource, concurrency, backpressure, recovery and performance behavior for every operation.
- Optimize each operation only against its correctness and fault contracts, using representative multi-node GitHub qualification to measure latency, throughput, allocations, memory, contention and backlog where applicable. Architecture choices or local builds alone do not prove maximum scalability or performance.
- Use ZoneTree's native storage APIs correctly and Orleans for bounded parallel execution of independent operation work. Preserve node-local storage ownership, the ordered atomic commit/apply gate, scoped read cuts, cancellation and backpressure; qualify the resulting performance in real multi-node GitHub runs (owner direction 2026-10-02).
- All KeyLoad-owned database models MUST use ZoneTree as their canonical storage engine, including documents, relational rows, graphs, vectors/search, time series, blobs, queues and events. Use and qualify ZoneTree's native WAL in the storage durability path; retain the distinct replication and atomic-commit recovery journals until an explicit storage-format ADR and fault qualification prove any migration safe. This requirement applies to KeyLoad models, while comparison engines retain their real native storage (owner direction 2026-10-02).
- Use native generated Orleans binary serialization with stable `[GenerateSerializer]`, `[Id]` and `[Alias]` contracts for all KeyLoad-owned internal typed persistence and inter-grain/replica payloads, including atomic WAL mutations over ZoneTree. Preserve raw-byte native ZoneTree serialization, sortable keys, public HTTP/MCP JSON, exact user document content, frozen canonical identity digests, synchronous durability barriers, checksums, ordered atomic recovery and RF3 authority. Format changes require an explicit upgrade contract; do not retain runtime JSON fallback or claim speed/fault improvements without actual GitHub evidence (owner clarification 2026-10-03).
- Orleans distribution and Orleans-coordinated caches are mandatory product workstreams. Define bounded memory, admission, eviction, concurrency and backpressure, with cache identity and validation tied to authorized scoped read cuts and policy epochs. Specify invalidation after writes and authorization changes, and recovery after grain migration, failover and restart before implementation; cached state MUST NOT replace persisted authorization, committed ZoneTree state or RF3 durability guarantees (owner direction 2026-10-02).
- The owner confirmed on 2026-10-02 that memory means the KeyLoad RAM layer and Orleans caches. Deliver a bounded node-local RAM acceleration layer over committed ZoneTree data and its native WAL, coordinated through Orleans; RAM contents are disposable across restart or activation movement and MUST NOT become the authority for acknowledged writes, authorization or RF3 recovery.
- Model logical shards and cache coordination as Orleans grains behind the separate grain-per-request boundary. Shard-grain identity MUST remain distinct from physical replica placement; activation movement routes to node-local ZoneTree/WAL owners rather than moving open storage handles (owner clarification 2026-10-02).
- Treat ManagedCode.TimeSeries arithmetic, allocations, SIMD and batch processing as an owning-repository performance workstream. Select improvements from source review and profiling, publish and verify its canonical release before consuming it in KeyLoad, and retain numerical and compatibility regressions.
- Performance qualification MUST compare both single-node and native multi-node configurations against the complete competitor matrix with equivalent workload and acknowledgement/durability contracts. Retain exact-source measurements and regression evidence; the goal of leading competitors MUST NOT be reported as achieved without those results (owner direction 2026-10-02).
- Qualification MUST use Linux runners only; the owner explicitly replaced the three-OS qualification matrix on 2026-10-03. Preserve every required suite, scalar portability check, recovery and RF3 gate while removing redundant macOS/Windows execution.
- Each comparison database MUST run the same complete workload suite in its own isolated GitHub Actions runner/job agent, with only that database's native topology and load generator. KeyLoad RF3 and competitor clusters remain genuine clusters; different databases MUST NOT share a runner, process, containers, volumes or measurement session (owner direction 2026-10-03).
- Each isolated comparison agent MUST retain its own source/run/attempt/target/topology/profile-bound JSON. After all target agents finish, one aggregation job MUST validate completeness, comparable settings and provenance, collect their results and generate the website metrics from those JSON files. Failed, missing, skipped or mixed-cohort measurements MUST NOT refresh published performance evidence (owner direction 2026-10-03).
- The comparison matrix MUST include actual native one-node, two-node and three-node configurations and intensive read/create/update/delete workloads. Each engine/node-count/scenario measurement MUST run on a separate isolated runner agent; record real membership, acknowledgement, correctness, latency, throughput and resource use. Do not relabel client counts or independent standalone databases as cluster node counts. Unsupported native/community topology remains explicitly unavailable, and benchmark topology changes require a fault/consistency ADR before implementation; the initial production RF3 requirement remains mandatory (owner direction 2026-10-03).

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
The explicit owner instruction to enable Orleans distributed directory and activation repartitioning is consent to those two native experimental APIs. Confine compiler opt-in ORLEANSEXP003/ORLEANSEXP001 to their two configuration calls, with ADR-034 evidence; it does not authorize global NoWarn, suppression of quality diagnostics or changing analyzer severity.

- On 2026-10-01 the owner explicitly authorized installing the Orleans skill through the `dotnet skills` command. This rule-specific permission applies to the requested Orleans skill; other skill installation remains prohibited unless separately authorized. Read and apply its installed SKILL.md before continuing Orleans implementation.
- Orleans 3.1.1 is installed globally at `/Users/ksemenenko/.codex/skills/orleans/SKILL.md`, from Managed Code catalog `2026.10.1.0` using `dotnet-skills` 0.1.242. It is applied to Orleans design, lifecycle, transport, routing and failure verification. This is the specifically authorized addition; the historical bootstrap installed no skills.
- No MCAF or .NET skills are installed for this bootstrap. The owner explicitly instructed: do not install skills. Do not create skill directories, install tools or modify global agent configuration for this task.
- Catalog links are informational: MCAF `https://mcaf.managed-code.com/skills`; .NET `https://skills.managed-code.com/`. Installed repository skill catalog: empty.
- If the owner later explicitly authorizes skill installation, source .NET skills from the Managed Code Skills catalog, use `mcaf-dotnet` as the routing entry, keep only current `mcaf-*` skills and exactly the TUnit framework skill `mcaf-dotnet-tunit`. Tool-specific skills MUST match tools actually used. Recheck build/test/format/analyze/complexity/coverage commands after an authorized upgrade.
- Local AGENTS.md MUST state applicable skill status honestly; a named catalog entry is not an installed skill.

## Rules to Follow (Mandatory)

### Commands
- Qualification and test execution MUST occur in GitHub Actions. Do not run local tests, recovery qualifications or load benchmarks. Trigger/inspect the real workflow and preserve its exact SHA, run/job URL and artifacts. Development builds and static source/governance checks are not test qualification.
- `restore`: `dotnet restore KeyLoad.slnx` with centrally pinned versions; current root forbids generating/committing package lock files. The historical CI baseline used locked mode; the current workflow is migrating to the no-lock policy.
- `build`: `dotnet build KeyLoad.slnx --no-restore --configuration Release`; warnings-as-errors and current analyzers are configured in Directory.Build.props.
- `test`: dispatch `gh workflow run ci.yml --repo managedcode/KeyLoad --ref main`, inspect `gh run view <run-id> --repo managedcode/KeyLoad`, and download the resulting test/comparison artifacts. CI executes `dotnet test --project tests/<project> --no-build --no-restore --configuration Release` for unit, recovery, RF3 and comparison suites. No skipped suite can count as passing.
- `format`: `dotnet format KeyLoad.slnx --verify-no-changes --no-restore` is the required formatter command and CI gate; source configuration does not establish a green formatter qualification.
- `analyze`: solution Release build with TreatWarningsAsErrors=true, AnalysisLevel=latest-all, SDK/style analysis and centrally attached KeyLoad.Analyzers; compiler SARIF reports are retained under artifacts/code-quality.
- `complexity`: numeric policy limits below are mandatory and are enforced by source-owned KLD0030/KLD0031/KLD0032/KLD0033 during ordinary consumer builds; the analyzer infrastructure has a real compiler source-inventory fixture in CI. Configuration or a scoped build MUST NOT be reported as a passing full gate without the exact-SHA complete build and fixture qualification.
- `coverage`: no CI coverage collector or numeric baseline is configured yet. Configure an MTP/TUnit-compatible collector before claiming the coverage thresholds below pass; no invented coverage result is allowed.
- `governance`: `node scripts/Features/RepositoryGovernance/verify.mjs` validates the real inventory and policy-preservation record; it is static installation validation, not a runtime test result.
- Active runner: Microsoft.Testing.Platform in global.json; mandatory framework: TUnit. Existing xUnit/VSTest references are migration debt, not a permitted second framework.
- .NET target is net10.0. C# 14.0 is explicitly pinned in Directory.Build.props. Root .editorconfig is the source of truth for formatting/style/analyzer severity; nested files require a concrete subtree purpose.
- Use the Prostir root `.editorconfig` as the owner-selected baseline. Enable .NET static analyzers and build-time code-style checks for every solution project, with warnings treated as errors.
- Keep repository-owned Roslyn rules in the KeyLoad analyzer project, attach them centrally to consuming projects, and retain compiler diagnostic reports so rules can be authored and verified in code.
- Canonical CI: `.github/workflows/ci.yml`; Pages publication: `.github/workflows/pages.yml`. Skills installed by this bootstrap: none.
- Owner clarification 2026-10-03 supersedes the historical combined test dispatch above: use `gh workflow run tests.yml --repo managedcode/KeyLoad --ref main` for project tests, `benchmarks.yml` for performance qualification and `release.yml` for package builds; `ci.yml` handles PR checks and `pages.yml` handles Website. All test qualification remains GitHub-only.

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
- `function_max_loc`: `50`
- `max_nesting_depth`: `3`
- `exception_policy`: `Document any justified exception in the nearest ADR, feature doc, or local AGENTS.md with the reason, scope, and removal/refactor plan.`

Local `AGENTS.md` files may tighten these values, but they must not loosen them without an explicit root-level exception.

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
- Before starting a brainstorm, decide whether the task is actually non-trivial.
- For non-trivial work, create a root-level `<slug>.brainstorm.md` file before making code or doc changes.
- For simple, short, or obvious work, skip the brainstorm and go directly to execution.
- Use `<slug>.brainstorm.md` to capture the problem framing, options, trade-offs, risks, open questions, and the recommended direction.
- Think through the task in the brainstorm before committing to implementation details.
- Before creating `<slug>.plan.md`, create a root-level `<slug>.acceptance.md` file.
- The acceptance criteria file MUST be detailed enough that another agent or maintainer can implement and test the task without rereading the conversation.
- The acceptance criteria file MUST contain:
  - task goal and user-visible outcome
  - in-scope and out-of-scope behaviour
  - assumptions and open questions
  - actors, entry points, permissions, and affected boundaries
  - numbered criteria with stable IDs such as `AC-001`, `AC-002`, and `AC-003`
  - clear pass and fail conditions for every criterion
  - positive flows, negative flows, edge cases, and unexpected/error paths
  - data, contract, API, UI, persistence, performance, security, and observability expectations where relevant
  - migration, compatibility, and rollback expectations where relevant
  - a criterion-to-test matrix that maps each `AC-*` item to planned automated tests, test level, assertions, and verification commands
  - explicit criteria that will not receive automated coverage, with the reason and required manual or review evidence
- Do not start implementation until the acceptance criteria file exists and the test strategy maps back to it.
- After the acceptance criteria are written, create a root-level `<slug>.plan.md` file derived from `<slug>.acceptance.md`.
- Keep the `<slug>.plan.md` file as the working plan for the task until completion.
- The plan file MUST contain:
  - a link or reference to the chosen brainstorm
  - a link or reference to the acceptance criteria file
  - implementation steps derived from the accepted `AC-*` criteria
  - task goal and scope
  - a detailed implementation plan with detailed ordered steps
  - constraints and risks
  - explicit test steps as part of the ordered plan, not as a later add-on
  - a test plan derived from the `AC-*` criteria, with every criterion covered by tests or a documented exception
  - the test and verification strategy for each planned step
  - the testing methodology for the task: what flows will be tested, how they will be tested, and what quality bar the tests must meet
  - an explicit full-test baseline step after the plan is prepared
  - a tracked list of already failing tests, with one checklist item per failing test
  - root-cause notes and intended fix path for each failing test that must be addressed
  - a checklist with explicit done criteria for each step
  - ordered final validation skills and commands, with reason for each
- Use the Ralph Loop for every non-trivial task:
  - brainstorm in `<slug>.brainstorm.md` before coding or document edits
  - think through options and choose the intended direction before planning
  - turn the chosen direction into detailed acceptance criteria in `<slug>.acceptance.md`
  - map every acceptance criterion to planned automated tests or a documented test exception before coding
  - turn the chosen direction into a detailed `<slug>.plan.md`
  - include test creation, test updates, and verification work in the ordered steps from the start
  - once the initial plan is ready, run the full relevant test suite to establish the real baseline
  - if tests are already failing, add each failing test back into `<slug>.plan.md` as a tracked item with its failure symptom, suspected cause, and fix status
  - work through failing tests one by one: reproduce, find the root cause, apply the fix, rerun, and update the plan file
  - include ordered final validation skills in the plan file, with reason for each skill
  - require each selected skill to produce a concrete action, artifact, or verification outcome
  - execute one planned step at a time
  - mark checklist items in `<slug>.plan.md` as work progresses
  - update `<slug>.acceptance.md` when the criteria change, then update the mapped tests and plan before continuing
  - review findings, apply fixes, and rerun relevant verification
  - update the plan file and repeat until done criteria are met; document blockers explicitly without weakening an acceptance criterion or mandatory rule.
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
- For non-trivial work, the acceptance criteria file, plan file, feature doc, or ADR MUST document the testing methodology:
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
- Each `AC-*` criterion in `<slug>.acceptance.md` MUST be covered by one or more automated tests or by an explicit written exception.
- Test names, display names, or comments should reference the relevant `AC-*` ID when that improves traceability.
- Every behaviour change needs new or updated automated tests with meaningful assertions. New tests are mandatory for new behaviour and bug fixes.
- Tests must prove the real user flow or caller-visible system flow, not only internal implementation details.
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
- The task is not done until the full relevant test suite is green, not only the newly added tests.
- If the stack is `.NET`, document the active framework and runner model explicitly so agents do not mix VSTest and Microsoft.Testing.Platform assumptions.
- If the stack is `.NET`, after changing production code run the repo-defined quality pass: format, build, analyze, focused tests, broader tests, complexity, coverage, and any configured extra gates such as architecture, security, or mutation checks.

### Code and Design
- Everything in this solution MUST follow SOLID principles by default.
- Every class, object, module, and service MUST have a clear single responsibility and explicit boundaries.
- SOLID is mandatory.
- SRP and strong cohesion are mandatory for files, types, and functions.
- Vertical-slice architecture and the single-repository boundary are mandatory under `MCAF-ARCH-001`.
- Each feature MUST live in one consistently named repository-wide slice with all feature-owned backend, frontend, contracts, tests, and supporting artifacts colocated or mirrored under the documented slice convention and its durable doc mapped by the same name under `docs/Features/`.
- Local rules and ADRs MUST NOT weaken `MCAF-ARCH-001`; they may only document stricter rules or a time-bounded migration to compliance.
- Prefer composition over inheritance unless inheritance is explicitly justified.
- Do not preserve obsolete, dead, duplicate, or replaced legacy code unless the user explicitly asks for a temporary compatibility path.
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
- Frequent concise Ukrainian progress stating completed work, remaining work and real blockers.
- Shared-checkout work that preserves unrelated changes and completes authorized delivery.
- Real Docker/Aspire multi-node comparisons using the same data, oracle and workload contract.
- All published test results and chart values MUST come from JSON produced by successful GitHub Actions runs. Include raw GitHub JSON links, measured source SHA, options, topology and acknowledgement/read guarantees. Missing or unsupported measurements MUST remain unavailable; never invent winners, zeros or sample performance values.
- Website publication MUST use a separate GitHub Actions workflow that collects fresh JSON artifacts from the actual successful comparison job, verifies its run/job/revision/artifact provenance, and publishes those files. Performance figures MUST be read from that evidence; never hardcode them in website or README charts. A failed, skipped, incomplete, expired or unauthenticated comparison cannot refresh the published evidence.
- The separate website workflow MUST trigger when `site/` changes and when the performance-benchmark producer workflow completes. Website completion is scoped to the site, its evidence ingestion, qualification and publication workflow; independently owned database and performance implementation MUST NOT be silently added to that task.
- Owner direction on 2026-10-02 resolves the earlier website-source and producer-success policy conflict for REQ-BC-028: qualify the current trusted-main website separately from the measured-source checkout, and require the actual successful comparison job and its measurement steps. Unrelated database jobs or whole-workflow failure MUST NOT block this website-only delivery. Preserve accurate site/measured/control revisions and all website qualification gates. Earlier successful-run and equal-source guidance remains recorded as the superseded task boundary; never label a failed whole run successful.
- Free community features only for comparison engines. No Enterprise images, license activation or paid cluster features.
- KeyLoad's visual style is serious, adult Apple-style Liquid Glass in the Managed Code family: editorial off-white surfaces, bold black typography and Managed Code's pastel iridescent accent (peach → lilac → periwinkle), plus Prostir graphite. Do not use green or lime as the brand accent, and do not return to the rejected light-blue look. No Material Design, colour blobs, rainbow logos or playful decoration. The `/admin` console and the public site share it (owner direction 2026-10-02).
- The public landing must be a radically better product page for KeyLoad as the database for AI agents, on .NET and Orleans. Its Three.js scene must be live and moving as soon as it loads, except under reduced-motion preferences, within the scene lifecycle, budget and honesty contracts.
- Every public KeyLoad page ends with "Developed by Managed Code" and a normal followed (dofollow) link to https://www.managed-code.com/.
- The central KeyLoad K MUST use the canonical true SVG outside the bounded 3D raster buffer so it stays sharp on Retina displays. An SVG wrapper around a bitmap or a CanvasTexture logo does not satisfy this requirement (repeated owner correction 2026-10-03).
### Dislikes
- Repeated permission questions for already authorized work.
- GitHub Actions workflows MUST use the plain names CI, Tests, Benchmarks, Release and Website. CI owns PR checks, Tests owns project tests, Benchmarks owns performance tests, and Release builds packages (owner clarification 2026-10-03).
- Repository-rule checks MUST always run inside the standard CI pipeline, never as a separate governance workflow (owner correction 2026-10-03).
- All comparative performance tests, including TimeSeries image checks, MUST run in one separate Benchmarks pipeline; do not name it Comparisons or create feature-specific comparison workflows. Preserve isolated Linux runners, real native topology, complete workloads and authenticated artifacts (owner clarification 2026-10-03).
- Fake production readiness, power-loss claims from process-kill tests, or unsupported performance supremacy.
- Installing skills when the owner explicitly prohibited it.
- For bug-fix and optimization requests, do not spend turns on plans/status documentation without promptly making concrete source-level repairs once the scope is clear.
- Recolouring tokens or restyling the old layout when the owner asks for a redesign. A landing or `/admin` redesign means a complete new layout, information architecture, visual concept and interaction patterns, delivered for both surfaces (repeated owner correction 2026-10-02).

## Current additive project inventory

The original 22-project rule above remains mandatory and preserved. The current inventory additionally contains `tests/KeyLoad.SiteTests` (ADR-040), bringing the project count to 23. Its local AGENTS.md was created before implementation; every original and new project/module remains covered by the same mandatory policy. See `docs/implementation/mcaf-installation.json` for the exact current paths and the preserved installation baseline.

ADR-043 additionally introduces `benchmarks/KeyLoad.ComparisonHost`, bringing the
current project count to 24. Its local policy preceded implementation; the existing
KeyLoad.Comparisons public library and every prior mandatory policy remain covered.

ADR-047 additionally introduces `benchmarks/KeyLoad.BenchmarkScenarios`, bringing the shared workspace count to25. Its local policy preceded implementation. Every existing root/local rule remains mandatory and unchanged.

A bounded website qualification candidate contains the20-project historical runtime base plus KeyLoad.Analyzers, KeyLoad.Analyzers.Tests and KeyLoad.SiteTests (23projects). Its derived inventory MUST record the shared25-project scope and omitted concurrent projects explicitly, preserve all28 included root/local policy blobs exactly, and qualify only the actual included website/analyzer source. This is an evidence-scope record, never an exception to any solution-wide architecture, project-policy or required product qualification rule.
