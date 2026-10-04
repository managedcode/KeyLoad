# StorageRecovery: epoch7 interpretation fence

Status: accepted implementation contract; KL-043/019 prerequisite for the new
Messaging and Search persisted contracts. [ADR-091](../../ADR/ADR-091-epoch7-interpretation-fence.md)
owns the format decision. Existing ADR-077 native5 evidence remains historical;
this contract extends its positive and negative controls to the new target.

New transfer, recurring/saga and vector-lineage records change what a reader
must understand. An epoch6 reader must never reopen epoch7 and serve projected
vectors without their lineage or route unsupported mutations. The chosen design
uses strict serving admission plus an explicit stopped-copy conversion. Merely
adding optional native fields or changing the RPC capability is insufficient.
An automatic live migration or an in-place identity rewrite is out of scope.

## Closed formats and authority

- Serving identity is exactly epoch7, native checkpoint version5 with a distinct
  magic. Preserve the native identity envelope, WAL4 frame layout and generated
  raw-key/value payload contracts. WAL frame identity is exactly7. Ordinary open,
  recovery, snapshot install, backup/restore and replication never accept5/6 or
  checkpoint3/4. No runtime fallback or partial reader compatibility is added.
- Explicit offline conversion accepts only verified stopped epoch5/WAL4/native3
  or epoch6/WAL4/native4 sources and produces a separate epoch7/native5 target.
  Keep these as named, exact migration-only profiles; choose the profile from
  the checksummed source identity and require every frame/image to match it.
  Preserve native5 controls rather than relabeling native5 as native6. A writer
  for obsolete formats is not retained in the product. Tests use actual immutable
  prior executables for both source profiles.
- Copy exact ordered raw keys/values, NodeId, incarnation, signing bytes, applied
  and log positions, clock, pause/generation, committed configuration, hard state,
  membership and all referenced current images. New metadata uses target7 only.
  Unreferenced history remains byte-identical history with its explicit format;
  it cannot become a current image. Every current referenced image is converted.
- Original inputs are locked and strictly inventoried under the existing
  no-link/regular-file rules. Destination is absent or empty and distinct. Use
  the existing receipt-owned private stage and atomic publication discipline.
  A new receipt revision records actual source5/6 and target7, preventing reuse
  of prior5-to6 stages. Verify the source inventory again before publication;
  unknown/corrupt/torn/mixed source formats fail without publishing.
- Existing explicit server/provider commands remain the entry points. The
  whole-node coordinator records the actual selected source epoch and target7
  in its descriptor, nested receipts and progress. Preparation, verification and
  publication require matching full-node authority; never convert just the
  primary store and abandon replica/current snapshot state.
- AC-EPOCH7-003/004 keeps the original node, canonical and replica owner locks
  continuously exclusive. Authority verification uses bounded private copies of
  only the canonical identity/WAL pairs through the existing stopped-source API;
  it must never reacquire an original owner lock or weaken its sharing mode.
  The same private-directory lifecycle joins primary and cleanup failures and
  deletes its owned copies on success/failure. Existing node acceptance,
  rejection, published-retry and process-cut tests exercise this real path.
- All five serving request/reply/discovery signature purpose families move from
  data-epoch6 to data-epoch7. The bounded CQRS stream wire shape/aliases/IDs remain
  v2; application RequestInterfaceVersion becomes3 and its purpose is
  data-epoch7-rpc3. Valid old-purpose signatures fail before dispatch or consensus
  admission, including an otherwise sufficient reachable old majority.

## Acceptance and mapped tests

| Requirement | Acceptance and evidence |
|---|---|
| REQ-EPOCH7-001: strict interpretation | AC-EPOCH7-001: new serving open rejects real5/6 and unknown identities before mutation; genuine old5 and old6 binaries reject7/checkpoint5; all original files unchanged. `Epoch7PriorReaderTests` plus existing `EpochPriorAcceptanceTests` |
| REQ-EPOCH7-002: exact offline copy | AC-EPOCH7-002: independent native5 and native6 sources convert through the explicit provider command; identities, every raw record, positions and snapshot cuts equal their source oracle; current reopened7 serves preserved data. `Epoch7ConversionTests`, existing upgrade/recovery tests |
| REQ-EPOCH7-003: whole-node preservation | AC-EPOCH7-003: whole-node prepare/verify/publish preserves exact node/replica/configuration/current-image authority for both source profiles; unknown/mixed image and source epochs reject. `Epoch7NodeUpgradeTests` and existing node acceptance/rejection cases |
| REQ-EPOCH7-004: bounded crash-safe publication | AC-EPOCH7-004: real child process kills at each existing upgrade/node stage leave original intact and target absent or wholly7; retry touches only matching private stages and does not erase later current writes. `Epoch7ProcessRecoveryTests`, existing process cases |
| REQ-EPOCH7-005: mixed serving peers fenced | AC-EPOCH7-005: old signed request/discovery/vote/append/snapshot/reply purposes and capability2 reject; compatible3 passes with original native shapes and fixed RF3. Existing cohort/signature controls extended by root; actual Aspire RF3 required |
| REQ-EPOCH7-006: genuine prerequisite ownership | AC-EPOCH7-006: recovery AppHost owns preparation of verified immutable prior5 and prior6 probe resources; test runner waits for successful native completion, original receipts are retained, failure prevents test admission and cleans owned resources. `PriorProbePrerequisiteTests` and actual recovery suite |

Every changed area retains full build, native TUnit unit/scalar/recovery, format,
analyzer/coverage and actual Docker/Aspire RF3 qualification. Local tests are
development evidence. Current baseline recovery25 is219/260 with41 missing
immutable-native5 prerequisite failures; they must be fixed, not skipped.

Probe preparation pins native5 source7784b6b46b98ce994dd98070dc1f58fe4e506b91
and native6 source2801b03091efc5cf45b1268c6570457539f12f27, with their exact Git
trees and original archive hashes. AppHost owns both build resources and passes
`KeyLoadTests__PriorProbesDirectory` for that run's unique results-owned directory
to the recovery runner. It contains native5-probe and native6-probe directories,
each with its explicit native-epoch receipt. Reusing an artifact under another
source or overwriting another run's directory is forbidden. The two isolated
probe builds can run in parallel; both must complete successfully before tests.
`PriorProbePrerequisiteTests` in ComparisonTests/StorageRecovery verifies the
actual recovery AppHost model, both successful-completion dependencies, exact
native command arguments and different results-owned directories per host.
The recovery suite verifies the original source/tree/archive and assembly/driver
hashes before executing either old binary. CI invokes only that recovery
AppHost entry; its original TestResults upload retains both prepared probes and
their receipts. The obsolete separately invoked native5 wrapper is removed.

## Ordered execution and ownership

1. Root freezes this specification and ADR before delegated edits. Luna
   query_wave reads latest role-folder policies, owns Storage.ZoneTree
   StorageRecovery format/admission/explicit converter files, Replication
   StorageRecovery snapshot conversion and Server StorageRecovery coordinator
   joins, plus mapped UnitTests/RecoveryTests StorageRecovery cases and helpers.
   All new executable code uses its actual role folder. Preserve aliases/IDs,
   existing source5 negative controls, finite budgets and source locks.
2. Root owns shared serving protocol/capability purposes, immutable prior probe
   tooling, actual AppHost prerequisite resources, central documents/status,
   public feature joins and Git. No worker edits these shared files or runs gates.
   Workers escalate format/receipt ambiguity rather than invent a third profile.
3. Root reviews combined source, builds with analyzers, runs changed cases via
   actual AppHost while other product agents keep coding, then full gates.
   Record original source/run/attempt/job artifacts and checkpoint the coherent
   stage. No task closes from source or older epoch6 results alone.
4. Rollout keeps all three voters stopped: preserve verified original copies,
   prepare and verify all three complete targets before publishing any, then
   start only homogeneous7/capability3 voters with unchanged placement. If
   publication is interrupted, keep the cluster stopped and resume matching
   targets. Rollback uses a capable7 reader or the complete verified pre-upgrade
   cohort with fenced fresh routing/replication authority; never open7 using6,
   revive stale replicas or discard acknowledged post-upgrade writes.

No new dependency, public client migration endpoint or UI is needed. Storage
conversion is a local stopped-server administrative operation; public SDK/MCP
still exercises preserved data and the newly admitted feature contracts.

TASK-EPOCH7-RECOVERY44-PRIOR-READER refines AC-EPOCH7-002/004: each process retry
must pass its independently selected source epoch through the owned prior-copy
inspection helper to the matching immutable native5 or native6 executable.
Keep exact null error, source revision, data epoch, node identity, applied
position and original inventory assertions. No default native5 reader may be
used to inspect a native6 source. Luna query_wave owns only the process case and
prior inspection helper in a private patch, with root owning integration and
the actual Aspire recovery gate. The separate observed exclusive-lock EAGAIN
failure remains unresolved; this change must not add retries or weaken locks.

```mermaid
flowchart LR
  Original[Locked native5 or native6 source] --> Profile[Exact migration-only profile]
  Profile --> Stage[Receipt-owned separate native7 stage]
  Stage --> Verify[Verify complete node and current images]
  Verify --> Publish[Publish only stopped verified targets]
  Publish --> Serve[Homogeneous epoch7 capability3 RF3]
  Old[Unaware old reader or signed peer] --> Reject[Reject before admission]
```
