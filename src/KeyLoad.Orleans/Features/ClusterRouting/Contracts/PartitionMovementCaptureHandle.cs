using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Orleans;

/// <summary>Bounded private source capability; canonical source records stay in the owning session.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(PartitionMovementAliases.CaptureHandle)]
internal sealed record PartitionMovementCaptureHandle(
    [property: global::Orleans.Id(0)] Guid HandleId,
    [property: global::Orleans.Id(1)] DateTimeOffset ExpiresAt,
    [property: global::Orleans.Id(2)] PartitionMoveImageDescriptor Descriptor,
    [property: global::Orleans.Id(3)] int PageCount);
