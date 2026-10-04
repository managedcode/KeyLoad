# OnlineGenerationLifetime within Search

Accepted L1 implementation for original KL-039 under
[ADR-095](../../ADR/ADR-095-online-generation-leases.md). Native FTS remains a
disposable node-local derived index, with exact source-cut/policy/schema manifests
and the existing selected provider. L1 delivers leased publication/retirement;
base-cut capture, delta catch-up and a background build outside the canonical
read gate are separate required L2 work, not completed by this stage.
The native capture/lifetime L2-A contract is
[NativeReadCuts](../StorageRecovery/NativeReadCuts.md) under ADR-097; it does not
alone provide delta retention, catalog switch or an online-index completion claim.

| Requirement | Acceptance and mapped tests |
|---|---|
| REQ-LEASE-001: publication retains borrowed old generations | AC-LEASE-001: an actual native generation lease remains usable and its owned files remain present while another source-cut generation publishes; current acquisition selects only the fully verified replacement; last old release retires only that generation. `NativeTextGenerationLifetimeTests` |
| REQ-LEASE-002: finite lifetime and admission | AC-LEASE-002: at most2 active leases, one native builder, one retired leased generation and one current generation; at most3 physical generations including the staged builder; native total disk/file/metadata/posting limits account every owned generation. Exact saturation/cancellation releases admission and a following request succeeds |
| REQ-LEASE-003: cleanup and shutdown preserve truth | AC-LEASE-003: failed native refresh/close/delete retains the unsettled owner/handle for joined retry, both primary and cleanup failures survive, repeated release/dispose cannot double-release or delete another generation; shutdown joins leases/build and closes every owned handle. Existing settlement tests and new overlap/shutdown cases |
| REQ-LEASE-004: existing runtime behavior stays exact | AC-LEASE-004: actual SearchEngine/GraphSearch text/vector/native corpus results, visibility/cut mismatch, restart and native process interruption remain exact through the unchanged ITextProjection interface; Aspire full unit/recovery/RF3 and delivered-source Linux gates pass |

No raw borrowed storage view may escape its gate. Native leases operate on their
owned generation; each query still performs current canonical candidate checks
inside its authorized read cut. A source-cut mismatch builds a new generation;
it never borrows an old manifest as current truth. Publication closes/verifies
the staged index and flushes its existing manifest before changing the manager's
current pointer. A retired lease can finish its originally scoped work, not read
new canonical state or make a current-authorization claim.

Replace the lifetime-long single semaphore with a short synchronized manager
transition plus independent bounded lease/build admission. No monitor or store
gate is held across waits or user callbacks; no polling wait on a grain thread.
Keep the existing ITextProjection/lease public shape, faults and generated
identity unchanged. Active generation readers may share only actual provider
operations proven safe; otherwise use one reader per generation and explicit
saturation. Never mutate or close an index borrowed by another lease. The maximum
counts above are ceilings, not permission to invent provider thread safety.

Root owns this contract, cross-cutting docs/joins/gates/receipts. Luna query_wave
owns only Server Features/Search Storage/Lifecycle new helpers and existing
NativeTextProjection/NativeTextProjectionLease/NativeTextSettlement lifetime
joins, NativeTextGenerationFiles.EnsureGenerationCapacity and
NativeTextRootFiles.RetireRestartGenerations capacity/preflight joins, plus new
UnitTests Search Cases/Helpers/Assertions/Models. Prepare a private
patch against3985008 while root tests that immutable compilation. Do not alter
provider APIs, Query read-cut ownership, canonical outbox/data epoch/public wire,
shared configuration or existing fixtures to hide failures. Root integrates the
actual consumed manager change, reviews and executes Aspire gates before commit.

The overlap fixture owns a linked cancellation source for its admitted native
replacement task. Any observation deadline cancels that source, resumes its
actual posting barrier and joins the original task before disposing the lease
or projection; a second timeout-abandoned wait is not cleanup evidence.

Frontend/new SDK/MCP syntax N/A: unchanged public search surfaces consume this
manager. Migration N/A: no canonical record or persisted index format changes.
Rollback stops the capable node and rebuilds disposable indexes; no canonical
data is deleted. L1 cannot close KL-039's concurrent canonical-write/delta/restart
criteria until L2 and original qualification exist.

```mermaid
flowchart LR
  Build[One verified native builder] --> Current[Publish current generation]
  Current --> Retired[Previous generation retains old lease]
  Retired --> Drain[Last reader settles]
  Drain --> Delete[Verified owned retirement]
```
