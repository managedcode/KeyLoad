# Optional authoritative-model cold-reopen gate

This gate covers the authoritative database models. Public operational analytics
exposes the current hour only: its fresh-report checks are mandatory, while a
changed public window leaves retained derived history explicitly UNQUALIFIED.
An overall case PASS does not qualify durable public analytics history.

## Current local results

The corrected native pair passed on source `57cea6244b78b8f8663ca5e48bfea8e36ca9c91e`.
Single-node cold completed in **104.61 seconds**; RF3 cold completed in
**118.73 seconds** after the accepted single-node result. Each original native
case passed once with zero failures/skips, joined caller/process/resource cleanup,
unchanged source/DLL hashes and the same image-input digest. All original nodes
were killed before any restart; actual restart receipts confirm one and three
new runtime generations.

Both runs preserve all three resources in each catalog model, original
SQL/mixed-command/blob-stage envelopes, full receipts and values, same credentials,
fresh grant revocation/repair and a new healthy write. The native public analytics
reports remained in the same hour on all observed nodes and passed their original
invariants; this does not prove exact historical-row retention or old-hour public
history. The separate native captured-range component remains its own proof.

Original local evaluations under `TestResults/stage61-native-join`:

| Topology | Original terminal evaluation | SHA256 |
| --- | --- | --- |
| One node | `root-r516-all-model-1-node-native-cold-reopen.terminal-evaluation.original.json` | `1b856730af9ebf75c52336f4b52bd9a815900f2f9d255b7bfc59ae3d8cb2f86f` |
| RF3 | `root-r517-all-model-3-node-native-cold-reopen.terminal-evaluation.original.json` | `2e72a40efda3100f9b2174d4c5bf54fb31045558c7dc82ce93e0e92c50ea2369` |

The complete Linux functional lane now includes the original normal pair, native
profile/selector controls and this cold pair, but its actual current-source
execution remains unqualified until original GitHub results pass. These local
results do not close DROP/resource catalog lifecycle, the 105-task plan,
power-loss, security/endurance, performance or release gates.

REQ-DBCOLD-001 defines a separate selected recovery gate after the working
single-instance/RF3 pair. It does not change either existing FunctionalDatabaseGateTests
case, their 10,000-success minimum, their three-resource rotation or their current
execution flow. Both optional cases use the existing 30-minute cancellation bound.
The original current local compiler and runtime gates passed as recorded above;
exact-source Linux delivery remains a separate open gate.

REQ-DBCOLD-002 requires the same original Aspire application, physical roots,
container names and verified source/image selection. ContainerRuntimeControl owns
native fault-command kill receipts, exited identities, native Start acceptance,
changed process generation and health settlement. All nodes exit before any starts;
all native starts are accepted before quorum-health waits. Existing joined shutdown,
retained failure evidence and released-storage checks remain mandatory. One voter is
its actual development acknowledgement topology; three voters are actual RF3.
The native ContainerRuntimeOwnedReceipt scenario namespace is reused unchanged;
no new public, persisted or Orleans serializer alias is introduced.

REQ-DBCOLD-007 requires the explicit cold-only test profile. StartColdAsync passes
both --KeyLoadTests:FunctionalColdReopen=true and the existing
--KeyLoadTests:ContainerFaults=true argument with FunctionalNodeCount exactly 1 or 3.
The owning AppHost validator admits only the exact value true without nested children;
it rejects a cold selection without faults or an admitted functional node count. All
existing ephemeral=true, absolute owned data-root and exclusive-profile restrictions
remain. Ordinary StartAsync selects cold=false and supplies neither argument, so the
existing working single-instance/RF3 gates retain their no-fault profiles.

This profile depends on the exact selected owning AppHost source correction and its
native-command selection regressions. F1 found that the r3 fixture omitted native fault
command registration while the prior functional validator also rejected ContainerFaults;
r3 and its earlier clean source-review claims are superseded for that blocker. Adding
only ContainerFaults, bypassing the validator or invoking Docker directly does not repair
that original integration seam. The corrected local compiler/runtime evidence
above supersedes that blocker; current-source Linux delivery remains open.

REQ-DBCOLD-008 extends the existing manual Build and Tests workflow task
functional-gates, preserving the original normal single-instance case then its
original receipt, followed by the normal RF3 case then its original receipt. The
same task must next run the six native FunctionalColdReopenProfileTests controls
and four native NativeTUnitSelectionTests controls with strict TRX evaluation and
no skips. It then runs the exact single-instance cold case with a strict native
one-case result and original receipt, followed by the exact RF3 cold case and its
receipt. Existing source/executable/image verification, joined cleanup and every
other mandatory suite remain unchanged. No ignored SourceN helper, new workflow
or fabricated 10,000-operation cold counter is an execution substitute. Linux CI
runtime qualification remains OPEN until the actual original workflow outcomes.


REQ-DBCOLD-003 provisions three real resources in each advertised model family.
Cold-only persisted writer, reader and tenant-local queue operator identities retain
their original private secrets in memory. All original SDK and official MCP caller
scopes join disposal before kill; new discovered public sessions use those same
credentials after restart. No identity or fixture is replaced and no store is copied.
The original three-identity cold ledgers and source-bound results above remain
historical evidence for their original scope. The current selected cold scenario
adds five persisted identities to those same writer, reader and queue operator
identities, for exactly eight identities total, under REQ-DBCOLD-005. Registering
eight identities or passing the original three-identity cases does not establish
current eight-user cold concurrency.

| Model family | Actual native request and original retained result |
| --- | --- |
| Documents | PutDocument in CommandRequest, full CommitReceipt, GetDocumentRequest |
| Typed tables | ScriptDOM CREATE TABLE SqlOperationRequest -> complete ResourceDefinition; original INSERT SqlOperationRequest with GUID and parameters -> full CommitReceipt; actual SDK/MCP SELECT -> complete projected rows |
| Event stream sets | AppendEvents; ReadStreamRequest with literal head/data and retained complete EventRecord |
| Work queues | EnqueueMessage; InspectMessageRequest with literal state/payload/header and retained complete metadata |
| Topics | PublishTopic; ReadEventSourceRequest with literal head/data and retained complete SourceEventRecord |
| Graphs | UpsertEdge and TraverseRequest; complete literal edge, canonical endpoints and vertex set |
| Time series | AppendSamples, ReadSamplesRequest and AggregateSamplesRequest; two samples, canonical anchor tags and literal count/sum/extrema/average |
| Blobs | Original BeginBlobUploadRequest, two WriteBlobPartRequest and CompleteBlobUploadRequest envelopes and full result/receipt ledger; complete bytes plus boundary/EOF partial BlobReadRequest oracles |
| Vectors | Authoritative VectorFieldProfile, PutVector and native exact SearchRequest; literal complete ranked document |
| Full text | Native collection text field, PutDocument, WaitForIndexRequest and SearchRequest; original acknowledged minimum, current schema/policy and literal complete ranked document |

REQ-DBCOLD-004 retains original mixed-command GUIDs, actual SQL statements and
parameters, blob stage GUIDs and every complete public receipt. After all-node cold
reopen both public clients replay these exact envelopes and compare complete original
results. Read oracles independently verify full model contents and canonical links.
Variable server-recorded event timestamps/sequences and queue sequence metadata are
validated initially and retained exactly for post-cold comparison. Read cuts may advance;
source heads, complete records, expected contents and minimum acknowledged cuts remain
required. Text freshness uses the original token; exact vector search uses its native
canonical read contract. No ANN consumer/generation is provisioned here, so this gate
makes no approximate-search generation recovery claim.

REQ-DBCOLD-005 requires one fixed eight-identity cold workload: the original
writer, reader and queue operator plus five additional persisted identities. The
queue operator is one of the eight, not a ninth caller. Each identity uses its own
actual owned .NET SDK and official MCP sessions and its unchanged persisted grants.
Before restart, all eight sessions reach one readiness gate before the bounded
`Task.WhenAll` wave releases. The five additional identities write their distinct
existing collection resources and retain complete original receipts; the original
three identities exercise their distinct original model resources, including writer
receipt replay. Typed joins in the concurrent wave are read-only; the existing
mutable constraint checks remain sequential and preserve all original rows.

The wave uses the original test cancellation/deadline. A caller failure cancels
its peers, preserves original and cleanup failures, and joins all eight tasks and
owned client disposals before the original Aspire-owned same-root restart. After
restart, the same eight credentials open fresh owned sessions and complete another
readiness-gated, joined wave of exact original replay and complete model/typed-join
reads before any policy revocation. Every identity must record more than zero actual
successful SDK operations and more than zero actual successful official MCP
operations in each wave, using fresh per-caller counters. The bounded witness records
eight ready callers, eight completed callers, joined ownership and those counters.
The gate and eight tasks establish overlapping owned caller lifetimes; they do not
claim eight physically simultaneous requests or a throughput result.

The original post-cold authorization checks remain mandatory: denied reader writes,
denial of an original writer command after persisted grant revocation, and the
additional identities' original foreign-resource and revoked-command denials. A
persisted grant repair admits a new healthy command; its cross-client replay and
exact reads pass, and every original model baseline is checked again. Existing
scenario selectors, public endpoints, native ABI, topology profiles, lifecycle,
receipts, limits and deadlines remain unchanged. This scope is the current cold
eight-user scenario, not future user Analytics, DROP or broader lifecycle work.
Source alone does not qualify either cold topology, Linux delivery, the complete
105-task plan or performance.

REQ-DBCOLD-006 checks native operational analytics separately: each actual node owns
one derived native engine. Both real SDK and official MCP dashboard reads always
verify complete fresh plans, counter consistency, same node/incarnation identity
and changed process instance after cold. Retained sample count, AppliedSum and
CommittedSum are compared only when both actual From and To equal the original
public window. Otherwise one bounded structured scenario observation per node records
analyticsHistory=UNQUALIFIED and PublicDashboardWindowChanged with both original and
current windows. A changed current-hour window is an observability limitation, not a
database failure or a TUnit skip. All three authoritative TimeSeries sets and every
other model, original replay, authorization, healthy-write and cleanup assertion remain
mandatory. This gate does not qualify unobservable old-hour public history; explicit
captured-range native engine recovery belongs to the separate
OperationalAnalyticsRootReopenFlow component scope and its own original evidence.
Derived analytics is not a user catalog model, three independent analytics stores,
authoritative database state or a replacement for native OpenTelemetry.

AC-DBCOLD-001 maps to
`FunctionalDatabaseColdReopenTests.SingleInstanceColdReopensAllModelsAndReplaysOriginalPublicCommands`.
AC-DBCOLD-002 maps to
`FunctionalDatabaseColdReopenTests.ThreeNodeColdReopensAllModelsAndReplaysOriginalPublicCommands`.
Each requires its native case PASS without skips, all original receipts, complete
model/read/authentication assertions, cold process receipts and joined cleanup.
AC-DBCOLD-004 maps the existing workflow's fail-closed step/outcome, source/image
and strict TRX bindings to all six native FunctionalColdReopenProfileTests controls,
all four native NativeTUnitSelectionTests controls and the exact two cold methods
named above. A failed, skipped, missing or mismatched original control/case or
receipt cannot qualify its corresponding stage.

AC-DBCOLD-005 maps REQ-DBCOLD-005 to the same two exact
`FunctionalDatabaseColdReopenTests` methods named in AC-DBCOLD-001/002. For each
actual current-source topology result, require native PASS without skips, original
warm and cold witnesses each containing exactly eight ready and completed callers,
all owned callers joined and positive SDK/MCP counters for every caller. Retain the
original same-root restart/process receipts, complete original model and receipt
comparisons, sequential constraint checks, fresh authorization denials/repair,
healthy continuation and joined Aspire shutdown. Missing or failed caller evidence
keeps this criterion open even if an older three-identity run passed. Local runtime
results remain development evidence; exact-source Linux qualification is separate.

AC-DBCOLD-003 keeps either optional gate open after any original failure, unexpected
effect, missing authoritative record, replay mismatch, invalid fresh analytics report
or unknown cleanup result. A changed analytics public window leaves only that derived
history observation UNQUALIFIED and cannot satisfy a retained-history claim.

The original R502 strict solution build failed with seven test-source/analyzer
errors and zero warnings: four named machine-key violations, two unused imports
and the aggregate 233-code-line fixture limit violation. The scoped source repair
uses named protocol constants, removes unused imports and moves cold coordination
and storage-lock assertions to distinct feature-local helpers. It preserves all
original operation and recovery oracles. The repaired R511 full solution build passed locally with zero warnings and zero
errors. The original parallel R503/R504 native runs passed 45 unit cases and three
process/native recovery cases without skips; this includes all six cold-profile
and four selector controls. All contributing production, UnitTests, RecoveryTests
and CrashHost source and DLL/PDB hashes remained exact after the final build, so
those original results are retained without a repeated run. Their original local
evidence is root-r512-cold-build-unit-recovery-contributor-evidence.original.json
under TestResults/stage61-native-join. The corrected local cold pair passed as recorded above; exact-source Linux
qualification remains OPEN. Process-kill proof does not establish power-loss durability, the complete
105-task plan, production readiness or performance.

The first original single-node R505 run failed before any cold action because the
new fixture selected query dialect 2 for VALUES INSERT. The native compiler
requires SqlOperationProtocol.DefaultQueryDialectVersion (1) for this operation.
The fixture now uses that existing constant; SELECT remains dialect 2, and no
production parser, supported-version check, request payload or result oracle was
relaxed. The focused R514 IntegrationTests build passed with zero warnings/errors;
only its DLL/PDB changed. The original failed report is retained. Corrected R516 passed before the matched
R517 RF3 cold run; both original outcomes are recorded above.

The owning contracts remain ADR-117 native test entry, ADR-125 connection execution
and ADR-136 physical partition ownership. No new ADR is required for
REQ-DBCOLD-005: it composes the existing owned public caller lifetimes with a fixed
bounded test wave and retains the same native execution, transport trust, physical
ownership and restart contracts. It introduces no product scheduling primitive,
provider, topology, library, resource limit or compatibility fallback.

```mermaid
flowchart LR
    A[Existing Aspire 1 or 3 voters] --> B[Persist credentials and original model ledgers]
    B --> C[Fixed eight owned SDK MCP callers and complete SQL reads]
    C --> D[Join original caller disposal]
    D --> E[Native kill every original node]
    E --> F[Accept every native Start]
    F --> G[Join health and original runtime receipts]
    G --> H[Same eight credentials joined cold wave and exact replay]
    H --> I[Verify all models and fresh authorization]
    I --> J[Healthy new write and joined shutdown]
```
