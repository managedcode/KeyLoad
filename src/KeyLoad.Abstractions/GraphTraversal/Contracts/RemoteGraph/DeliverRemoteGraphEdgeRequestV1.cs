namespace KeyLoad;

/// <summary>Deliver remote graph edge request.</summary>
/// <param name="CommandId">Command id.</param>
/// <param name="SourcePartition">Source partition.</param>
/// <param name="Graph">Graph.</param>
/// <param name="EdgeId">Edge id.</param>
/// <param name="Destination">Destination.</param>
/// <param name="ExpectedRevision">Expected revision.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(DeliverRemoteGraphEdgeRequestV1.Alias)]
public sealed record DeliverRemoteGraphEdgeRequestV1(
    [property: global::Orleans.Id(0)] Guid CommandId,
    [property: global::Orleans.Id(1)] PartitionRef SourcePartition,
    [property: global::Orleans.Id(2)] string Graph,
    [property: global::Orleans.Id(3)] string EdgeId,
    [property: global::Orleans.Id(4)] EntityRef Destination,
    [property: global::Orleans.Id(5)] long ExpectedRevision)
{
    internal const string Alias = "keyload.contract.remote-graph-deliver-request.v1";
}
