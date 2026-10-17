namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(PlacementCapacityWitnessV1Aliases.PlacementCapacityWitnessV1Alias)]
internal sealed record PlacementCapacityWitnessV1(
    [property: global::Orleans.Id(0)] Guid OriginalRequestId,
    [property: global::Orleans.Id(1)] Guid OriginalQueryNonce,
    [property: global::Orleans.Id(2)] Guid PhysicalShardId,
    [property: global::Orleans.Id(3)] Guid Incarnation,
    [property: global::Orleans.Id(4)] string VoterId,
    [property: global::Orleans.Id(5)] string OriginalSiloAddress,
    [property: global::Orleans.Id(6)] long OriginalSampleSequence,
    [property: global::Orleans.Id(7)] DateTimeOffset OriginalSampledAt,
    [property: global::Orleans.Id(8)] DateTimeOffset OriginalSampleDeadline,
    [property: global::Orleans.Id(9)] long OriginalStorePosition,
    [property: global::Orleans.Id(10)] long OriginalAppliedPosition,
    [property: global::Orleans.Id(11)] string OriginalSnapshotDigest);

internal static class PlacementCapacityWitnessV1Aliases
{
    internal const string PlacementCapacityWitnessV1Alias = "keyload.cluster-routing.placement-capacity-witness.v1";
}
