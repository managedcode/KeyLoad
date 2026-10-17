using System.Collections.Immutable;

namespace KeyLoad;

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
