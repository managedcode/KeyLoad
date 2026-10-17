namespace KeyLoad;

/// <summary>Upsert remote graph edge request.</summary>
/// <param name="CommandId">Command id.</param>
/// <param name="SourcePartition">Source partition.</param>
/// <param name="Edge">Edge.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(UpsertRemoteGraphEdgeRequestV1.Alias)]
public sealed record UpsertRemoteGraphEdgeRequestV1(
    [property: global::Orleans.Id(0)] Guid CommandId,
    [property: global::Orleans.Id(1)] PartitionRef SourcePartition,
    [property: global::Orleans.Id(2)] UpsertEdge Edge)
{
    internal const string Alias = "keyload.contract.remote-graph-upsert-request.v1";
}
