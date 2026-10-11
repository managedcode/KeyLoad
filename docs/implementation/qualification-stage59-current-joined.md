# Stage59 joined implementation and recorded task acceptance

The original architecture plan now has **18 accepted tasks and 86 in progress**. KL-065 is newly accepted for the exact recorded Linux source below. Acceptance is scoped to the original task criteria; current whole-product coverage, RF3, endurance and performance qualification remain open.

## KL-065: complete original task acceptance

Source: `f0d869c08f1a9efe30e93911c4631ad26eb87fb0`. The [successful Linux task job](https://github.com/managedcode/KeyLoad/actions/runs/38162441575/job/114538000515), attempt 1, executed **78 cases in 20 selections**: **45 Unit, 25 process recovery and 8 Aspire RF3**. All passed; none failed or skipped. RF3 operations used the actual .NET client and official MCP SDK against the Aspire-owned Docker cluster.

| Original architecture acceptance criterion | Executed operation evidence |
|---|---|
| Cross-tenant IDs do not collide in cache | `GenerationVisibilityTests` proves complete tenant/epoch-scoped SQL and graph sets, held positive/negative leases, retirement, shared admission, cancellation, original replay and same-root cold. The ZoneTree point-cache authorization flow and cross-tenant RF3 whole flow independently exercise current persisted scope. |
| Stale grants do not pass final authorization | Security/query, equality access paths, SQL inner join, partition query, document mutation and team policy operations prove persisted owner/team/project grants, removal, revocation, higher-epoch repair and complete original outcomes. Public SQL/partition and graph capability RF3 operations verify SDK/MCP refusal and authorized continuation. |
| Hidden graph paths do not appear in response or Explain | Incoming privacy, traversal, shortest-path and graph-search operations preserve hidden source/target/intermediate policy and field-use authorization. Five graph RF3 cases exercise real SDK and official MCP responses, persisted revocation, repair and revisioned projections. |

Root independently checked all **189 original length/SHA references**, the exact old registry from Git at this source, unique genuine native UIDs and declarations, all original TRX identities/outcomes, exact Git source/PDB mappings for **6,592 files**, compiled-source receipts, unchanged prepared/before/after images, native exit/readers/disposal and the source-bound Docker image. The original CI acceptance verifier itself exited successfully with all readers joined. DLL/PDB binary digests are authenticated CI receipts; binaries are absent from this artifact, so local binary digest recomputation is not claimed.

Authenticated artifact: [`task-acceptance-KL-065-normal`, 11711112499](https://github.com/managedcode/KeyLoad/actions/runs/38162441575/artifacts/11711112499).

| Original | SHA256 |
|---|---|
| GitHub ZIP | `baeb291d60863d793ea873bbfdc1a8c16bacd784d0ff2ed64e8e6f07925dbc5b` |
| Original acceptance receipt | `c10e39fd5129e46a9517f453c7a6a93cd3f3eaf462761e72ec0fd967d9df1f74` |
| Original execution manifest | `6b479297111e2c08098e179dc489ffc847dd56954fc32a8295a6bcbebdcd193c` |
| Independent observation | `52d23f3f12c4f7a02708f913ed94a57519f301a9393f6b2a57e65b68df7aec37` |

The independent observation is retained at `/private/tmp/keyload-stage58-f0-linux-intake-r1-20261011/kl-065-independent-intake.original.json`. The durable status records the source, job, artifact, hashes and criterion mapping. Later shared execution changes require their own regression checks.

## Joined implementation

The command inbox now preserves control admission while serving actual admitted tenants in bounded round-robin turns. It retains the ordered apply gate, original limits, cancellation, shutdown outcomes and lease ownership. The new whole operation proves A1/B1/A2/A3 order, principal/tenant refusal without effects, cancellation, shutdown, complete receipts, replay and same-root cold continuation. This is supporting KL-076 implementation, not its complete CPU/RSS, mixed-client or Linux performance gate.

Replica security fixtures explicitly borrow the owning `TimeProvider`. The signed expiry operation retains exact original MAC/refusal/reply/replay bytes. It is a required transport control and remains a noncontributor: verifying the accepted envelope does not dispatch a database write.

Due-work algorithm fixtures now use an explicit logical provider while preserving the actual native ZoneTree reader, range accounting, cancellation, original deadline predicate and iterator/gate ownership. Their deadline operation proves exact BudgetExceeded, complete no-effect state, healthy continuation and cold replay. The original Linux system-clock failures remain evidence; no product latency repair or enlarged deadline is claimed.

CodeQuality uses native filesystem enumeration without changing path/quota/link checks or source digests. Functional report admission requires globally unique genuine native identity and a complete required-case mapping before publication. Known optional nonempty source `rawUrl` is supported with required line/range links; unknown or malformed fields still refuse. The owning whole operation preserves originals, tests duplicate/swapped/source/status/unknown-field refusals and joins every native child.

The strict task registry includes the three previously omitted credential-status operations for KL-014 and the original `ClientRemoteLink` telemetry instance for KL-015. This fixes task-selection inventory; it does not claim their subsequent Linux operation lane passed. KL-015's original held-write/producer-settlement RF3 failures remain open.

R40 registers only the actual two new native instances: tenant admission in existing functional group 05 and the signed clock transport control as a required noncontributor. The existing 3,478 identities/classifications, all five selectors and 61 contributors are preserved; the five due-work entries only refresh observed source/span bindings. The registered inventory is 3,053 functional plus 427 required-control instances. The seven-case discovery is selected actual metadata, not a complete current census or numerical coverage result.

## Current local checks and immutable failures

The full Release solution build passed with **0 warnings and 0 errors**, approximately 150 seconds, with 8,696 source inputs and zero drift. Its receipt is `/private/tmp/keyload-stage59-native-orleans-owner-rule-r1-20261011/main-build-r3-completion.original.json`. Native Roslyn analysis also reported zero errors/warnings; informational findings are retained and are not all declared resolved.

The focused native TUnit cohort executed eight whole-operation cases with configured parallelism 50 and normal SIMD. Seven passed and one failed, in approximately 95 seconds. The tenant case failed during genuine snapshot-directory admission because macOS's original `/var/folders/...` temporary path traverses the `/var` symlink; the product guard correctly refused it. No storage guard or test assertion was weakened.

The same unchanged tenant case subsequently passed with `TMPDIR` set to the existing canonical `/private/var/folders/...` directory, in 1.449 seconds. The native runner exited zero; source drift was empty. This separate one-case result does not rewrite the original eight-case failure or manufacture a new passing cohort. Originals are in `TestResults/stage59-focused-originals/`, including native discovery, DLL/PDB/compile identity, both process reports and TRX results. The five due-work cases, borrowed-clock control and CodeQuality whole operation passed in the original cohort.

After those executions, registry/classification and durable acceptance documentation changed. The canonical formatter checked all 18 changed C# files and found one whitespace-only IDE0055 failure in `DueDiscoveryDeadlineFlow`. The native Roslyn formatting action corrected that brace placement without changing operation semantics. A strict owning Unit project Release rebuild then passed with **0 warnings and 0 errors** in approximately 105 seconds, retaining the previously built product images. Its original completion is `TestResults/stage59-focused-originals/post-format-unit-build-completion.original.json`.

The matching one-file `dotnet format whitespace --verify-no-changes --no-restore` successor passed in 7.377 seconds with native exit zero and both readers closed. Its original is `TestResults/stage59-focused-originals/formatter-whitespace-completion.original.json`. The original all-file formatter failure remains preserved separately; the unchanged operation semantics do not require rerunning the 92-second CodeQuality flow. Repository governance and `git diff --check` also passed; governance preserved the full existing root-policy prefix, 27 projects and four modules.

## Remaining qualification

The original full Linux Unit run at the same `f0d869c` source executed 3,478 cases: 3,461 passed and 17 errored. The full process-recovery suite passed **349/349**. The three-node database job failed; full functional coverage and its strict successful merge remain open. These results are distinct from the successful KL-065 task lane.

KL-035's new exact lane passed **49/49** with all 45 originals independently verified. Its earlier `fc4e490` ordered-tail absence remains an unresolved regression; a later passing run does not establish a causal repair. Its status stays in progress. KL-079's original reconnect failure remains open with the full QueryPage and Outbox comparison preserved.

Native Azure/Orleans membership, six-silo physical trust, discovery, custom peer-HTTP/provider retirement and autonomous ANN original-result ownership are separate prepared implementation waves. They are not part of this built/tested source stage and do not receive runtime or acceptance credit here.

## Quality review

The installed quality skills were reviewed in the required order. Root `.editorconfig`, latest-all SDK analyzers, warnings-as-errors and KeyLoad semantic maintainability rules retain ownership and unchanged severity. The Release build and native diagnostics are actual first-party/semantic analysis checks. `dotnet format` remains the formatter; CSharpier is N/A by explicit repository choice. Roslynator, Meziantou and StyleCop package installation/CLI execution are N/A for this stage because the repository uses its existing SDK and KeyLoad analyzer baseline; none was introduced or substituted. The existing 400/200/64/3 limits are unchanged.

The added operations cover original refusal, healthy, replay and cold flows rather than field getters. CodeQuality admission/source binding is stricter and the tenant scheduler owns one bounded responsibility. Whole-product numerical coverage and source-matched CRAP are **unmeasured** until the authentic successful coverage gate; missing data is not fabricated as zero or a passing score. Canonical formatter and repository governance receipts are retained in `TestResults/stage59-focused-originals/` as original process results before the stage commit.
