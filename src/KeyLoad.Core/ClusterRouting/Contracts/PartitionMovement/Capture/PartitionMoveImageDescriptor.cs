using System.Collections.Immutable;

namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.DescriptorAlias)]
internal sealed record PartitionMoveImageDescriptor(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid MoveId,
    [property: Orleans.Id(2)] PartitionRef Partition,
    [property: Orleans.Id(3)] AtomicPartitionPlacementResolution SourcePlacement,
    [property: Orleans.Id(4)] long SourceCut,
    [property: Orleans.Id(5)] ImmutableArray<PartitionMoveImageFamily> Families,
    [property: Orleans.Id(6)] string Digest,
    [property: Orleans.Id(7)] ImmutableArray<ResourceDefinition> Resources);
