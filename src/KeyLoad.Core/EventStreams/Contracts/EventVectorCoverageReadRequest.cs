namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorCoverageReadRequest.SerializerAlias)]
internal sealed record EventVectorCoverageReadRequest(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] EventFeedControlRequest OriginalRequest,
    [property: Orleans.Id(2)] int GroupOrdinal,
    [property: Orleans.Id(3)] PhysicalShardRecord SourceOwner,
    [property: Orleans.Id(4)] PhysicalShardRecord ControlOwner)
{
    internal const string SerializerAlias = "keyload.core.event-vector-coverage-read-request.v1";
}
