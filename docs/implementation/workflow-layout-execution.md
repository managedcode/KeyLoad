# Workflow layout plan

Inputs: [brainstorm](workflow-layout-brainstorm.md),
[acceptance](workflow-layout-acceptance.md), ADR-062. Owner final scope supersedes the initial three-pipeline draft. Lead integration owner approves
the bounded contracts under the owner's explicit CI/Benchmarks separation direction.

Parallel workstreams: provenance audit, disjoint C# protocol/regression updates,
and production isolated artifact adapters; lead alone owns workflow YAML, shared
policy, feature/ADR integration, site handoff and delivery. The existing legacy
CI producer must not be silently renamed in retained historical JSON.

| Task | AC | Owner/tier | Permissions/ownership | Dependencies/start | Artifacts/verification/join |
|---|---|---|---|---|---|
| TASK-WF-AUDIT | 003/004 | capable read-only worker | inspect scripts/tests/pages, no writes | accepted scope | exact identity routes and migration risk report; terminal complete/blocked/failed/cancelled |
| TASK-WF-CS | 001/002/003 | cost-efficient capable C# worker | named clean C# comparison protocol/fixture files and new unit layout tests only | contracts and lead naming decision | diff + source checks; no local tests; lead review before join |
| TASK-WF-SCRIPTS | 003 | cost-efficient capable JS worker | isolated producer scripts only, explicit named files | audit and accepted new identity | diff + syntax checks; no local tests; exact provenance remains fail-closed |
| TASK-WF-ROOT | all | lead planner/integrator | workflows, policy, docs, site adapter/constants | serialized contract ownership | review all diffs, static checks, scoped commit/push, GitHub results |

Ordered steps and done conditions:
- [x] Read root/local policy and architecture; record owner correction first.
- [x] Write brainstorm, detailed AC/test matrix and this task graph before workers.
- [x] Inspect baseline full relevant GitHub CI/Website failures at existing main SHA;
  track failing job/step/root cause before attributing any result to this change.
- [x] Approve ADR-062 implementation contract: final producer Benchmarks; current CI/Website names change while historical KeyLoad CI main-push identity remains exact.
- [x] Join read-only audit; update only new isolated producer identities, retaining
  authentic legacy historical CI proof. Delegate bounded C#/script scopes in parallel.
- [x] Move comparison graph and TimeSeries image checks into benchmarks.yml, with
  complete prerequisite build/format/rules; move standalone rules into standard CI.
- [x] Update Pages completion trigger and matching measured-source producer copies.
- [x] Add/update TUnit regressions derived from AC; inspect every worker diff and
  exact native job/step/artifact names. Workers cannot commit or push.
- [x] Run static governance, syntax/whitespace and integrated source review. Preserve
  all pre-existing dirty work; stage only task-owned hunks/files.
- [x] Commit and push the stable layout milestone on current main. Inspect actual
  push-triggered Tests/Benchmarks, manual CI/Release and Website exact-SHA jobs. No duplicate full dispatch.
- [x] Download changed-job artifacts, record actual SHA/run/job URLs and gate status;
  fix layout/provenance failures and repeat relevant GitHub checks if required.
- [x] Close all AC or state exact pre-existing/external blockers without claiming
  failed/skipped/incomplete measurement, readiness or publication success.

Baseline failures: main9b3bd8ed64b42e7e3bd37d454face7984dfc6b5f CI37111280400 and
Website37112008157 fail. CI verify job111169530401 fails only the existing
AcTsi006UnknownProblemNamesAndUnavailableStatusStayUnknown assertion at line44;
that test file was already dirty before this task and remains outside this scoped
layout change. Website qualify job111171578311 fails authenticated comparison
selection before tests (no complete isolated producer), followed by missing native
coverage inputs. Governance37111280336 succeeds, but its standalone workflow
contradicts owner-directed layout. These remain existing qualification gaps;
never weaken tests, use partial results or invent coverage to bypass them.

Validation guidance: no extra skills are applicable to this bounded infrastructure
change; install none. TUnit/MTP runs only in GitHub. Every new assertion targets
caller-visible pipeline composition/provenance rather than performance arithmetic.
Build precedes tests; full ordinary CI and preserved native comparison gates remain.
Existing incomplete product qualifications are not silently included as new repairs.

## Final owner clarification and task graph extension

- [x] TASK-WF-ROOT separates CI PR checks from Tests main/PR/manual project suites,
  names Benchmarks/Website plainly, and adds Release tag/manual package builds.
- [x] TASK-WF-CS updates the bounded C# fixtures and minimal source-contract
  regressions to all five final workflow roles/names; no runtime behavior changes.
- [x] TASK-WF-SCRIPTS updates exact isolated Benchmarks routes; adds explicit
  current CI metadata name separately from historical legacy KeyLoad CI run name.
- [x] TASK-WF-AUDIT reviews final migration and package/provenance boundaries.

No public package publishing is requested: Release retains real generated NuGet
packages as run artifacts. Current CI removes main-push triggers, so legacy push
history can remain strictly KeyLoad CI while its current metadata is named CI.
Final shared contracts are lead-owned; workers receive disjoint named file scopes.

## Integrated static evidence

Both bounded C#/JS packets are COMPLETE and joined after lead diff review. The
independent audit's undefined EventBlock call and incorrect RF3 assertion location
were corrected before staging. Ruby parsing confirms the five final names; exact
original native job definitions remain equal apart from their prerequisite. All
seven modified JS modules pass node --check and scoped whitespace checks pass.

The shared checkout acquired an unrelated new Diagnostics project during this
task; its in-progress inventory briefly fails static governance. An exact current
HEAD archive plus only this task's source changes passes the existing validator:
25 projects,4 modules, original policy prefix and unchanged skill inventory.
The sandboxed development build returned exit1 with0 warnings/0 errors after
5min and is not a passing build result. Required compilation/runtime evidence
comes from the exact delivered GitHub workflows. No local tests were run.

## Delivered GitHub milestone

Commit `9e0532fdeed7c55a17f9c857f8d5a264215341e7` is pushed to main.
The authenticated workflow inventory contains exactly the five active names
CI, Tests, Benchmarks, Release and Website. CI run
[37113420787](https://github.com/managedcode/KeyLoad/actions/runs/37113420787)
passes repository rules, the complete Release/format build and analyzer tests.
Release run
[37113424709](https://github.com/managedcode/KeyLoad/actions/runs/37113424709)
passes and retains nine actual `0.1.0-dev` NuGet packages. Downloaded package
versions, sizes and SHA256 values agree with its source/run-bound `packages.json`.
Artifact `packages-0.1.0-dev` has authenticated id `11271266373`.
The qualification receipt (report removed from repository) retains the exact
run/job/artifact identities, verified package manifest and real image test summary.

Benchmarks run
[37113337516](https://github.com/managedcode/KeyLoad/actions/runs/37113337516)
passes its complete build/format/rules prerequisite and the moved real pinned
TimeSeries image job `111176089078`. The unchanged native resource-model suite
then fails four Timescale image-tag assertions (expected `2.30.2-pg18`, actual
empty tag). The incomplete cohort cannot refresh website metrics.

Tests run
[37113337508](https://github.com/managedcode/KeyLoad/actions/runs/37113337508)
passes complete build/format/rules and 118 analyzer cases. All four new
workflow-layout regression cases pass, as do all 183 process-recovery cases.
The ordinary unit suite passes 1856/1857; its sole failure is the same pre-task
SDK-status assertion listed in the baseline above. RF3 passes 62/63 and reports
one existing SQL admission assertion
`AcAisql007InsufficientDataBytesRejectSdkAndOfficialSqlBeforeIdClaimWhileDirectControlKeepsRf3Healthy`.
The moved RF3 job definition is unchanged. Downloaded genuine TUnit reports bind
these counts and focused assertions to the exact milestone source SHA.

Website run
[37113337499](https://github.com/managedcode/KeyLoad/actions/runs/37113337499)
fails authenticated comparison selection and missing coverage inputs, matching
the pre-task publication blocker. The complete isolated archive needed by the
SiteTests before-session setup remains unavailable, so a new source/provenance
test is compiled but cannot be counted as runtime-qualified. Publication gates
remain intact. These failures do not establish successful database, benchmark
or website qualification. The follow-up preserves the original PR trigger and
PR cancellation in the separate Tests workflow.
