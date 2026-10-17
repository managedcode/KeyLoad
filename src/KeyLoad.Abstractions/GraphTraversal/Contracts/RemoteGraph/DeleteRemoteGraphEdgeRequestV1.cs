namespace KeyLoad;

/// <summary>Delete remote graph edge request.</summary>
/// <param name="CommandId">Command id.</param>
/// <param name="SourcePartition">Source partition.</param>
/// <param name="Graph">Graph.</param>
/// <param name="EdgeId">Edge id.</param>
/// <param name="ExpectedRevision">Expected revision.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(DeleteRemoteGraphEdgeRequestV1.Alias)]
public sealed record DeleteRemoteGraphEdgeRequestV1(
    [property: global::Orleans.Id(0)] Guid CommandId,
    [property: global::Orleans.Id(1)] PartitionRef SourcePartition,
    [property: global::Orleans.Id(2)] string Graph,
    [property: global::Orleans.Id(3)] string EdgeId,
    [property: global::Orleans.Id(4)] long? ExpectedRevision = null)
{
    internal const string Alias = "keyload.contract.remote-graph-delete-request.v1";
}
