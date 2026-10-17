using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Complete original logical rows; owner-specific read cuts are checked separately, never erased from receipts.</summary>
internal sealed record PartitionMovementPublicParentRf3ModelCut(DocumentResult? First, DocumentResult? Second,
    DocumentResult? Document, global::KeyLoad.GraphTraversal Graph, global::KeyLoad.GraphTraversal CompositionGraph, RankedDocument[] Vectors,
    StreamHead StreamHead, ImmutableArray<EventRecord> Events, MessageInspection? Work,
    MessageInspection? Forward, MessageInspection? Reverse, SampleRecord[] Samples, BlobReadResult Blob);
