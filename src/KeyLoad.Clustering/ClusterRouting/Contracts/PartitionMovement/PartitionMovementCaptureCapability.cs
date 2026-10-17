using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Clustering;

/// <summary>Original verified grant scope and exact private source capability identity.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementAliases.CaptureCapability)]
internal sealed record PartitionMovementCaptureCapability(
    [property: global::Orleans.Id(0)] PartitionMovementCaptureAction Action,
    [property: global::Orleans.Id(1)] PartitionMovePeerEnvelope Verified,
    [property: global::Orleans.Id(2)] Guid HandleId,
    [property: global::Orleans.Id(3)] int Ordinal);
