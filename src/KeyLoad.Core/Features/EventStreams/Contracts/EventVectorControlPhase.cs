namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorControlPhase.SerializerAlias)]
internal sealed record EventVectorControlPhase(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] EventFeedControlRequest OriginalRequest,
    [property: Orleans.Id(2)] EventVectorControlAction Action,
    [property: Orleans.Id(3)] long ExpectedMapRevision,
    [property: Orleans.Id(4)] long ExpectedCoverageGeneration,
    [property: Orleans.Id(5)] long ExpectedCleanupGeneration,
    [property: Orleans.Id(6)] Guid? ExpectedPendingSourcePhaseId,
    [property: Orleans.Id(7)] EventVectorSourcePhase? IntendedSourcePhase,
    [property: Orleans.Id(8)] EventVectorSourcePhase? ObservedSourcePhase,
    [property: Orleans.Id(9)] ReadOnlyMemory<byte> OriginalEncodedEntries,
    [property: Orleans.Id(10)] ReadOnlyMemory<byte> OriginalEntryChecksum)
{
    internal const int CleanupFrontierField = 13;
    [Orleans.Id(CleanupFrontierField)]
    public ReadOnlyMemory<byte> OriginalCleanupFrontierBytes { get; init; }

    internal const int ParentExpiryField = 12;
    [Orleans.Id(ParentExpiryField)]
    public DateTimeOffset OriginalParentExpiresAt { get; init; }

    internal const string SerializerAlias = "keyload.core.event-vector-control-phase.v1";
    [Orleans.Id(11)]
    public ReadOnlyMemory<byte> OriginalCoverageWitness { get; init; }
}
