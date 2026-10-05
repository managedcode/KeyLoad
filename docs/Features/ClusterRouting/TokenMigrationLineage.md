# ClusterRouting: native placement epochs and outcome association

Status: contract accepted before implementation on 2026-10-05. Implementation and qualification remain open. Canonical slice: ClusterRouting. Related decision: [ADR-017](../../ADR/ADR-017-migration-tokens.md); additive metadata uses the explicit upgrade boundary under [ADR-011](../../ADR/ADR-011-format-upgrades.md).

## Scope decision

This is two directly connected canonical-commit stages toward KL-071/KL-072:

1. **Same-view placement epoch witness.** Commit tokens are issued from the exact existing transaction view; Batch ownership is checked against that same view; currently consumed local remote-transfer commit receipts validate their exact destination placement from the same view. No new epoch-changing endpoint is introduced and no `>1` movement claim is made.
2. **Outcome scope association.** Preserve the existing global `(verifiedPrincipal, commandId)` outcome key as the only dedup lookup. Add additive typed metadata and a partition-scoped locator written atomically for fully decoded partition operations so a future transfer can enumerate, verify, and carry those outcomes without guessing. Global and unknown-scope records remain distinct and are not silently moved.

Together these stages do not implement split/merge. The one-default-shard catalog, existing admission envelope gaps, global authority, quorum barriers, cross-group transport, restartable transfer protocol, and offline compatibility remain blockers. Physical movement stays disabled.

## Requirements and acceptance criteria

- `REQ-MTOKEN-001`: All commit-token issuance reads the authoritative placement witness through the exact current `IAtomicTransaction`/`IKeyValueView`; no nested store read, stale cache, caller-supplied role, or invented epoch fallback.
  - `AC-MTOKEN-001`: Real ZoneTree tests use an explicitly bootstrapped real physical-shard catalog. Fallback and explicit default rows issue epoch 1 from the correct full partition witness. Missing catalog, orphan row, malformed catalog/row, and row/header mismatch fail closed. No write/outbox/receipt mutation publishes if witness resolution fails.
- `REQ-MTOKEN-002`: Existing Batch ownership claims are checked against the exact same transaction view's placement epoch.
  - `AC-MTOKEN-002`: A real ZoneTree current-epoch Batch commits; stale/future claims return `OwnershipLost`; failed requests preserve domain data and outbox effects; the existing atomic apply path may persist its domain-failure outcome and advance the apply cut. Missing/corrupt authority aborts at the existing corruption boundary without a false success. The sole current catalog only permits initial epoch 1, so no acceptance claim for a movement-created epoch is made in this stage.
- `REQ-MTOKEN-003`: A consumed commit token binds its actual issuer incarnation, full atomic partition identity and same-view placement epoch. No direct source/destination log-position comparison or implicit token translation is allowed.
  - `AC-MTOKEN-003`: Current native queue-transfer receipt consumers reject wrong incarnation, partition, position, or placement epoch with the existing safe `TokenInvalidated`/persisted-corruption categories at their existing boundaries; replay and already-processed result semantics remain unchanged. Cross-group receipt validation is N/A until authenticated destination-owner authority is frozen.
- `REQ-PMOVE-005`: Every newly persisted command outcome has explicit scope classification derived from its normalized typed payload, while global dedup identity and outcome replay remain unchanged.
  - `AC-PMOVE-005`: Actual ZoneTree commits persist `Partition` scope and exactly one locator key for each valid partition-scoped operation, including domain-failure outcomes; global operations persist `Global` scope and no locator. Decode failure, missing/invalid partition, and unknown operation persist `Unknown` scope and no guessed locator. Same principal+command ID across partitions retains its existing fingerprint conflict/replay behavior; success and error outcomes retain exact results. All reads agree with current behavior.
- `REQ-PMOVE-006`: A future mover can enumerate partition-scoped outcome candidates without scanning/guessing global command identities, and incomplete legacy/global authority remains a fail-closed blocker.
  - `AC-PMOVE-006`: The canonical outcome key remains `KeySpace.Outcome(principal,id)`. A bounded partition family `outcome-locator-v1` stores `(full PartitionRef, principal, commandId)` locator keys atomically with each scoped outcome; tests cross-check each locator with the canonical global outcome scope metadata and reject missing/extra/mismatched locators. Global and Unknown rows are never emitted as movable partition records. Old native outcome frames missing new fields decode as `Unknown`; they remain readable/deduplicable, are never automatically rewritten, and make affected-store movement ineligible until an explicit ADR-011 offline migration has complete verified source history and an immutable backup. No retained history means no backfill and no movement.
- `REQ-MTOKEN-004`: Ownership changes are monotonic; unverifiable old token positions are explicitly invalidated instead of translated or compared across separate Raft groups.
  - `AC-MTOKEN-004`: No epoch-bump or cross-group cutover API is included. Tests verify stale-vs-current semantics only through the actual placement witness; source code and docs record that translation remains N/A until accepted durable lineage/sequence and destination quorum/read-barrier protocols exist.
- `REQ-MTOKEN-005`: The current generated Orleans reader consumes an actual six-field committed outcome from the immutable prior native6 producer, preserving its known result.
  - `AC-MTOKEN-005`: The recovery AppHost prepares revision `2801b03091efc5cf45b1268c6570457539f12f27`, tree `678ac682c90294306a0ae4092c4c80b382c4a18b`, through its existing prior-probe prerequisite. That exact source has the existing StoredOutcome alias and IDs 0..5, including CompositionAuthority at ID5. A bounded prior-process operation applies a small real ConfigureResource command with a test-owned principal and command ID, then captures the exact committed value at `KeySpace.Outcome(principal,id)` from that same ZoneTree store. The parent verifies the existing archive/tree, overlay-driver and published-binary receipts before launching the existing bounded process, inserts the returned bytes unchanged under that canonical key in a separately owned current ZoneTree store, and asserts the known result through `DatabaseEngine.Outcome`. No current-code frame producer, rewritten fields, JSON outcome or replacement codec can satisfy this criterion.
- `REQ-MTOKEN-006`: Missing appended fields in that same genuine prior frame have the exact native defaults; malformed frames and unverified provenance fail closed.
  - `AC-MTOKEN-006`: Decode the same unchanged frame with current `NativeSerialization.Deserialize<StoredOutcome>` and assert `ScopeKind == Unknown`, `Partition == null`, unchanged prior identity/result fields and no partition locator or rewrite after the public outcome read. Add only `KeyLoad.RecoveryTests` to the existing Core test-visibility list for these internal assertions; expose no product API. Raw frames are nonempty and at most 16,384 bytes, Base64 transport at most 21,848 characters, within the existing 65,536-character process-output bound. Wrong epoch/source, invalid Base64, oversized output/frame, nonzero child exit, truncated native frame and missing/mismatched artifact receipts fail the case without current-source fallback. Native5 cannot be substituted for the six-field producer. Retain original artifacts and distinguish local process interoperability from Linux/RF3, mixed-writer, endurance and power-loss qualification.

- `REQ-MTOKEN-007`: Resolve Batch authorization and its issued receipt/outbox tokens from one validated placement witness in the same owned apply transaction. Retain no witness across requests, transaction resets, recovery or ownership changes.
  - `AC-MTOKEN-007`: Authorization compares the typed Batch claim to the resolved placement epoch, including replay admission, without a literal epoch fallback. The same immutable transaction-local witness issues the Batch receipt token, reused for every effect's outbox token. A native document mutation adds exactly the three bounded catalog/directory/row reads to the previous six point reads, independent of document payload size; paired-size real ZoneTree counter cases and batch/outbox epoch regressions prove this. Non-Batch mutation groups resolve their token once per group. Missing/corrupt authority aborts without domain effects, and stale/future claims preserve the existing OwnershipLost result. No public token fields, global cache, authorization bypass or cross-group position translation is added.

## Exhaustive operation outcome classification

The new selector consumes only `operation.NativePayload` after the existing normalizer/authority verification. It never infers a partition from command ID, result bytes, or hashes.

Partition-scoped (extract complete partition field only): `Batch.CommandRequest.Partition`; `Receive/Delivery/Processing.Lane.Partition`; subscription configure/seek/receive/delivery/processing/pause `Subscription.Source.Partition`; projection configure/commit/release `Consumer.Partition`; `PurgeOutbox.Partition`; and all six Blob requests' `Blob.Partition`.

Global: resource, principal, API-key, dispatch, membership, physical-catalog bootstrap, and placement-bind outcomes. Placement bind contains a partition but mutates global catalog authority; it is not a movable user-partition outcome.

Unknown: decode failure marker; malformed/null required partition; unsupported/future operation kind; or malformed native payload. When the existing normalizer/apply path persists an outcome for that input, preserve its original error and write no locator; inputs rejected before commit remain rejected before commit. Any newly added operation kind must be explicitly classified before it can be movable.

## Forward-only native format and upgrade

Preserve the existing `StoredOutcome` alias and field IDs 0..5. Append
`StoredOutcomeFields.ScopeKind=6` and nullable full `PartitionRef` at
`StoredOutcomeFields.Partition=7`. `CommandOutcomeScopeKind` has explicit native
`int` values `Unknown=0`, `Global=1`, `Partition=2`, with append-only allocation
and stable alias `keyload.command-outcome-scope-kind.v1` through the native Orleans
generator. Its primitive numeric representation stays unchanged; unsupported
generator attributes must fail the build rather than introduce a new codec.
Missing fields in old Orleans binary frames default to Unknown/null; unrecognized
numeric scope values fail Corruption. No alternate JSON decoding, replacement key
or current global replay-key migration is allowed.

Qualification must cover native round-trip scope/partition fields, legacy missing
fields and unrecognized numeric values using actual generated serializers. Until
native unknown-field retention is proven, prohibit mixed-version outcome writers
and old-writer rollback after scoped outcomes exist. Roll out a cold-compatible
fleet with writers stopped, retaining existing committed outcomes, metadata and
locators. A compatible reader may recover forward; an old writer may not silently
discard the new scope. Immutable backup/history requirements for any offline
backfill remain separate under ADR-011.

For each new scoped outcome, the canonical outcome row and partition locator are written in the existing atomic apply transaction, including a persisted domain failure. For Global/Unknown, no locator is written. Replay verifies a present scope against the normalized typed operation and treats contradictory metadata/index as Corruption; it does not “repair” a locator implicitly. A previously Unknown row stays Unknown on replay to avoid automatic data rewrites. Command principal and ID uniqueness/conflict rules are unchanged.

Any backfill is a separate explicit ADR-011 offline, quiescent, writer-excluded procedure with immutable backup/fingerprint, exact retained canonical-history coverage, atomic replace/receipt and verified reopen. A profile/outcome scan alone cannot reconstruct missing partition scope. If history is incomplete, retain Unknown and keep movement inadmissible. No current command executes this upgrade.

## Exact code/test ownership

- `src/KeyLoad.Core/Features/ClusterRouting/Queries/AtomicPartitionPlacementReader.cs`: add internal same-view epoch/owner-witness helper by reusing the existing validated same-view resolver; do not duplicate row/catalog serialization or authorize via public admin query.
- `src/KeyLoad.Core/DatabaseEngine.cs`: remove unsafe two-argument `Token` helper; replace with `Token(IKeyValueView view, PartitionRef partition, long position)` requiring current resolver. Preserve `CommitToken` alias/field IDs.
- Existing token producers pass the currently owned transaction: `src/KeyLoad.Core/OperationDispatcher.cs`, `src/KeyLoad.Core/AtomicMutationApplication.cs`, `src/KeyLoad.Core/Features/Messaging/Commands/QueueReadyClaims.cs`, `QueueDeliveryTransition.cs`, `RemoteTransferCommands.cs`, `src/KeyLoad.Core/Features/Messaging/Execution/SubscriptionGroups.cs`, `src/KeyLoad.Core/Features/ChangeFeeds/Execution/ProjectionOutbox.cs`, and `src/KeyLoad.Core/Features/BlobStorage/Execution/BlobStorageOperations.cs`.
- `src/KeyLoad.Core/Features/Authorization/Commands/CommandAuthorization.cs`: validate existing Batch epoch using the incoming same view. This does not pretend other public operation DTOs already carry expected epochs; their future trusted epoch-bearing admission envelope is part of the later movement contract.
- `AtomicCommandCommit.cs` retains the ephemeral Batch witness returned by authorization only for that existing apply transaction; `OperationDispatcher.cs` requires it for the Batch token and passes that token into `AtomicMutationApplication.cs`. Reset/domain-failure and replay paths do not retain or reuse it. The latter resolves at most one token for other mutation groups, preserving the same transaction view. Root owns this shared join and the exact DocumentStorage read-count regression; workers do not change the frozen public/native formats.
- Existing token consumers update without new transport: `Features/Messaging/Commands/RemoteTransferCommands.cs`, `Features/Messaging/Execution/RemoteTransferReauthorization.cs`, `Features/Messaging/Execution/RemoteTransferInspection.cs`. Same-view destination validation applies only while current source/destination use the same physical database authority.
- `src/KeyLoad.Core/Features/InternalSerialization/Serialization/CoreNativeFieldIds.cs` and `Contracts/CoreNativeContracts.cs`: additive outcome scope fields only; retain alias/old field IDs.
- Core ClusterRouting `Identity/CommandOutcomePartitionIdentity.cs` (typed operation classification), `Serialization/CommandOutcomePartitionLocatorSerialization.cs` (bounded native index key), and `Contracts/CommandOutcomePartitionScope.cs` (enum/value contract). `KeySpace` receives the partition-locator key constructor without changing the existing Outcome key.
- `src/KeyLoad.Core/AtomicCommandCommit.cs` and `src/KeyLoad.Core/CommandOutcomes.cs`: persist/cross-check metadata and index in existing commit/replay path; no second dispatcher.
- `PartitionRecordFamilies` adds the scoped outcome locator family; global `outcome` remains excluded from ordinary partition page inventory.
- UnitTests: shared `TestDatabase`/`RemoteTransferDatabase` explicitly bootstrap a test-owned physical catalog tuple with stable, independent voter identities; add focused `ClusterRouting/Cases/PartitionOwnershipEpochTests.cs`, `CommandOutcomePartitionScopeTests.cs`, and role-local helpers. Extend Messaging remote-transfer receipt cases to prove same-view owner tuple/epoch and preserve existing error/replay oracle. Use actual ZoneTree store, real generated Orleans records, current TestDatabase boundaries, no mocks.
- Genuine prior-frame proof uses the existing shared StorageRecovery probe infrastructure: `scripts/Features/StorageRecovery/build-prior-probe.sh`, CrashHost `Features/StorageRecovery/Helpers/EpochPriorSourceProbe.cs` and `Fixtures/EpochUpgradeFixture.cs`, and RecoveryTests `EpochPriorExecutableArtifact`/`EpochPriorExecutableProcess`. Keep immutable provider/serializer/project source untouched; the existing receipt hashes every overlaid driver. The new consumer case and its bounded receipt/serialization helpers belong under `tests/KeyLoad.RecoveryTests/Features/ClusterRouting/{Cases,Contracts,Serialization,Helpers}/`. Core `Features/InternalSerialization/Execution/CoreTestVisibility.cs` owns only the additive test friendship. Root freezes this contract before Luna implements the private packet and before required Aspire recovery qualification.
- Documentation integration owner: root updates `docs/Features/ClusterRouting/PartitionTransfer.md`, `docs/ADR/ADR-017-migration-tokens.md`, and only if required `docs/ADR/ADR-011-format-upgrades.md`. Root joins the feature/ADR amendment before any source implementation.

## Movement and token claims still deferred

No physical partition movement, copy/catch-up, source/destination barrier, owner CAS/switch, restartable phase persistence, rollback, or cleanup endpoint is introduced. The current `AtomicPartitionPlacementV1` validator requires row owner tuple and epoch equal the single default physical shard; startup accepts only the initial epoch. Current requests beyond Batch carry no ownership epoch, so a trusted versioned epoch-bearing operation envelope is required before all write paths can be fenced. Old/global outcome authority remains nonmovable without cross-group global dedup/authorization ownership. No translation between independent replica-log positions exists. KL-071 and KL-072 remain open until those contracts and actual RF3 process-cut gates are delivered.

Public SDK/MCP, frontend and SQL surfaces: N/A for this native commit/metadata stage. No new public operation, token field, movement command or client role is introduced. Actual movement remains subject to its future RF3 and process-cut qualification.
