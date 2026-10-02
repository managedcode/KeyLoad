# Comparative harness library and CLI boundary

The strict source cut has 32 CA1515 diagnostics because the comparative executable
also exposes the API consumed by real unit and container tests. Internalizing those
types breaks callers; disabling the rule weakens the requested analysis.

Chosen direction: retain KeyLoad.Comparisons at its existing path, assembly and
namespace as the public harness library. Move the sole executable entry into
KeyLoad.ComparisonHost, with feature-owned CLI composition and one thin Program.
Aspire still launches resource comparisons with the same settings and dependencies.
This is a source/build boundary repair under the accepted strict-analysis goal.

Public DTO/interface/constructor signatures, enum values, reports, workload, engine
registration and topology are outside this stage. Nine public array diagnostics
and ComparisonTopology.Single require a separate accepted CLR migration; this
split does not claim to clear them. The currently wired six targets must not be
reported as the nine-engine ADR-034 qualification.

Parallel work: one worker owns the new host after its local policy exists; another
owns only new real child-process startup tests; lead owns project/solution/Aspire,
inventory and durable docs. Existing native and website owners remain protected.
No worker edits shared contracts, adapters, existing tests, central config or CI.

Risks: target construction can fail before cleanup, console handlers can retain
disposed lifetime state, and changing project references can affect Aspire metadata.
Use explicit lifetime ownership, preflight required configuration before creating
clients, detach handlers and test real host failure exits. Do not run local tests.

Existing relevant baseline is CI 36936319423 at
9c570f8c33a7a9667507a8e1c0ca68860de3be45; it predates this stage. Current strict
build/formatter blockers and pending exact-SHA GitHub proof remain visible.

The normal host build now exposes six CA2000 construction-transfer findings and
CA1849/CA1031 in cleanup. Chosen bounded correction: the host keeps a private
pending-target field before publishing a newly constructed target to its owned
list, and removes its transferred clients from the unowned list before publication.
Partial failure therefore still has an explicit cleanup owner. Ordered try/finally
cleanup attempts remaining targets and unowned clients even after a failure;
cleanup errors emit the existing safe target/type line asynchronously and throw a
named safe comparison failure without retaining a credential-bearing inner error.
Successful exits and native registration stay exact. A real child-process failure
case with unusable endpoints proves the new failure boundary without doubles or
measured load; private exactly-once/partial-transfer details retain the explicit
source-review evidence exception under AC-HOST-004.
