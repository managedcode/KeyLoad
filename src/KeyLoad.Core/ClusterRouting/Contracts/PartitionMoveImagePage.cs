using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.PageAlias)]
internal sealed record PartitionMoveImagePage(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid MoveId,
    [property: Orleans.Id(2)] PartitionRef Partition,
    [property: Orleans.Id(3)] string Family,
    [property: Orleans.Id(4)] int Ordinal,
    [property: Orleans.Id(5)] ImmutableArray<KeyValueRecord> Records,
    [property: Orleans.Id(6)] string Digest);

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.FamilyAlias)]
internal sealed record PartitionMoveImageFamily(
    [property: Orleans.Id(0)] string Family,
    [property: Orleans.Id(1)] long RecordCount,
    [property: Orleans.Id(2)] long RawBytes,
    [property: Orleans.Id(3)] int PageCount,
    [property: Orleans.Id(4)] string Digest);

[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveProtocol.ImageAlias)]
internal sealed record PartitionMoveImage(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid MoveId,
    [property: Orleans.Id(2)] PartitionRef Partition,
    [property: Orleans.Id(3)] AtomicPartitionPlacementResolution SourcePlacement,
    [property: Orleans.Id(4)] long SourceCut,
    [property: Orleans.Id(5)] ImmutableArray<PartitionMoveImageFamily> Families,
    [property: Orleans.Id(6)] ImmutableArray<PartitionMoveImagePage> Pages,
    [property: Orleans.Id(7)] string Digest,
    [property: Orleans.Id(8)] ImmutableArray<ResourceDefinition> Resources);
