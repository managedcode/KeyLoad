# Comparative host acceptance

Goal: keep the shared comparative harness available to its real callers while
giving Aspire a thin, correctly owned CLI executable under mandatory analysis.
Actors are the Aspire host, CI process invoker and harness-library consumers.

Scope: one new executable, unchanged library assembly/API, project reference and
resource wiring, lifecycle ownership, configuration error regression evidence.
Out of scope: benchmark CLR collection/enum migration, new engines/profiles,
report schema, database behavior, site changes or qualification shortcuts.

Assumptions: existing explicit strict-analysis/resource repair authorization covers
this preserving boundary; lead accepts ADR-043 before delegation. Shared native
AppHost ownership permits only the exact project reference and generated type
join. Any wider topology/configuration requirement stops this stage for review.

| Criterion | Pass/fail | Test and evidence |
|---|---|---|
| AC-HOST-001 | Existing public harness assembly, namespace and every public signature stay unchanged; it becomes a library. Fail on internalization or lost caller. | Normal dependency-enabled builds and lead public-surface diff review; no reference-disabled build. |
| AC-HOST-002 | New net10/C#14 host has local policy before implementation, canonical BenchmarkComparisons behavior and composition-only Program. Fail on rule suppression, a duplicate CLI or new dependency. | Strict build, composition analyzer, formatter and governance. |
| AC-HOST-003 | Required settings, environment/CLI precedence, current targets, report files and successful/failed exit meaning stay unchanged. Configuration failure precedes client allocation. | Real child-process cases for missing required settings, CLI override and rejected configuration; full real-engine GitHub comparison flow. |
| AC-HOST-004 | Every successfully constructed client/target has one cleanup owner; partial construction cleans up prior owners. Console handler detaches before lifetime disposal. Fail on retained handlers or lost prior owners. | Explicit source/lifetime review exception for private construction paths; actual GitHub comparison cleanup and cancellation evidence required. No fake targets/handlers. |
| AC-HOST-005 | Aspire uses the new generated host type, retaining comparisons resource name, all environment/waits/images and RF3 nodes. | Exact two-site source diff plus real GitHub/Aspire comparison suite. |
| AC-HOST-006 | Solution/inventory/docs contain the new project and distinguish source/build/formatter from test/runtime proof. | Governance, inventory/property evaluation, full strict solution build/format and exact-SHA GitHub artifacts. |
| AC-HOST-007 | A target cleanup failure keeps every later owner eligible for ordered cleanup, emits only the existing target/type diagnostic asynchronously, then fails through named ComparisonTargetCleanupFailed without secret-bearing messages or inner errors. Partial publication retains one pending owner and transfers client ownership exactly once. Native initialization failure takes precedence over unsupported capability, which is reported only after successful initialization. | Real comparison-host child process against unusable endpoints: nonzero exit, safe failure code, exact full case cardinality, every case failed with setup detail, null measurement and zero samples, no credential text. Preserve the independently verified unsupported-after-successful-initialization contract. Full source review for private transfer and all-finally paths under004; exact-SHA real GitHub comparisons remain mandatory. |

Negative/edge flows include no settings, invalid option values, CLI override of an
invalid environment value, absent later settings, setup failure, cancellation and
cleanup failure. No client-supplied trusted roles, new persistence/wire fields or
data migration is involved. Rollback restores the original sole CLI together with
project/Aspire wiring; never keep both executable implementations.

All tests use TUnit/MTP and actual child processes or Docker/Aspire engines, only in
GitHub Actions. No doubles, local test execution or invented numeric coverage.
The whole product suite, report comparison and existing analyzer gates remain
required. Pending host tests do not make the current solution build qualified.
