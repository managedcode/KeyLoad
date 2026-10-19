# Public correction-only chunk tag admission

This child specification owns one public acceptance slice of REQ/AC-SERIES-CHUNK-008/009/010/012. Unit correction-only-tag controls and existing chunk-worker maintenance/revocation controls remain unchanged and separately required. No production defect or runtime result is claimed by this source packet.

## Exact operation and independent oracle

`SampleChunkCorrectionPredicateClusterTests.CorrectionOnlyTagHitMissAndWholeLimitRefusalSurviveMergeReplayAndTwoColdOwners` starts the existing fixture-owned replicated-cluster Aspire topology and waits for all actual nodes. The unchanged creator fixture persists the real principal, credential and series. That creator opens the real window, appends equal-UTC rows with values2/4 and alternate offset, then seals the original generation with visible17 tags.

A genuine late signed-zero append with visible23 leaves a pending correction until the Orleans `[StatelessWorker]` chunk worker flushes its buffer on its timer (the typed flush-interval option) and writes the merge through the normal grain -> ZoneTree owner path; the reads below run while that correction is pending. The first complete expected generation is g1/r5/source3; the base inventory has no visible23, while the real correction does. This observes the real chunk worker, not a synthetic worker, provider or scheduling replacement.

| Criterion | Complete public oracle |
| --- | --- |
| Correction-only candidate | SDK and official MCP `/visible=23`, Limit1 return exactly the original late signed-zero record/sequence3, exact tags, window bounds, floor absence and generation/revision/source. |
| Predicate miss | `/visible=99`, Limit1 returns the complete same header and no records. |
| Complete result bound | Unfiltered Limit1 returns complete BudgetExceeded and no SDK result; official MCP returns the exact safe problem without secret/tag disclosure. No partial prefix is accepted. |
| Conservation after every selected response | Regardless of a primary assertion failure, complete chunk/raw/latest/aggregate/windows/retention reads and the entire scoped outbox status are checked. Independent literal raw values are2/4/-0, sequences1/2/3, aggregate count3/sum6/min-0/max4/average2, absent floor. The four legitimate mutations produce tail/stored count4 with first1, positive stored bytes and no consumers. |
| Actual healthy transition | After the chunk worker's timer flush, observe the actual merge g2/r6/source3. All rows and predicates remain exact; the one legitimate merge advances outbox count to5. |
| Replay and cold | Both callers replay the actual original seal and correction command GUID/body and compare their complete retained receipts. Conservation follows each replay. Two joined same-volume cold owner cycles retain the exact complete result, signed-zero bits, original receipts and full stable outbox status. |

The real creator executes the correction; only the real administrator reads full outbox metadata. Existing current persisted authorization, request execution, signed discovery and original parent deadline remain in force. Physical CutPosition must be positive; it is never compared with a potentially different receipt coordinate. Expected dynamic window bounds come from the originally chosen public window, not from the returned result. Receipt positions are retained original observations; fixed receipt command/mutation/durability fields are checked before replay.

## Scope, ownership and qualification

Only six new TimeSeries Integration roles and this child specification are selected. Product, Core format/options, shared ClusterFixture, chunk-worker registration, host/lifecycle, test transport, SIMD and libraries remain unchanged. Backend/frontend changes are N/A: this is public operation qualification of the existing implementation. SDK/MCP and infrastructure reuse existing surfaces; no public route or compatibility mode is introduced.

The existing Aspire replicated-cluster fixture and RequestCqrsClusterCallers own all actual startup, clients, worker settlement, process shutdown and root retention. The whole body and each response/conservation pair preserve independent failures through the existing failure ledger and join owned shutdown. No fake close failure or no-op disposal is accepted.

```mermaid
flowchart LR
  Seed[Actual creator open append seal visible17] --> Late[Actual visible23 correction]
  Late --> Pending[Pending correction g1 r5]
  Pending --> Reads[SDK and MCP hit miss whole-limit refusal]
  Reads --> Merge[Chunk worker timer flush: merge g2 r6]
  Merge --> Replay[Full models outbox original receipt replay]
  Replay --> Cold[Two joined same-volume cold cycles]
```
