using System.Collections.Immutable;

namespace KeyLoad;

internal static class GraphCrossPartitionAliases
{
    internal const string OwnerVersion = "keyload.contract.graph-edge-owner-version.v1";
    internal const string DeliveryIntent = "keyload.contract.graph-cross-partition-delivery-intent.v1";
    internal const string ApplyReverseEdge = "keyload.contract.apply-cross-partition-reverse-edge.v1";
    internal const string CompleteReverseEdge = "keyload.contract.graph-reverse-edge-completion.v1";
    internal const string ReceiverState = "keyload.contract.graph-cross-partition-receiver-state.v1";
    internal const string IncomingRequest = "keyload.contract.graph-incoming-edges-request.v1";
    internal const string IncomingRow = "keyload.contract.graph-incoming-edge-row.v1";
    internal const string IncomingPage = "keyload.contract.graph-incoming-edges-page.v1";
}

internal static class GraphCrossPartitionFields
{
    internal const int OwnerVersion = 0;
    internal const int OwnerRevision = 1;
    internal const int OwnerDeleted = 2;
    internal const int IntentVersion = 0;
    internal const int IntentSourcePartition = 1;
    internal const int IntentGraph = 2;
    internal const int IntentEdgeId = 3;
    internal const int IntentDestination = 4;
    internal const int IntentRevision = 5;
    internal const int IntentDeleted = 6;
    internal const int IntentEdge = 7;
    internal const int IntentPrincipalId = 8;
    internal const int IntentPolicyEpoch = 9;
    internal const int IntentFingerprint = 10;
    internal const int MutationSourcePartition = 0;
    internal const int MutationGraph = 1;
    internal const int MutationEdgeId = 2;
    internal const int MutationDestination = 3;
    internal const int MutationExpectedRevision = 4;
    internal const int ReceiverVersion = 0;
    internal const int ReceiverSourcePartition = 1;
    internal const int ReceiverGraph = 2;
    internal const int ReceiverEdgeId = 3;
    internal const int ReceiverDestination = 4;
    internal const int ReceiverRevision = 5;
    internal const int ReceiverDeleted = 6;
    internal const int ReceiverFingerprint = 7;
    internal const int RequestVersion = 0;
    internal const int RequestTarget = 1;
    internal const int RequestGraph = 2;
    internal const int RequestLimit = 3;
    internal const int RowEdge = 0;
    internal const int DeliveredRevision = 1;
    internal const int PageVersion = 0;
    internal const int PageRows = 1;
    internal const int CutPosition = 2;
    internal const int Projection = 3;
}

/// <summary>Persists the monotonic source-owned edge revision even after deletion.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionAliases.OwnerVersion)]
internal sealed record GraphEdgeOwnerVersionV1(
    [property: Orleans.Id(GraphCrossPartitionFields.OwnerVersion)] int Version,
    [property: Orleans.Id(GraphCrossPartitionFields.OwnerRevision)] long Revision,
    [property: Orleans.Id(GraphCrossPartitionFields.OwnerDeleted)] bool Deleted);

/// <summary>Stores one coalesced desired reverse-projection state per edge destination.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionAliases.DeliveryIntent)]
internal sealed record GraphCrossPartitionDeliveryIntentV1(
    [property: Orleans.Id(GraphCrossPartitionFields.IntentVersion)] int Version,
    [property: Orleans.Id(GraphCrossPartitionFields.IntentSourcePartition)] PartitionRef SourcePartition,
    [property: Orleans.Id(GraphCrossPartitionFields.IntentGraph)] string Graph,
    [property: Orleans.Id(GraphCrossPartitionFields.IntentEdgeId)] string EdgeId,
    [property: Orleans.Id(GraphCrossPartitionFields.IntentDestination)] EntityRef Destination,
    [property: Orleans.Id(GraphCrossPartitionFields.IntentRevision)] long Revision,
    [property: Orleans.Id(GraphCrossPartitionFields.IntentDeleted)] bool Deleted,
    [property: Orleans.Id(GraphCrossPartitionFields.IntentEdge)] EdgeRecord Edge,
    [property: Orleans.Id(GraphCrossPartitionFields.IntentPrincipalId)] string OriginalPrincipalId,
    [property: Orleans.Id(GraphCrossPartitionFields.IntentPolicyEpoch)] long OriginalPolicyEpoch,
    [property: Orleans.Id(GraphCrossPartitionFields.IntentFingerprint)] string Fingerprint);

/// <summary>Locates the current source intent; payload and revision are reloaded from storage.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionAliases.ApplyReverseEdge)]
public sealed record ApplyCrossPartitionReverseEdge(
    [property: Orleans.Id(GraphCrossPartitionFields.MutationSourcePartition)] PartitionRef SourcePartition,
    [property: Orleans.Id(GraphCrossPartitionFields.MutationGraph)] string Graph,
    [property: Orleans.Id(GraphCrossPartitionFields.MutationEdgeId)] string EdgeId,
    [property: Orleans.Id(GraphCrossPartitionFields.MutationDestination)] EntityRef Destination,
    [property: Orleans.Id(GraphCrossPartitionFields.MutationExpectedRevision)] long ExpectedRevision) : Mutation(Graph);

/// <summary>Completes a source intent after reloading its committed target high-water record.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionAliases.CompleteReverseEdge)]
public sealed record CompleteCrossPartitionReverseEdge(
    [property: Orleans.Id(GraphCrossPartitionFields.MutationSourcePartition)] PartitionRef SourcePartition,
    [property: Orleans.Id(GraphCrossPartitionFields.MutationGraph)] string Graph,
    [property: Orleans.Id(GraphCrossPartitionFields.MutationEdgeId)] string EdgeId,
    [property: Orleans.Id(GraphCrossPartitionFields.MutationDestination)] EntityRef Destination,
    [property: Orleans.Id(GraphCrossPartitionFields.MutationExpectedRevision)] long ExpectedRevision) : Mutation(Graph);

/// <summary>Stores the target partition's revisioned derived reverse-edge high-water state.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionAliases.ReceiverState)]
internal sealed record GraphCrossPartitionReceiverStateV1(
    [property: Orleans.Id(GraphCrossPartitionFields.ReceiverVersion)] int Version,
    [property: Orleans.Id(GraphCrossPartitionFields.ReceiverSourcePartition)] PartitionRef SourcePartition,
    [property: Orleans.Id(GraphCrossPartitionFields.ReceiverGraph)] string Graph,
    [property: Orleans.Id(GraphCrossPartitionFields.ReceiverEdgeId)] string EdgeId,
    [property: Orleans.Id(GraphCrossPartitionFields.ReceiverDestination)] EntityRef Destination,
    [property: Orleans.Id(GraphCrossPartitionFields.ReceiverRevision)] long Revision,
    [property: Orleans.Id(GraphCrossPartitionFields.ReceiverDeleted)] bool Deleted,
    [property: Orleans.Id(GraphCrossPartitionFields.ReceiverFingerprint)] string Fingerprint);

/// <summary>Reads the eventual reverse projection for one fully qualified target vertex.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionAliases.IncomingRequest)]
public sealed record ReadIncomingGraphEdgesRequestV1(
    [property: Orleans.Id(GraphCrossPartitionFields.RequestVersion)] int Version,
    [property: Orleans.Id(GraphCrossPartitionFields.RequestTarget)] EntityRef Target,
    [property: Orleans.Id(GraphCrossPartitionFields.RequestGraph)] string Graph,
    [property: Orleans.Id(GraphCrossPartitionFields.RequestLimit)] int Limit);

/// <summary>Contains one canonical source edge validated against a delivered revision.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionAliases.IncomingRow)]
public sealed record GraphIncomingEdgeRowV1(
    [property: Orleans.Id(GraphCrossPartitionFields.RowEdge)] EdgeRecord Edge,
    [property: Orleans.Id(GraphCrossPartitionFields.DeliveredRevision)] long DeliveredRevision);

/// <summary>Marks the bounded reverse projection as eventual, not complete canonical graph truth.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionAliases.IncomingPage)]
public sealed record GraphIncomingEdgesPageV1(
    [property: Orleans.Id(GraphCrossPartitionFields.PageVersion)] int Version,
    [property: Orleans.Id(GraphCrossPartitionFields.PageRows)] ImmutableArray<GraphIncomingEdgeRowV1> Rows,
    [property: Orleans.Id(GraphCrossPartitionFields.CutPosition)] long CutPosition,
    [property: Orleans.Id(GraphCrossPartitionFields.Projection)] string Projection);
