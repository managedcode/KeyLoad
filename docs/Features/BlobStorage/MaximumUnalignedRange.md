# Inclusive maximum unaligned public Blob ranges

REQ-BLOB-MAX-RANGE-001 refines REQ/AC-BLOB-002 and004 under ADR-038: genuine three-part publication has two65536-byte parts and one-byte tail, total131073. Independent input byte(position)=(position*17+3)%251 defines bytes before any server result. Actual native inclusive MaxRangeBytes65536 admits unaligned windows crossing two parts without truncation. AC-BLOB-MAX-RANGE-001 compares complete BlobReadResult metadata/offset/allbytes through real SDK and official MCP at offsets0,1,65535,65536,65537 with Count65536, plus tail131071/count2 and exactEOF131073/count0. Public equality does not prove native visited-part counts, raw storage cuts or memory measurements.

REQ-BLOB-MAX-RANGE-002 / AC-BLOB-MAX-RANGE-002 require Count65537 at offset0 and offset65538/count65536 to return exact native Validation and existing sanitized official detail with dispatched request identity. AFTER EACH SDK refusal and EACH official refusal, compare full retained current metadata, Complete upload state and every independent healthy range. These are separately authorized public observations, not an atomic cross-client canonical no-effects claim.

REQ-BLOB-MAX-RANGE-003 / AC-BLOB-MAX-RANGE-003 preserve original Begin, three Part and Complete full request/results. Independently bind each receipt to its exact command/atomic partition, positiveposition/quorum and empty mutations. Each SDK and official original replay is immediately bracketed by the complete current range/upload/metadata matrix, preventing later receipts from concealing damage. SHA256 parts and fixed72byte big-endian chain are computed independently; the genuine Begin initial chain is observed, not claimed as an independent InitialHash oracle.

REQ-BLOB-MAX-RANGE-004 / AC-BLOB-MAX-RANGE-004 map BlobMaximumRangePublicTests.InclusiveMaximumUnalignedRangesRefuseOneOverPreserveEveryOriginalReceiptAndTwoClusterColdCycles, one Test/noArguments. Original keyed ClusterFixture owns Aspire cluster and discovered native SDK/official MCP sessions. Original caller scopes join before TWO Kill-all/BeginRestart-all/Restart-all cycles with ordered failure ledgers. Fresh caller scopes repeat exact refusals, ranges and each original receipt. Test/deadline linked cancellation owns every await. No synthetic provider/roles/clock/schema/transport/sharedfixture/API changes; no physical power-loss, endurance or scale claim.

```mermaid
flowchart LR
 A[Three genuine parts] --> B[Inclusive maximum unaligned ranges]
 B --> C[One-over and end-overrun refusal]
 C --> D[Full current matrix after each client refusal]
 D --> E[Each original result replay then full current matrix]
 E --> F[Join callers and two all3 cold repetitions]
```

Canonical slice: Integration BlobStorage Cases/BlobMaximumRangePublicTests, Models/BlobMaximumRangePublicState, Helpers/BlobMaximumRangePublicPublication/Replay/Trial, Assertions/BlobMaximumRangePublicAssertions; docs/Features/BlobStorage/MaximumUnalignedRange.md. Product/server/client/sharedfixture/serializer/frontend changes N/A. Existing PartialReadBoundary is7byte cross-edge and three length-invalid requests; ArchiveActive checks aligned fullparts and4byte cross-edge; BoundedRetirement aligned65536+tail. This bounded source inventory proves a distinct missing maximum-unaligned public lifecycle, not a product defect/global coverage assertion. Requirements precede code preview. SourceY/compiler/nativeUID/runtime/exact-source Linux cluster gates remain OPEN.
