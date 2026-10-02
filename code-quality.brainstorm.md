# CodeQuality brainstorm

## Problem and scope

The owner requests the sibling Prostir build/style policy and its editable Roslyn
analyzer project in KeyLoad. The root already promotes warnings, but does not
explicitly enable build-time style enforcement or attach custom analyzers. The
checkout includes unrelated ongoing governance and runtime changes.

In scope: copy the Prostir `.editorconfig` byte for byte, enable its build-time
analyzer settings solution-wide, port applicable custom rules into a KeyLoad-owned
CodeQuality slice, test the real Roslyn compilation boundary, retain diagnostic
reports, and add CI quality verification. Out of scope: database topology, public
database contracts, dependency upgrades, unrelated source cleanup, global skills.

## Options and recommendation

1. Reference the sibling checkout: fragile and violates the single-repository
   solution boundary; reject.
2. Copy every product-specific rule without inspecting applicability: misleading
   because Prostir has API/Gateway, Cosmos, Studio MCP and commerce contracts.
3. Bring the reusable analyzer infrastructure and applicable rules into KeyLoad,
   adapting assembly ownership; explicitly document product-specific exclusions.
   Choose this direction unless source inspection proves all rules applicable.

Use SDK-owned Roslyn references as Prostir does; no dependency replacement or
consumer workaround. Use compiler SARIF reports and normal IDE diagnostics rather
than a separate custom reporting engine. New rules belong to Features/CodeQuality.
Do not suppress newly exposed findings or call development builds test proof.

## Risks and questions

Strict settings will expose existing findings. Existing dirty files must survive.
GitHub qualifications cover committed SHA only, not the local dirty checkout.
The governance bootstrap currently lacks its local policies and working plan;
record those failures without weakening its verifier. No skills are installed.

## 2026-10-02 numeric-fixture review

The updated root policy makes the four source-owned numeric rules mandatory in
ordinary consumer builds. They now exist with an accepted metric contract and
real compiler tests. A bounded test-only review found that the current assertions
check diagnostic ID, severity, source path and line, while not checking the source
column. It also found unexercised syntax/content edges: multiline literal counting
in file/type metrics, directives inside type/executable spans, expression-bodied
property/indexer units, and several supported control-flow forms (including
foreach, else-if, using statement and fixed). The tests compile but have not run.

Add only real C# 14 compiler cases to the CodeQuality test slice, including exact
diagnostic starting columns for one failure per numeric rule. Exercise each listed
metric boundary against valid compilations. Preserve the analyzer implementation,
the four established limits, official generated-code exclusion and sole Database-
Engine aggregate exception. Improve the acceptance matrix and task plan before the
test writer starts. AC-CQ-009 coverage collection remains a separately pending
gate; these source-size/depth rules cannot stand in for a coverage report.

## Preliminary task graph and safe parallel work

| Task | Requirements / AC | Owner / tier | Permissions | Dependencies / start | Artifact / verification | State / join |
|---|---|---|---|---|---|---|
| TASK-001 | REQ-CQ-001 / AC-CQ-001 | Lead, planning model | Central files only | Root policy read | Scope, acceptance, plan, ADR | In progress; required before writers |
| TASK-002 | REQ-CQ-002 / AC-CQ-002 | Analyzer researcher, capable economy | Read-only | This graph | Source inventory, reusable rules, risks | Ready; lead reviews recommendation |
| TASK-003 | REQ-CQ-003 / AC-CQ-003 | CI researcher, capable economy | Read-only, GitHub reads | This graph | Baseline SHA/run/jobs, CI/report integration | Ready; lead reviews evidence |
| TASK-004 | REQ-CQ-002 / AC-CQ-002 | Analyzer implementation worker | Only new analyzer project | Acceptance + ADR contract | Analyzer sources/build evidence | Pending; diff inspection required |
| TASK-005 | REQ-CQ-002 / AC-CQ-004 | Regression worker | Only new analyzer tests | Acceptance + rule inventory | TUnit real-compilation tests | Pending; lead joins with TASK-004 |
| TASK-006 | All | Lead, planning model | Shared configuration/docs | TASK-002/003/004/005 | Integrated build, reports, CI/evidence | Pending; no worker claim substitutes proof |

Research can proceed independently. Central configuration, solution, workflow and
durable documentation have one owner. Implementation workers must have disjoint
new-project scopes. Qualification executes only in GitHub Actions.
## Remaining benchmark prerequisite stage, 2026-10-02

The actual library/host dependency build now exposes 123 diagnostics. Keep the
preserving host split and strict rules. Independent safe workstreams are XML-only
adapter documentation and private URI/cancellation/style corrections; serialize
shared corpus/runner argument guards, real-clock and private sampler-lock changes
through the lead. SQL identifier safety and public arrays/enum/exception ownership
need read-only design review before any structural migration. Do not internalize
the public library, suppress diagnostics, change workload/report bytes, run local
tests or infer a qualified performance improvement from source cleanup.
