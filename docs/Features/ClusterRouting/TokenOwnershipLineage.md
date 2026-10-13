# ClusterRouting: current ownership tokens and scoped outcomes

Status: current native ownership and outcome contract. Implementation and
qualification status are tracked in the solution status ledger. Canonical slice:
ClusterRouting. Related decisions: [ADR-017](../../ADR/ADR-017-ownership-session-tokens.md),
[ADR-011](../../ADR/ADR-011-current-native-format.md) and
[PhysicalShardCatalog](PhysicalShardCatalog.md).

## Scope and authority

Commit tokens bind the current database incarnation, complete atomic partition,
committed position and validated placement epoch. Atomic partition identity is
separate from physical shard identity. Resolve placement from the exact current
transaction/read view; never infer it from a token, cache it across requests or
compare positions from independent replica groups. The current catalog admits its
configured current placement only. Orleans activation movement preserves the
node-local PartitionHost and does not itself move physical storage or change
canonical token authority.

This feature defines same-view token production/validation and current persisted
command-outcome identity. It does not add a physical partition move, an owner
switch, token translation or a second request dispatcher. Any future movement
must first receive its own accepted authority, sequencing, atomicity, recovery and
RF3 contract.

## Requirements and acceptance

- `REQ-MTOKEN-001`: Resolve the current placement witness through the exact
  existing `IAtomicTransaction`/`IKeyValueView`; do not perform a nested store
  read, use stale cached authority or accept caller-supplied roles or epochs.
  - `AC-MTOKEN-001`: Real ZoneTree operations resolve the configured catalog and
    complete partition witness in the current view. Missing, malformed,
    mismatched or corrupt authority fails before effects or token publication.
- `REQ-MTOKEN-002`: Validate a Batch ownership claim against that same current
  transaction view.
  - `AC-MTOKEN-002`: Current claims commit; stale/future claims return the
    existing `OwnershipLost` result without domain effects. The existing atomic
    apply path may persist the domain-failure outcome and advance its apply cut.
    Missing/corrupt authority aborts at the existing corruption boundary.
- `REQ-MTOKEN-003`: A commit token binds the actual issuer incarnation, full
  atomic partition identity, committed position and validated placement epoch.
  - `AC-MTOKEN-003`: Real native token producers and consumers preserve the
    frozen alias, fields and public behavior. Wrong incarnation, partition,
    position or epoch yields the existing safe invalidation/corruption category;
    no direct cross-group position comparison or implicit translation occurs.
- `REQ-MTOKEN-004`: Validate ownership tokens only against the current
  authoritative placement witness; do not translate a token or compare committed
  positions across independent replica groups.
  - `AC-MTOKEN-004`: Real consumers reject wrong incarnation, partition, position
    or epoch with the existing safe invalidation/corruption result. Current replay
    and already-processed behavior remain unchanged; this contract adds no
    cross-group token translation or owner-switch operation.
- `REQ-PMOVE-005`: Classify each newly committed command outcome from its
  normalized typed operation while retaining the canonical outcome identity and
  replay behavior.
  - `AC-PMOVE-005`: Real ZoneTree commits assign `Partition` only when the
    complete partition is validated, `Global` to global operations and `Unknown`
    when normalization cannot establish a safe scope. Unknown outcomes are
    persisted failed-operation results without a guessed partition or locator.
    An authorized retry of the same normalized operation replays the same
    failure; it cannot shadow a resolved-scope result. Success, domain failure,
    fingerprint conflict, authorization and receipt behavior remain exact.
- `REQ-PMOVE-006`: Store bounded partition-scoped outcome locators atomically
  with the exact matching outcome, without making global or Unknown outcomes
  movable.
  - `AC-PMOVE-006`: Generated native outcomes retain the frozen alias and
    Id0..7 contract. `ScopeKind` uses `Unknown=0`, `Global=1`, `Partition=2`;
    `Partition` contains the full `PartitionRef`. Current outcome keys use the
    exact resolved scope and partition locators contain the matching canonical
    outcome key. Real-store tests reject malformed scope, orphan/mismatched
    locators and corrupt bytes before effects; there is no alternate-key lookup
    or implicit metadata repair.
- `REQ-MTOKEN-007`: Reuse one validated Batch placement witness throughout the
  original atomic apply transaction.
  - `AC-MTOKEN-007`: Authorization resolves the witness once and the receipt
    token plus every effect's outbox token reuse that immutable view-local value.
    Other mutation groups resolve one token per group. Real paired-size ZoneTree
    counters prove exactly the bounded placement reads and unchanged payload
    work; missing/corrupt authority aborts without effects.

## Operation outcome classification

The scope selector consumes only the normalized native operation payload after
signature and persisted authorization checks. It does not infer a partition from
a command ID, result, principal, hash or catalog state.

- **Partition:** `Batch.CommandRequest.Partition`; `ReceiveRequest.Lane.Partition`,
  `DeliveryCommand.Lane.Partition` and `ProcessingRequest.Lane.Partition`;
  configure/seek/receive/delivery/processing/pause subscription source partition;
  configure/commit/release projection consumer partition; `PurgeOutbox.Partition`;
  and the `Blob.Partition` on each of the six blob operations.
- **Global:** resource, principal, API-key, dispatch, membership, physical-catalog
  bootstrap and placement-bind outcomes. Placement bind contains a partition but
  changes global catalog authority, so it is not a partition-scoped user outcome.
- **Unknown:** empty or malformed native payload, decode failure, malformed or
  missing required partition, and unsupported operation kind. If the existing
  normalizer persists a failure outcome, retain its original error with no
  locator. Inputs rejected before commit remain rejected before commit. Every new
  operation kind must be explicitly classified before it can receive Partition
  scope.

## Current native outcome identity

`StoredOutcome` retains its frozen native alias and Id0..5. Id6 is
`ScopeKind` with `Unknown=0`, `Global=1` and `Partition=2`; Id7 is the complete
nullable `PartitionRef`. Unsupported scope values and inconsistent identities
fail as Corruption. Current outcome keys are distinct: partition keys use the
full `PartitionRef` under `outcome-v2`, global keys use the explicit `global`
identity, and Unknown keys use the explicit nonmovable `unknown` identity.
`outcome-locator-v2` contains the complete partition, principal and command ID
plus the exact matching partition outcome key.

The outcome, applicable locator, domain effects, apply watermark and clock commit
in the existing atomic transaction. `ResolveOutcome(originalOperation)` checks
persisted authorization before selecting or decoding a stored result. Replay
resolves the original normalized operation, verifies selected scope, fingerprint,
incarnation and policy epoch, then preserves the original result.
Unknown failures remain distinct, nonmovable results; retrying the same
normalized operation returns the same failure without a locator. Malformed scope,
missing or mismatched locator, contradictory metadata and corrupt native bytes
fail closed without repair or partial effects. Authorization precedes outcome
decoding so a denied caller cannot inspect or expose a stored result.

## Code and test ownership

Core owns the same-view placement resolver, token producers/consumers, typed
operation classification, current outcome identity, locator serialization and
atomic replay validation in `Features/ClusterRouting/`. Abstractions retains
stable public token shape and generated native identities. DocumentStorage owns
its scoped outcome callers and payload/read-count regressions. Real UnitTests use
ZoneTree and generated Orleans records; RecoveryTests preserve current process
reopen and stored-outcome behavior; IntegrationTests use the actual Aspire RF3
.NET SDK and official MCP clients for authorization, retry and receipt flows.

No public token fields, global cache, authorization bypass or physical movement
endpoint is introduced. Current tests must cover positive same-view tokens,
stale/future claims, same-ID retries, changed-content conflicts, Unknown failure
replay, malformed/corrupt outcome state, permission revocation, restart and a
healthy independent operation. Source review is not runtime qualification; all
required build, normal/scalar, recovery and current RF3 gates remain applicable.

## R28 original token after actual owner transfer

REQ-MTOKEN-008 / AC-MTOKEN-008 qualifies the current token-consumer contract across the already accepted ADR106 physical transfer, not token translation. In the same actual protected six-owner cohort, preserve the exact original source CommitReceipt.Token object and literal original record. SDK callers at both configured ingress groups and the official MCP GetDocumentRequest.MinimumToken first read successfully under original authority. Perform the same original public Transfer, requiring actual destination owner/incarnation/epoch/installed receipt and directory publication. Original source token is then TokenInvalidated with no document value through both real SDK ingress callers and official MCP, whose separately dispatched operation identity remains nonempty. Original signed command receipts replay byte exactly and all original models remain exact. Join six physical owners/all18 locks; same-root cold restart repeats token refusal and native terminal receipt replay without raw canonical image/counter/body/index mutation. A new genuine quorum command under destination authority emits its actual destination incarnation/full partition/epoch/positive position token; that token reads the new literal record through SDK and official MCP and survives another full cold reopen. Actual SDK/official MCP/Q1 same-ID new-command replay is exact. No original token field changes, synthetic generation/clock, cross-group position comparisons, authority/cache bypass, source-local fallback, new wire/schema/provider or deadline change is permitted. Original ParentDeadline and joined seed/HTTP/official-MCP/Aspire cleanup preserve every original failure. Local evidence remains development-only; fresh source-bound Linux/RF3 qualification is mandatory.

CurrentOwnershipTokenAfterMovementRf3Tests.OriginalSourceTokenIsRejectedAfterTransferWhileReceiptsReplayAndCurrentOwnerTokenSurvivesCold owns one complete operation flow without Arguments. Source is implemented; compilation, native discovery and genuine Aspire/Docker RF3 execution remain unqualified.

### Original KL-069/KL-072 complete consumer criteria

REQ/AC-MTOKEN-MOVE-CONSUMERS-001 refines AC-MOVE-006 and original KL-072: the existing original token-after-movement declaration also captures a genuine source-owner ReadChangeFeedRequest cursor at the current tail and proves native WaitForIndexRequest success before transfer through both SDK ingress groups and official MCP. After the same accepted whole-partition movement and every existing cold/replay cut, original commit-token waits and original cursor reads must return TokenInvalidated without result values through those same callers. Keep the signed cursor opaque; no cross-owner position comparison or fabricated epoch/expiry is allowed.

Before a new destination-owned command, capture its actual owner cursor. The new literal document emits the existing complete quorum receipt. Native WaitForIndex applied tokens must match that acknowledged destination incarnation/full partition/epoch and be at least its position only within that same owner lineage; schema matches the actual configured resource and fresh policy agrees with the unchanged originally observed policy. Resuming the exact destination cursor returns exactly the one literal new document change and its original commit token before and after the existing full cold cut. Existing complete canonical images, receipts, all model reads, same-ID SDK/MCP/Q1 replay and joined cleanup remain unchanged.

One related original KL-069 admission control uses the existing public whole-partition plan: two distinct movement GUIDs selecting the same complete PartitionRef must throw the existing ArgumentException before enumeration/dispatch and preserve the literal original placement plus complete original model/receipt reads. This is the actual native duplicate-partition admission contract; it creates no fragment/range endpoint, mode or new rejection code, and cannot be represented as every KL-069 criterion passing without the remaining explicit task manifest and exact-source Linux qualification.

The unchanged original case/UID owns this extension; two feature-local helper/assertion roles keep each method below64 code lines and each executable type below200. Product contracts, original ParentDeadline, six-owner Aspire topology, permission checks, query grants, signature keys and failure/cleanup ledger are unchanged. This N-only proposal is not compiler/runtime or whole-task acceptance.

AC-MTOKEN-MOVE-CURSOR-FAMILIES-001 maps each actual consumer independently. Indexed QueryRequest pagination uses the actual by-scope equality index and two literal ordered documents; old-owner continuations refuse CursorExpired, and a captured same-owner query cursor also refuses after the real cold reopen under the native query owner/read-generation fence. LiveQuerySnapshot continuations retain the actual nested change-feed contract: old-owner reads refuse TokenInvalidated, while unchanged-owner cold continuation returns no collection changes. WaitForIndex and change-feed retain their exact TokenInvalidated contract. No cursor is fabricated for ANN, graph traversal or time-series request families that have no such field.

AC-MTOKEN-MOVE-STREAM-CURSOR-001 adds native ReadStreamRequest pagination to the same original six-owner public flow and original auxiliary committed command/receipt. Seed a distinct stream with two literal EventData values within the already configured stream set. Capture actual first-page signed cursors independently through both SDK ingress groups and official MCP, require literal stream/head/revision/event data, and resume each original cursor to the literal second event with the same captured SnapshotCutPosition. After movement and the existing dirty cold/replay cuts, each source-owner cursor refuses CursorExpired with no value; preserve the exact public caller schema and dispatched MCP operation.

Under the destination owner capture fresh genuine stream cursors before the unchanged healthy document command. Continue them before and after the full healthy cold reopen with exact original snapshot cut, head, stream and second event. Native stream cursor claims bind incarnation, full StreamRef, persisted principal/policy/schema, placement epoch, logical placement revision and directory fence; they do not contain query NodeId/ReadGeneration fields, so a same-owner cold refusal is not invented. The original auxiliary command and complete quorum receipt replay through SDK, official MCP and Q1; all original images/model/receipt/cold/healthy controls and joined cleanup remain present. This is an unexecuted N extension; original KL-069 unsupported-fragment criteria and complete KL-072/Linux/RF3 qualification remain explicitly open.

### Explicit local-development image admission for the original six-owner operation

AC-MTOKEN-MOVE-IMAGE-001: The existing protected two-RF3 factory consumes the canonical LocalRf3ImageSelection.Read selection already admitted by the AppHost runner. A missing selection preserves the original authenticated GitHub receipt requirement. A malformed, partial or mixed local/GitHub selection refuses; it must never fall back to an invented GitHub identity or relax image verification.

The existing custom wave owns all six real Docker resources and its protected registered groups, readiness, original parameters/endpoints, node-local storage and joined cleanup. Local selection passes through the existing native wave arguments and bounded local image verifier, source-input digest/original local receipt and immutable actual image-config identity. Verify all six model resources before start and all six actual container image IDs/references after readiness. Same-volume restart uses the same owned selection and original parameter/endpoint bindings. No new local-selector class, wire field, replacement fixture or image bypass is added.

The original CurrentOwnershipTokenAfterMovementRf3Tests.OriginalSourceTokenIsRejectedAfterTransferWhileReceiptsReplayAndCurrentOwnerTokenSurvivesCold case and its complete SDK/official MCP/Q1 movement, specific cursor/WaitForIndex failures, original receipts, full cold cuts, healthy continuation and cleanup assertions remain unchanged. R33's original local failure occurred in GitHub image receipt admission before any operation; it is retained as such and is not evidence of token/placement failure. A successful local execution is development evidence only; the exact-source authenticated Linux/RF3 qualification and other original task criteria remain open.
