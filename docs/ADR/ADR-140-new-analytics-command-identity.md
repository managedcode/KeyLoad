# ADR-140: Deterministic first issuance of new Analytics command identity

Status: owner-selected contract; prepared source and qualification remain separate.

## Context

New user AnalyticsStore mutations introduce immutable string-keyed dictionaries in canonical Batch commands. `UpsertAnalyticsEvents` contains event Dimensions, Metrics and ExpectedRevisions; `DeleteAnalyticsEvents` contains ExpectedRevisions. These are semantic maps. Their enumeration order is not a command operation order. Native Orleans deserialization reconstructs dictionaries on the receiving process; process-specific string hashes may change enumeration order.

The existing native first-issuance factory serializes a decoded DTO as PayloadJson. Frozen command fingerprints include that JSON as an opaque string. Sorting the outer fingerprint envelope cannot order properties inside this string. Existing retained command identities, original authority signatures, native payloads and outcomes must remain byte exact. The separate typed-content semantic hash does not replace the frozen fingerprint.

## Decision and scope

REQ-ANALYTICS-ID-001: Only newly issued OperationKind.Batch whose actual typed CommandRequest.Mutations contains `UpsertAnalyticsEvents` (`kind: upsertAnalyticsEvents`) or `DeleteAnalyticsEvents` (`kind: deleteAnalyticsEvents`) receives deterministic identity material. The issuer identifies these actual mutation variants; OperationKind.Batch alone, ResourceKind alone, resource names, or substrings in arbitrary user JSON do not select this policy.

REQ-ANALYTICS-ID-002: Canonicalize the complete decoded CommandRequest at its first native issuance, including mixed document/graph/event siblings, using the existing bounded canonical JSON writer. Recursively order object keys ordinally, reject duplicate property names and preserve arrays, mutation/event order, scalar types and number spelling. Use the existing writer's `decimalNumbers: false` mode. Document JSON remains its original string content. No dictionary values, event arrays, links, capabilities or native wire contracts are rewritten.

REQ-ANALYTICS-ID-003: Charge serialization, traversal and output to the actual original issuance work and bounds. Cancellation/deadline and allocation/output limits stay authoritative; no new unlimited buffer, configuration inflation, fallback writer or second budget is introduced. The existing canonical writer owns formatting and configured flushing, while the issuer owns the original ledger and bounded destination.

REQ-ANALYTICS-ID-004: Every non-Analytics operation and every Batch without either selected Analytics mutation retains its original PayloadJson generation byte for byte. Frozen fingerprint algorithms remain unchanged for every operation. Retained issued operations are never rewritten, reissued, canonicalized on replay, or resigned. Original outcome selection and fresh persisted authorization retain their existing semantics. Different map values or different ordered command contents under the same GUID still refuse as Conflict and do not alter the original business outcome.

This is an explicit pre-release contract for new Analytics first issuance, not a migration of retained data or a general change to semantic command equality. Map ordering is not guaranteed by ImmutableDictionary or System.Text.Json; this integration policy belongs to KeyLoad.

```mermaid
flowchart TD
    A[Actual bounded native first issuance] --> B{Batch contains typed Analytics upsert or delete?}
    B -->|yes| C[Existing canonical writer; original budget; preserve numeric spelling]
    B -->|no| D[Original byte-exact identity serializer]
    C --> E[Existing frozen fingerprint and native authority]
    D --> E
    E --> F[Native ordered commit and original outcome]
    G[Already issued operation] --> H[Verify original bytes and authority]
    H --> F
```

## Acceptance and evidence boundaries

AC-ANALYTICS-ID-001: `AnalyticsCommandIdentityTests.FreshAnalyticsIssuanceRetainsMixedReceiptAcrossMapOrderingConflictAndNativeCold(false/true)` newly issues mixed upsert and delete commands from independently rebuilt reversed maps. It checks ordinal map keys against literal names, exact original public receipt, literal complete rows/document, changed-value Conflict with no result, conserved complete native fixture state/outbox/journal and fresh issuance again after a joined same-directory cold reopen. Its retained-index no-append law is specific to the actual native unit fixture, not an assertion about ordinary public cluster retries.

AC-ANALYTICS-ID-002: `NonAnalyticsIssuanceRetainsOriginalJsonAndFrozenFingerprint` verifies the unchanged Batch and ConfigureResource serializer identity and existing frozen outer fingerprint. Existing `NativeOperationFingerprintTests.EveryKindAndEscapeMatchesTheExistingGoldenFingerprintShape` remains required and unchanged.

AC-ANALYTICS-ID-003: The existing FunctionalDatabaseGateTests normal and cold cases must send genuine freshly reconstructed same-GUID Analytics and mixed commands through SDK, official MCP and T-SQL EXEC against their original Aspire-owned one-node/multi-node server processes. They must check full original receipts and complete model state, changed-value Conflict and no effects, then repeat after genuine joined cold restart. Replaying only a retained signed operation does not satisfy this criterion.

AC-ANALYTICS-ID-004: Original-budget cancellation/exhaustion/output-size negatives and healthy following commands must pass with the selected actual issuer. The issuer owner supplies these bound-specific regressions. Native immutable-map framing and same-root native journal cold tests remain independent required coverage.

All these gates remain OPEN until Main adopts the complete coherent source, builds it and retains actual original TUnit/process/public results. This ADR, source inspection and same-process unit tests do not prove cross-process replay, acknowledgement from every configured copys, power-loss durability, performance or production readiness. No status or qualification claim is changed here.

Related contracts: [node-owned derived Analytics](ADR-134-node-owned-derived-analytics.md), [native CreateTable identity](ADR-132-native-create-table-command-identity.md).
