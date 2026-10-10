namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorControlIdentity.SerializerAlias)]
internal sealed record EventVectorControlIdentity(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid OriginalParentCommandId,
    [property: Orleans.Id(2)] Guid MapId,
    [property: Orleans.Id(3)] PartitionRef ControlPartition,
    [property: Orleans.Id(4)] string PrincipalId,
    [property: Orleans.Id(5)] EventVectorControlAction Action,
    [property: Orleans.Id(6)] long ExpectedMapRevision,
    [property: Orleans.Id(7)] long ExpectedCoverageGeneration,
    [property: Orleans.Id(8)] long ExpectedCleanupGeneration,
    [property: Orleans.Id(9)] Guid SourcePhaseCommandId,
    [property: Orleans.Id(10)] ReadOnlyMemory<byte> OriginalBodyDigest,
    [property: Orleans.Id(11)] ReadOnlyMemory<byte> OriginalSourceOutcomeDigest)
{
    internal const string SerializerAlias = "keyload.core.event-vector-control-identity.v1";
}
