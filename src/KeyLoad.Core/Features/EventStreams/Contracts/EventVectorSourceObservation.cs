namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorSourceObservation.SerializerAlias)]
internal sealed record EventVectorSourceObservation(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid OriginalSourcePhaseCommandId,
    [property: Orleans.Id(2)] ReadOnlyMemory<byte> OriginalBodyDigest,
    [property: Orleans.Id(3)] EventSourceRef Source,
    [property: Orleans.Id(4)] PhysicalShardRecord SourceOwner,
    [property: Orleans.Id(5)] Guid NodeId,
    [property: Orleans.Id(6)] long ReadGeneration,
    [property: Orleans.Id(7)] long StoreCutPosition,
    [property: Orleans.Id(8)] long AppliedCutPosition,
    [property: Orleans.Id(9)] string PrincipalId,
    [property: Orleans.Id(10)] long PolicyEpoch,
    [property: Orleans.Id(11)] AtomicPartitionPlacementResolution Placement,
    [property: Orleans.Id(12)] ReadOnlyMemory<byte> OriginalStoredOutcome)
{
    internal const int CurrentPinRowField = 13;
    [Orleans.Id(CurrentPinRowField)]
    public ReadOnlyMemory<byte> CurrentPinRowBytes { get; init; }

    internal const string SerializerAlias = "keyload.core.event-vector-source-observation.v1";
}
