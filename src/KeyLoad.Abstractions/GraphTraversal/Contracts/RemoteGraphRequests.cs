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

/// <summary>Read remote incoming graph edges request.</summary>
/// <param name="Version">Version.</param>
/// <param name="Target">Target.</param>
/// <param name="Graph">Graph.</param>
/// <param name="Limit">Limit.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(ReadRemoteIncomingGraphEdgesRequestV1.Alias)]
public sealed record ReadRemoteIncomingGraphEdgesRequestV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] EntityRef Target,
    [property: global::Orleans.Id(2)] string Graph,
    [property: global::Orleans.Id(3)] int Limit)
{
    internal const string Alias = "keyload.contract.remote-graph-incoming-request.v1";
}
