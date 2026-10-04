# RepositoryGovernance

Status: configured locally and independently reviewed; GitHub delivery pending. Owner: KeyLoad lead/integrator. Source: [MCAF tutorial](https://mcaf.managed-code.com/tutorial), upstream edcfac048c3cb5e158e841378bd61fe457dc34e7. Skills are not installed by explicit owner instruction.

## Requirements

All requirements are mandatory. IDs remain stable when implementation changes.

| ID | Type / priority | Requirement and rationale | Measurable acceptance |
|---|---|---|---|
| REQ-MCAF-001 | Governance / P0 | Merge root and local policy without deleting or weakening any existing rule. | AC-MCAF-001: original root prefix hash and complete diff are preserved. |
| REQ-MCAF-002 | Workflow / P0 | Configure the four mandatory MCAF policies and real .NET commands. | AC-MCAF-002: all policy IDs and customized commands exist; no TODO placeholders. |
| REQ-MCAF-003 | Ownership / P0 | Every project and delivery module has local policy with real entry points and boundaries. | AC-MCAF-003: all 22 current csproj roots plus site, workflows, docs and scripts pass inventory validation. |
| REQ-MCAF-004 | Architecture / P0 | Map the complete repository using canonical slices and explicit interfaces; record existing layout migration debt. | AC-MCAF-004: diagrams, slice map and dated ADR cover all surfaces. |
| REQ-MCAF-005 | Orchestration / P0 | Plan before delegated writes, use capability/cost tiers, disjoint ownership and joined integrated proof. | AC-MCAF-005: task graph precedes writes and all required task results are reviewed. |
| REQ-MCAF-006 | Constraint / P0 | Install no skills, including .NET skills, for this owner-requested bootstrap. | AC-MCAF-006: skill inventory is unchanged. |
| REQ-MCAF-007 | Evidence / P0 | Keep conflicts, migration gaps and CI qualification explicit; publish only GitHub Actions performance JSON. | AC-MCAF-007: no unsupported readiness or benchmark claim is introduced. |
| REQ-MCAF-008 | Repository hygiene / P0 | Remove temporary planning Markdown files and keep requirements, acceptance and execution contracts in canonical Feature/ADR documents. | AC-MCAF-008: no tracked or checkout `*.plan.md`, `*.brainstorm.md` or `*.acceptance.md`; all three patterns are ignored without exceptions. |
| REQ-MCAF-009 | Validation / P0 | Repository rules validate durable documents and reject reintroduced working planning files. | AC-MCAF-009: the real Node validator passes without planning files and fails for each suffix at root or nested paths; prefix, ownership and skill checks remain. |
| REQ-MCAF-010 | Architecture / P0 | Organize each vertical slice by its actual responsibilities instead of placing mixed role files in a flat feature root. The migration covers every KeyLoad solution production, SDK, infrastructure, benchmark and test project. | AC-MCAF-010: every solution feature C# file belongs to a populated role folder; the before/after file-content inventory is identical, namespaces/aliases/Ids remain unchanged, live references resolve and Release compilation includes the moved files. |

## Slice surfaces

- Governance: root and all local AGENTS.md, docs/Architecture.md, docs/ADR/ADR-032-mcaf-governance.md and installation audit record.
- Tooling: scripts/Features/RepositoryGovernance/verify.mjs; Node built-ins only.
- Product backend, data schema, public API, authentication and physical storage: N/A because installation only adds governance and documentation.
- Frontend behavior: N/A; site local governance is added, but rendering changes belong to BenchmarkComparisons.
- Test flow: static real-repository validation and independent diff review. Runtime qualification source is GitHub Actions.

```mermaid
flowchart LR
    Tutorial[Canonical MCAF template] --> Merge[Preserve and merge policy]
    Existing[Existing mandatory rules] --> Merge
    Merge --> Root[Root AGENTS]
    Root --> Local[Project and module AGENTS]
    Root --> Contracts[Feature and ADR contracts]
    Contracts --> Workers[Bounded coding tasks]
    Workers --> Join[Lead review and integrated validation]
    Join --> Evidence[GitHub qualification evidence]
```

## ADR and execution contract

[ADR-032](../ADR/ADR-032-mcaf-governance.md) owns installation and time-bounded layout migration. This specification owns stable acceptance criteria; its execution table and the ADR own ordered work, while the installation record retains terminal evidence.

| Task | Requirement / acceptance | Owner / tier | Write scope | Dependency and join |
|---|---|---|---|---|
| TASK-MCAF-REVIEW-001 | REQ-MCAF-001/002/004/005/007; AC-MCAF-001/002/004/005/007 | Highest-capability read-only architect | None | Starts on existing policy/template; joins with concrete conflicts and review findings. |
| TASK-MCAF-REVIEW-005 | All requirements and acceptance | Highest-capability independent read-only reviewer | None | Starts on the merged contracts; joins only after all local files, validator and integrated evidence are reviewed. |
| TASK-MCAF-LOCAL-002 | REQ-MCAF-003/006; AC-MCAF-003/006 | Least expensive capable documentation worker | Only new project/module AGENTS.md | Starts after root/spec/ADR contracts exist; joins after every local file is inspected and inventory passes. |
| TASK-MCAF-CHECK-003 | REQ-MCAF-001/002/003/006; AC-MCAF-001/002/003/006 | Least expensive capable Node worker | Only scripts/Features/RepositoryGovernance/verify.mjs and new scripts/AGENTS.md | Starts after audit contract exists; joins with real-repository validation and meaningful negative validation evidence. |
| TASK-MCAF-INTEGRATE-004 | All requirements and acceptance | Lead planner/integrator | Root policy, central docs, installation audit and execution contracts | Joins all required complete workers, inspects all diffs, validates combined state and reports conflicts. |

Workers must stop on ambiguity, overlapping ownership, changed contracts or a policy conflict. No worker may edit central config, product source, README, existing dirty files, or skills. States are pending, running, complete, blocked, failed or cancelled; only reviewed complete outputs unblock integration.

## Traceability and evidence

TASK-DOCS-CURRENT-PATHS refines AC-DOCS-005/008 and AC-MCAF-010 after the owner's
responsibility-folder migration. Root owns integration; Luna cluster_wave owns
only a private proposed update of docs/implementation/status.json evidence paths
and its explicit mapping receipt. Replace a moved path only after identifying its
actual current file and owning task semantics; expand split files to the relevant
current slice files. Unique basename matches are evidence, not permission to
guess a renamed implementation. Preserve every task, status, title, requirement,
qualification limitation and immutable historical run/source receipt. Report
unresolved mappings with exact missing paths rather than dropping evidence or
inventing a delivered capability. No production/test/workflow or policy changes.
Root reviews the complete mapping and actual file existence before joining;
static path repair is not runtime qualification or task acceptance completion.

Every REQ maps to its same-numbered AC above, ADR-032, tasks in the execution table and validator/review evidence. All seven local configuration criteria passed independent review and integrated static validation; outcomes and native worker joins are recorded in implementation/mcaf-installation.json. This is not new GitHub runtime qualification. The initial inventory had 20 projects; concurrent CodeQuality work added two projects and their local policies, preserved by this installation. The current installation record covers all 22. Any delivered GitHub snapshot must include the recorded projects or the validator correctly fails; do not publish a governance-only commit with an inventory of omitted uncommitted projects.

## Повне покриття функцій та рішень

Власник вимагає описувати весь продукт через Features та ADR. Це розширення governance, а не зміна runtime. Контракт: [ADR-037](../ADR/ADR-037-documentation-coverage.md) та requirements/acceptance і execution contracts цієї owning Feature-специфікації. Старі REQ-MCAF/AC-MCAF зберігаються.

| Вимога | Критерій | Перевірка |
|---|---|---|
| REQ-DOCS-001: кожна канонічна функція має свій Feature | AC-DOCS-001: рівно 20 owning Feature-specs індексовані, включно з required BlobStorage | File/index inventory |
| REQ-DOCS-002: повний контракт функції | AC-DOCS-002: actors, entry points, stable REQ/AC, flows, slice/N/A, source/target/planned, tests і Mermaid | Full-file review та REQ/AC mapping |
| REQ-DOCS-003: змістовні ADR для всіх рішень | AC-DOCS-003: ADR-001–039 мають context/rationale/alternatives/consequences, implementation contracts та verification | ADR inventory і contract review |
| REQ-DOCS-004: унікальна ідентичність ADR | AC-DOCS-004: comparisons034, foundation036; filename/header і всі залежні links узгоджені | Number/reference inventory |
| REQ-DOCS-005: повний backlog/test trace | AC-DOCS-005: усі 104 KL мапляться на existing Feature/ADR; кожен new REQ → measurable AC → existing/planned test або explicit review exception | JSON/source/test inventory |
| REQ-DOCS-006: правдиві qualification boundaries | AC-DOCS-006: source не названий GitHub-qualified; public blobs/MCP та нерозв'язані choices не вигадані | Independent source/evidence review |
| REQ-DOCS-007: збережені правила і scope | AC-DOCS-007: immutable original hashes усіх 27 policies, незмінність ними цього task, full original/current hashes і повний старий текст при поясненому зовнішньому additive drift; попередні Feature IDs збережені; workers тільки у disjoint new docs | SHA256/full diff/ownership review; unexplained drift або rule loss/weakening — fail |
| REQ-DOCS-008: повна навігація та integrated proof | AC-DOCS-008: links/indexes/diagrams/governance/whitespace pass, COMPLETE packets joined, strongest-model review complete | Combined static checks і cached Mermaid render |

Точний набір: RepositoryGovernance, BenchmarkComparisons, DocumentStorage, EventStreams, Messaging, GraphTraversal, TimeSeries, Search, QueryExecution, Authorization, ChangeFeeds, StorageRecovery, ClusterReplication, ClusterRouting, ClientApi, BackupRestore, CodeQuality, ResourceExecution, TestInfrastructure, BlobStorage.

Owning executable-artifact convention для цієї документаційної роботи: `docs/Features/`, `docs/ADR/`, `docs/README.md` та `docs/implementation/documentation-coverage.json`; глобальні maps/indexes не є runtime slices. Backend/HTTP/UI/persisted schema N/A, бо змінюється опис, а не поведінка.

Позитивний flow: читач проходить README → docs index → Feature → ADR → source/test/evidence. Негативний/error flow: missing/duplicate doc, broken path, unknown KL, неіснуючий test або неподтверджений Implemented зупиняє completion. Edge flow: required capability без реалізації має Proposed ADR, планований test і явний pending status; historical GitHub proof залишається historical.

TASK-DOC-AUTHOR-004/005 мають тільки нові, різні файли; лід володіє існуючими документами/індексами й join, TASK-DOC-REVIEW-007 — strongest read-only review. Повний task graph, моделі, start/join/terminal/escalation contracts — у цьому Feature та ADR-037. Product tests лишаються real TUnit/Recovery/Docker-Aspire RF3 через SDK/MCP у GitHub Actions; документаційні criteria мають статичну перевірку та explicit full-source-review exception.

Актори та entry points: власник задає scope, contributor/agent читає root/local AGENTS та Architecture перед task, strongest planner фіксує contracts, bounded worker повертає evidence, integrator/reviewer joins всі результати; CI запускає static validator. Required/planned policy або source migration не оголошується виконаною від створення spec чи локального validator pass. Installed-by-bootstrap status і delivered-source qualification залишаються в owning audit records.

## Historical workflow separation, superseded by ADR-064

[ADR-062](../ADR/ADR-062-workflow-separation.md),
[acceptance](../implementation/workflow-layout-acceptance.md) and
[plan](../implementation/workflow-layout-execution.md) specify REQ-WF-001..006 and AC-WF-001..006:
CI always owns PR repository-rule/build checks; Tests owns ordinary project
qualification; Benchmarks owns every performance comparison, including TimeSeries;
Release builds NuGet package artifacts; Website retains separate publication. Historical legacy CI report
identity remains authentic while new isolated cohorts bind to benchmarks.yml.
Each same-numbered REQ/AC maps to the plan's task graph and TUnit/GitHub proof;
no database, workload, native topology or unrelated website design changes apply.

## Three-pipeline integration, 2026-10-03

The owner's latest explicit correction supersedes the five-workflow placement above.
[ADR-064](../ADR/ADR-064-three-pipeline-release-delivery.md) and
[ReleaseDelivery](ReleaseDelivery.md) define REQ/AC-PIPE-001..004 and REL-001..003:
CI combines ordinary build/test/rule gates; Benchmarks runs every load/comparison
suite and qualifies/publishes the same run's metrics; Release builds real database
packages/distribution/images and creates an immutable dated tag/GitHub Release after
successful exact-source CI. TUnit source/version/current-producer regressions and
actual GitHub run/job/artifact/provider records own verification. Static checks do
not establish successful qualification, site publication or database release.

## Working-file removal, owner correction 2026-10-03

REQ-MCAF-008/009 and AC-MCAF-008/009 supersede only the earlier requirement for separate working Markdown files. Durable REQ/AC, ordered execution, test mapping, ADRs, policy preservation and qualification remain mandatory. No working plans are renamed or relocated into documentation.

| Task | Owner / permission | Dependency / completion |
|---|---|---|
| TASK-MCAF-CLEAN-001 | Root; policy, ignore rules, deletion, canonical governance contracts and integration | Owner request; all planning files absent and ignored, unrelated work preserved |
| TASK-MCAF-CLEAN-002 | Node/TUnit worker; only RepositoryGovernance validator and matching test slice | Approved AC-008/009 and ADR-032 contract; original validator runs against actual positive/negative filesystem roots |
| TASK-MCAF-CLEAN-003 | Documentation worker; Feature/ADR/live Markdown references, excluding these two governance contracts | Frozen ownership; references point to existing canonical contracts, no capability or qualification claim changes |
| TASK-MCAF-CLEAN-004 | Catalog worker; live documentation coverage and implementation status pointers | Frozen ownership; temporary source pointers removed/replaced; historical measured receipts untouched |
| TASK-MCAF-CLEAN-005 | Root; final review, static checks, focused TUnit, formatter, scoped commit/push | Every required worker complete and reviewed; no staging of unrelated changes |

Testing methodology: AC-008 uses complete file/Git inventory and real `git check-ignore` checks at root/nested paths for every suffix. AC-009 uses TUnit/Microsoft.Testing.Platform to execute the original Node validator on real filesystem copies of the current repository's required policy/inventory documents, with no planning files (pass) and each suffix at root or nested paths (fail). Existing prefix, project/module, required document and skill rejection checks remain. Static review verifies live links and that only the explicitly authorized file-placement policy changed. Local development checks do not establish RF3, recovery, performance or production qualification.

Local verification, 2026-10-03: removed 59 tracked files and 123 additional checkout files; all three suffixes are ignored at root and nested paths, including the former governance exceptions. The live Node validator passes with 26 projects and four modules. The original nine TUnit cases pass with no failures or skips; their focused Release build has zero warnings/errors and the scoped formatter passes. Reference review preserves existing REQ/AC occurrences and Markdown fences and resolves every new local link. The shared solution build is blocked by concurrent, untracked scaled-storage tests requiring unfinished Fixture/Snapshot types; those files are outside this delivery.

## Feature-local responsibility structure, 2026-10-04

REQ-MCAF-010 / AC-MCAF-010 extend MCAF-ARCH-001 and [ADR-032](../ADR/ADR-032-mcaf-governance.md).
A feature folder remains the ownership boundary; role folders inside it make grains,
commands, queries, models and supporting protocols discoverable. Global layer folders,
empty placeholder folders and namespace/wire-contract changes are outside this migration.

TASK-MCAF-LAYOUT-001: root owns policy, this specification, ADR-032, Architecture and
all physical moves in `src/KeyLoad.Orleans/Features/`. TASK-MCAF-LAYOUT-002: read-only
reviewer inventories responsibilities and path-bound consumers, then reviews the final
map. Shared documentation and source moves have one integration owner because the
current routing files contain concurrent CQRS changes. Reviewer starts after the root
policy correction; integration joins only after its exact mapping and concerns are reviewed.

Ordered work: capture exact file contents; group routing, replication and cache metadata
by actual role; move small read-capability slices into Queries; repair current source
navigation; compare the complete byte inventory and ensure no flat C# files remain;
run governance, formatter, solution Release build and relevant AppHost suites. Positive
flow resolves a grain/query/model through its owning slice and role. Negative flow
rejects lost, duplicated, flat or changed source files. Edge flow preserves dirty and new
files byte for byte. Compiler/style failures and unavailable test infrastructure remain
explicit blockers. No runtime behavior changes are intended, so no new behavior test
is added: the structural criteria use explicit complete-inventory/manual role review
plus existing compilation and runtime suites. Local checks do not qualify RF3 or durability.

```mermaid
flowchart LR
    Slice[Owning feature] --> Grains[Grains]
    Slice --> Commands[Commands]
    Slice --> Queries[Queries]
    Slice --> Models[Models and contracts]
    Slice --> Protocols[Streaming and supporting roles]
    Grains --> Verify[Unchanged source and runtime contracts]
    Commands --> Verify
    Queries --> Verify
    Models --> Verify
    Protocols --> Verify
```

Structural development verification, 2026-10-04: moved all108 current Orleans C#
files (105 tracked files and three concurrent new source files) across all five slices;
complete SHA-256 inventory and independent role review passed with unchanged source.
Governance and the full solution formatter passed. Release solution build passed with
zero warnings/errors (MSBuild servers disabled, single build worker after sandbox IPC
blocked the initial attempt). RF3 attempt:14/93 passed,79 failed;76 reject macOS
temporary reparse paths, two require authentic GitHub RF3 image identity and one requires
genuine prior-server proof. Unit/recovery verification remains pending; no delivered
Linux GitHub runtime, fault, performance or durability qualification is claimed.

## Whole-solution structural migration, owner scope 2026-10-04

The owner explicitly selects the entire KeyLoad solution after the Orleans checkpoint.
REQ-MCAF-010 / AC-MCAF-010 now apply to every production, SDK, infrastructure,
benchmark and test project, with actual role folders inside each canonical feature.
Executable site/tooling artifacts retain their existing fully colocated conventions;
inspect them for mixed ownership and record their applicable structure without inventing
C# role folders for HTML or shell entry points. Composition roots remain outside slices.

| Task | Owner / exact write scope | Dependencies / join |
|---|---|---|
| TASK-MCAF-LAYOUT-003 | Engine worker: Core, Query, Storage.ZoneTree, Storage.IO, Security, Replication, Artifacts, Diagnostics; local policies and exact-content source moves only | Accepted whole-solution scope and this contract; return exhaustive old/new/hash map and role review |
| TASK-MCAF-LAYOUT-004 | Surface worker: Abstractions, Server, Client, Cli, ServiceDefaults, AppHost, Analyzers, and all benchmark projects; local policies and exact-content source moves only | Same contract; no central config or runtime/API changes; return exhaustive map |
| TASK-MCAF-LAYOUT-005 | Test worker: all tests projects; local policies and exact-content moves only | Same contract; group cases, fixtures, assertions, models and real-process infrastructure by actual responsibility; return map and path-sensitive concerns |
| TASK-MCAF-LAYOUT-006 | Root: global maps, live path references, docs, site/tooling review, combined verification and scoped commits | Join all reviewed maps; preserve concurrent edits and historical immutable receipts |

Workers read the full nearest policy and actual code before classifying files. They move
feature-owned files into populated roles, retain namespaces/aliases/Ids and byte contents,
and do not rewrite behavior, take over other tasks, alter APIs, install tools, weaken
checks or commit/push. Existing nested ownership is retained; new flat files produced
concurrently are reported for final join. Root owns every shared path repair. Each
worker must return complete/blocked/failed/cancelled; missing or ambiguous ownership
cannot silently count as complete. Root validates all bytes and paths, then repairs
path-bound checks without changing their assertions, runs build/formatter/governance
and AppHost suites, and records actual qualification separately from structural progress.

Root structural split contract: `Core/GraphAndSeries.cs` mixes GraphTraversal,
TimeSeries and Search. TASK-MCAF-LAYOUT-006 splits its existing `DatabaseEngine`
partial declarations into the already owning feature/role folders. Preserve every
method body, signature, documentation, namespace and serializer contract; only repeat
the original using/namespace/partial-class shell. Exact file-hash equality applies to
physical moves; this split additionally compares the concatenated original declaration
bodies and records old/new ownership, then requires complete compilation and formatter.
No logic rewrite, storage/API change or new abstraction is authorized by the split.

The same exact-declaration split applies to `Abstractions/Queries.cs`: place each public
request/result record in its owning DocumentStorage, EventStreams, Messaging,
GraphTraversal, TimeSeries, QueryExecution, Search, BackupRestore or ClusterRouting
slice under Contracts. Preserve XML documentation, signatures, attributes, aliases
and field IDs. Repeat only required using/namespace headers; verify original declaration
text and compile the complete solution. Concurrent API changes remain unstaged.

The existing repository governance validator also rejects C# files directly at
`Features/<SliceName>/` across all projects. Positive evidence is the complete live
inventory; negative development evidence temporarily introduces one owned flat C#
fixture, requires validator failure, removes it and requires success. This enforces
REQ-MCAF-010 without changing runtime contracts; role meaning still requires code review.

Whole-solution source checkpoint: `ebe6f1a`; import-only cleanup: `594173a`.
[Development receipt](../implementation/vertical-slice-layout-2026-10-04.json) binds
structural reference tree `7e5dc117ec315105b4f1bc237653e07e929e8772` to the base
`37a9da0`, excluding concurrent feature bodies. Complete reference inventory, original
GraphAndSeries member text and original HEAD query declarations verified; Release
solution build and formatter passed; AppHost governance regressions passed 37/37.
The shared checkout build still fails in concurrent Messaging/Search implementation.
The full unit attempt has no completed result after native DCP watcher timeout and
controlled shutdown; causality for the long suite is unproven. Readiness fixture
regressions also passed 10/10 through AppHost, for 47/47 targeted cases. The website
session hook requires authentic source/archive preparation missing from this local
reference. Complete Linux runtime/RF3/fault/endurance gates remain pending.
