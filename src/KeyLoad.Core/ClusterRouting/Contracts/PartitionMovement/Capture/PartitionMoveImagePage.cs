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
