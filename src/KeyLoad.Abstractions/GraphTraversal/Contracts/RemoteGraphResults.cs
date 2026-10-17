using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Remote graph delivery result.</summary>
/// <param name="Destination">Destination.</param>
/// <param name="Revision">Revision.</param>
/// <param name="Deleted">Deleted.</param>
/// <param name="TargetReceipt">Target receipt.</param>
/// <param name="CompletionReceipt">Completion receipt.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphDeliveryResultV1.Alias)]
public sealed record RemoteGraphDeliveryResultV1(
    [property: global::Orleans.Id(0)] EntityRef Destination,
    [property: global::Orleans.Id(1)] long Revision,
    [property: global::Orleans.Id(2)] bool Deleted,
    [property: global::Orleans.Id(3)] CommitReceipt TargetReceipt,
    [property: global::Orleans.Id(4)] CommitReceipt CompletionReceipt)
{
    internal const string Alias = "keyload.contract.remote-graph-delivery-result.v1";
}

/// <summary>Remote graph edge result.</summary>
/// <param name="CommandId">Command id.</param>
/// <param name="SourceReceipt">Source receipt.</param>
/// <param name="Deliveries">Deliveries.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteGraphEdgeResultV1.Alias)]
public sealed record RemoteGraphEdgeResultV1(
    [property: global::Orleans.Id(0)] Guid CommandId,
    [property: global::Orleans.Id(1)] CommitReceipt SourceReceipt,
    [property: global::Orleans.Id(2)] ImmutableArray<RemoteGraphDeliveryResultV1> Deliveries)
{
    internal const string Alias = "keyload.contract.remote-graph-edge-result.v1";
}

/// <summary>Remote incoming graph edge row.</summary>
/// <param name="Edge">Edge.</param>
/// <param name="DeliveredRevision">Delivered revision.</param>
/// <param name="SourceCut">Source cut.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteIncomingGraphEdgeRowV1.Alias)]
public sealed record RemoteIncomingGraphEdgeRowV1(
    [property: global::Orleans.Id(0)] EdgeRecord Edge,
    [property: global::Orleans.Id(1)] long DeliveredRevision,
    [property: global::Orleans.Id(2)] RemoteGraphOwnerCutV1 SourceCut)
{
    internal const string Alias = "keyload.contract.remote-graph-incoming-row.v1";
}

/// <summary>Remote incoming graph edges page.</summary>
/// <param name="Version">Version.</param>
/// <param name="Rows">Rows.</param>
/// <param name="TargetCut">Target cut.</param>
/// <param name="Projection">Projection.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteIncomingGraphEdgesPageV1.Alias)]
public sealed record RemoteIncomingGraphEdgesPageV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] ImmutableArray<RemoteIncomingGraphEdgeRowV1> Rows,
    [property: global::Orleans.Id(2)] RemoteGraphOwnerCutV1 TargetCut,
    [property: global::Orleans.Id(3)] string Projection)
{
    internal const string Alias = "keyload.contract.remote-graph-incoming-page.v1";
}
