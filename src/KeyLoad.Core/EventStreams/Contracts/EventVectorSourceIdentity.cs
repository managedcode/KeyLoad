namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorSourceIdentity.SerializerAlias)]
internal sealed record EventVectorSourceIdentity(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid MapId,
    [property: Orleans.Id(2)] PartitionRef ControlPartition,
    [property: Orleans.Id(3)] Guid ControlIncarnation,
    [property: Orleans.Id(4)] long MapRevision,
    [property: Orleans.Id(5)] long CoverageGeneration,
    [property: Orleans.Id(6)] string PrincipalId,
    [property: Orleans.Id(7)] EventVectorSourcePhaseRole Role,
    [property: Orleans.Id(8)] int SourceOrdinal,
    [property: Orleans.Id(9)] ReadOnlyMemory<byte> OriginalBodyDigest,
    [property: Orleans.Id(10)] long CleanupGeneration)
{
    internal const string SerializerAlias = "keyload.core.event-vector-source-identity.v1";
}
