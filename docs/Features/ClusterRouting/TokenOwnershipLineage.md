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
