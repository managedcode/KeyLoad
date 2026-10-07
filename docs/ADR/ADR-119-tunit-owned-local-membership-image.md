# ADR-119: TUnit-owned local membership image

Status: Accepted; implementation and native runtime qualification pending.

Feature contracts: TestInfrastructure REQ/AC-TEST-015, NativeTUnitEntry
REQ/AC-TUNIT-ENTRY-005 and ClusterRouting/PartitionTransfer
AC-MEMBERSHIP-001/002/006. This refines ADR-074/117 prerequisite ownership and
ADR-106 six-silo membership. No public database, dependency, persistence or
membership protocol changes are authorized.

The native caller selects `Suite=rf3`, a nonblank bounded filter and
`LocalRf3Image:Enabled=true`. Initially the only supported owned-image filter is
`/*/*/TwoRf3MembershipProfileTests/*`; reject other filters instead of silently
running an unowned local topology. `scripts/Features/TestInfrastructure/run-tests.mjs`
only validates selection and passes bounded JSON through
`KEYLOAD_TUNIT_LOCAL_RF3_IMAGE_ARGUMENTS`. It never starts Aspire or Docker.
Reject mixed native coverage/comparison/protocol selectors and GitHub receipt,
revision or actions identity; do not erase provenance to admit local mode.

The membership TUnit case owns a `LocalRf3ImageTestSession` followed by a
`TwoRf3MembershipWave`, in that disposal order. The session reads the explicit
native preparation arguments and creates the existing testing builder for the
RF3 suite/filter/local-image prerequisite. Read the original validated runner
configuration, remove the recursive runner before building or starting, and run
only `LocalRf3ImagePrerequisite` to its original successful exit. Reuse
`LocalRf3ImageExecution`, the original source snapshot producer/verifier and
`LocalRf3ImageCleanup`; do not create another image implementation. Capture the
canonical reference/tag/receipt as a typed selection, verify its actual current
receipt/config ID, and pass it explicitly to the wave. Never mutate process-global
environment or fabricate a registry digest for a Docker config ID.

With no owned-local selection, preserve the existing authenticated GitHub image
path. A selected malformed local identity fails before cluster startup without a
GitHub fallback. AppHost's `TwoRf3ClusterResources` uses the existing
`LocalDevelopmentContainerImage` branch for all six fixed names and preserves
all current ephemeral/no-benchmark/no-probe/no-cohort gates. Every child builder
receives only the original validated local selector arguments. The existing
strict GitHub digest oracle remains unchanged.

Extend `LocalRf3ImageIdentity` with explicit expected-name overloads, preserving
ordinary three-node wrappers. Validate the exact distinct nonempty expected set,
all matching model nodes and repository/tag/no-digest annotations before start.
After the unchanged six-node healthy barrier, inspect the actual six
`ContainerNameAnnotation` names through the existing native Docker helper and
require the exact receipt-owned image config ID and configured reference on each.
Preserve the existing six-member Orleans view, two three-voter groups and actual
SDK/official MCP no-dispatch operations through nodes 1 and 4.

Stages and ownership:
1. Root freezes this contract and its feature/ADR links before source work.
2. Luna unpack_atomicity prepares guarded source under AppHost
   Features/ClusterRouting/Resources/TwoRf3ClusterResources.cs; IntegrationTests
   Features/ClusterReplication/Fixtures/LocalRf3ImageTestSession.cs and cohesive
   feature-local helpers as needed, Helpers/LocalRf3ImageSelection.cs and
   LocalRf3ImageIdentity.cs; ClusterRouting/Helpers/TwoRf3MembershipWave.cs,
   Cases/TwoRf3MembershipProfileTests.cs and applicable model negatives; and the
   native selection script plus UnitTests/TestInfrastructure native selection
   whole-process regressions. Root owns shared composition and final joins.
3. Root reviews every source/contract guard, compiles with published packages,
   and runs native positive and negative selections. Actual local preparation,
   six started containers, membership, SDK/MCP rejection and full cleanup must be
   observed in the original TUnit case. Model-only checks cannot qualify runtime.
4. Commit/push and retain exact-source original Linux qualification separately.

Keep the existing 15-minute case/start deadline, 60-second wave cleanup, producer
snapshot admission of 20,000 files/512 MiB and existing image cleanup policy.
Join preparation output readers and application stop/disposal. After wave stop,
application disposal and all 18 exclusive lock checks, invoke exact-tag image
cleanup and settle its original process/readers. Preserve primary and cleanup
failures together. Never force-remove/prune images, delete another invocation,
skip lock errors or weaken the SDK/MCP no-effect oracle.

AC-TUNIT-ENTRY-005 maps to the actual membership case plus native selection and
model admission regressions: accepted selection executes the complete flow;
missing/partial/mixed/GitHub selectors and unsupported filters fail closed before
startup; changed source/image identity fails without fallback; original cleanup
refuses a still-referenced image and removes only its own unused tag. Existing
canonical producer/cleanup whole-operation tests remain mandatory. No new test
may merely inspect a field/getter or duplicate source logic.

Frontend: N/A, test orchestration only. Public contracts: N/A, no server API
change. The image and receipt are disposable local development artifacts. A
failed stage retains original evidence and removes only successfully settled
owned resources. Rollback is an ordinary source revert of this explicit local
refinement, preserving ADR-117 direct TUnit and the strict GitHub route. No data
migration, legacy support, widened deadline or qualification bypass is introduced.
